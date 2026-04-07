using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEditor;
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// Material graph construction MCP tool handlers. Provides programmatic
    /// building of PBR material node graphs via binary Visject Surface
    /// serialization, enabling automated material pipeline workflows without
    /// requiring the visual material editor.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Material Graph
        // ==================================================================

        /// <summary>
        /// Builds a complete PBR material with texture parameter nodes wired
        /// to the material output. Supports BaseColor, Normal, ORM (packed
        /// AO/Roughness/Metallic), and Emissive texture maps with float
        /// fallbacks for roughness and metalness.
        /// </summary>
        private string ToolBuildPbrMaterial(Dictionary<string, object> args)
        {
            var outputPath = GetArgString(args, "outputPath");
            if (string.IsNullOrEmpty(outputPath))
                return BuildJsonObject("error", "Missing 'outputPath' argument.");

            var name = GetArgString(args, "name", Path.GetFileNameWithoutExtension(outputPath));
            var roughnessDefault = GetArgFloat(args, "roughnessDefault", 0.5f);
            var metalnessDefault = GetArgFloat(args, "metalnessDefault", 0.0f);

            return InvokeOnMainThread(() =>
            {
                // Create or load the material asset
                var absPath = ResolvePath(outputPath);
                var dir = Path.GetDirectoryName(absPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                bool created = false;
                if (!File.Exists(absPath))
                {
                    if (FlaxEditor.Editor.CreateAsset("Material", absPath))
                        return BuildJsonObject("error", $"Failed to create material at: {outputPath}");
                    created = true;
                }

                var material = FlaxEngine.Content.Load<Material>(absPath);
                if (material == null)
                    return BuildJsonObject("error", $"Failed to load material: {outputPath}");

                // Ensure the material asset is fully loaded before writing surface data
                if (material.WaitForLoaded())
                    return BuildJsonObject("error", $"Material failed to load (timeout): {outputPath}");

                // Build the surface graph binary data
                byte[] surfaceData;
                try
                {
                    surfaceData = BuildPbrSurfaceData(roughnessDefault, metalnessDefault);
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to build surface data: {ex.Message}");
                }

                // Configure material info for standard PBR
                var info = MaterialInfo.CreateDefault();
                info.Domain = MaterialDomain.Surface;
                info.BlendMode = MaterialBlendMode.Opaque;
                info.ShadingModel = MaterialShadingModel.Lit;

                // Save the surface data
                if (material.SaveSurface(surfaceData, info))
                    return BuildJsonObject("error", "Failed to save material surface data.");

                // Reload the material so the editor picks up changes
                material.Reload();

                return BuildJsonObject(
                    "ok", "true",
                    "created", created.ToString().ToLowerInvariant(),
                    "name", name,
                    "path", outputPath,
                    "parameters", "BaseColorMap, NormalMap, ORMMap, EmissiveMap, RoughnessValue, MetalnessValue"
                );
            });
        }

        // ==================================================================
        // Surface graph builder
        // ==================================================================

        // --- VariantType enum values (from FlaxEngine.Utilities.VariantUtils) ---
        // Must match the VariantType enum exactly. Only types needed for PBR
        // material graph construction are defined here.

        private const byte VT_Null = 0;   // VariantType.Null
        private const byte VT_Void = 1;   // VariantType.Void
        private const byte VT_Bool = 2;   // VariantType.Bool
        private const byte VT_Int = 3;    // VariantType.Int
        private const byte VT_Float = 7;  // VariantType.Float
        private const byte VT_Object = 11; // VariantType.Object
        private const byte VT_Asset = 13; // VariantType.Asset
        private const byte VT_Float2 = 16; // VariantType.Float2
        private const byte VT_Float3 = 17; // VariantType.Float3
        private const byte VT_Float4 = 18; // VariantType.Float4
        private const byte VT_Guid = 20;  // VariantType.Guid

        // --- Surface binary builder -------------------------------------

        /// <summary>
        /// Builds the complete Visject Surface binary data for a standard PBR
        /// material graph with six parameters and their connections to the
        /// material output node.
        ///
        /// The binary format exactly matches what Flax's C++
        /// <c>Graph::Load()</c> and the C# <c>LoadGraph()</c> expect for
        /// version 7000 surfaces. ALL boxes must be serialized for every
        /// node — not just connected ones — because the C++ material
        /// generator accesses boxes by ID via <c>GetBox()</c> which ASSERTs
        /// that the box's Parent pointer is set.
        /// </summary>
        private byte[] BuildPbrSurfaceData(float roughnessDefault, float metalnessDefault)
        {
            // Define parameter GUIDs (deterministic for reproducibility)
            var paramBaseColor = new Guid("10000001-0000-0000-0000-000000000001");
            var paramNormal    = new Guid("10000002-0000-0000-0000-000000000002");
            var paramORM       = new Guid("10000003-0000-0000-0000-000000000003");
            var paramEmissive  = new Guid("10000004-0000-0000-0000-000000000004");
            var paramRoughness = new Guid("10000005-0000-0000-0000-000000000005");
            var paramMetalness = new Guid("10000006-0000-0000-0000-000000000006");

            // Node IDs
            const uint nodeIdMaterial  = 1;
            const uint nodeIdBaseColor = 2;
            const uint nodeIdNormal    = 3;
            const uint nodeIdORM       = 4;
            const uint nodeIdEmissive  = 5;
            const uint nodeIdRoughness = 6;
            const uint nodeIdMetalness = 7;

            // Node types: packed as (GroupID << 16) | TypeID
            // GroupID=1 Material, TypeID=1 Material Output
            // GroupID=6 Parameters, TypeID=1 Get Parameter
            const uint typeMaterial = (1 << 16) | 1;
            const uint typeGetParam = (6 << 16) | 1;

            const int nodeCount = 7;
            const int paramCount = 6;

            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                // =============================================================
                // HEADER
                // =============================================================
                w.Write(1963542358);  // Magic
                w.Write(7000);        // Version
                w.Write(nodeCount);   // Node count
                w.Write(paramCount);  // Parameter count

                // =============================================================
                // NODE HEADERS (ID + Type for each)
                // Type is uint32: low 16 bits = TypeID, high 16 bits = GroupID
                // =============================================================
                w.Write(nodeIdMaterial);  w.Write(typeMaterial);
                w.Write(nodeIdBaseColor); w.Write(typeGetParam);
                w.Write(nodeIdNormal);    w.Write(typeGetParam);
                w.Write(nodeIdORM);       w.Write(typeGetParam);
                w.Write(nodeIdEmissive);  w.Write(typeGetParam);
                w.Write(nodeIdRoughness); w.Write(typeGetParam);
                w.Write(nodeIdMetalness); w.Write(typeGetParam);

                // =============================================================
                // PARAMETERS
                // Format per parameter:
                //   WriteVariantType(paramType)
                //   Guid (16 bytes)
                //   WriteStr(name, check=97)
                //   byte isPublic
                //   WriteVariant(defaultValue)
                //   Meta.Save()
                // =============================================================
                WriteSurfaceParam(w, paramBaseColor, "BaseColorMap", "FlaxEngine.Texture");
                WriteSurfaceParam(w, paramNormal,    "NormalMap",    "FlaxEngine.Texture");
                WriteSurfaceParam(w, paramORM,       "ORMMap",       "FlaxEngine.Texture");
                WriteSurfaceParam(w, paramEmissive,  "EmissiveMap",  "FlaxEngine.Texture");
                WriteSurfaceParamFloat(w, paramRoughness, "RoughnessValue", roughnessDefault);
                WriteSurfaceParamFloat(w, paramMetalness, "MetalnessValue", metalnessDefault);

                // =============================================================
                // NODE DATA (values + boxes + meta, for each node)
                // Format per node:
                //   int32 valuesCount
                //   Variant[valuesCount]
                //   ushort boxesCount
                //   Box[boxesCount]  (byte id, VariantType type, ushort connCount, Connection[connCount])
                //   Meta.Save()
                // =============================================================

                // ----- Node 1: Material Output (Group 1, Type 1) -----
                // No values (archetype has no DefaultValues)
                w.Write(0);

                // All 15 input boxes (IDs 0-14). Every box must be written
                // so the C++ GetBox() finds Parent!=null for each.
                //
                // Material node box layout (from Material.cs archetype):
                //   0  = Layer        (void)
                //   1  = Color        (Float3)
                //   2  = Mask         (float)
                //   3  = Emissive     (Float3)
                //   4  = Metalness    (float)
                //   5  = Specular     (float)
                //   6  = Roughness    (float)
                //   7  = AO           (float)
                //   8  = Normal       (Float3)
                //   9  = Opacity      (float)
                //   10 = Refraction   (float)
                //   11 = PosOffset    (Float3)
                //   12 = TessMult     (float)
                //   13 = WorldDisp    (Float3)
                //   14 = SubsurfColor (Float3)
                w.Write((ushort)15);

                // Box 0: Layer (void, no connections)
                WriteBox(w, 0, VT_Void);

                // Box 1: Color <- BaseColor node box 1
                WriteBox(w, 1, VT_Float3, nodeIdBaseColor, 1);

                // Box 2: Mask (no connections)
                WriteBox(w, 2, VT_Float);

                // Box 3: Emissive <- Emissive node box 1
                WriteBox(w, 3, VT_Float3, nodeIdEmissive, 1);

                // Box 4: Metalness <- ORM node box 4 (B channel)
                WriteBox(w, 4, VT_Float, nodeIdORM, 4);

                // Box 5: Specular (no connections)
                WriteBox(w, 5, VT_Float);

                // Box 6: Roughness <- ORM node box 3 (G channel)
                WriteBox(w, 6, VT_Float, nodeIdORM, 3);

                // Box 7: AO <- ORM node box 2 (R channel)
                WriteBox(w, 7, VT_Float, nodeIdORM, 2);

                // Box 8: Normal <- Normal node box 1 (Vector output)
                WriteBox(w, 8, VT_Float3, nodeIdNormal, 1);

                // Box 9: Opacity (no connections)
                WriteBox(w, 9, VT_Float);

                // Box 10: Refraction (no connections)
                WriteBox(w, 10, VT_Float);

                // Box 11: Position Offset (no connections)
                WriteBox(w, 11, VT_Float3);

                // Box 12: Tessellation Multiplier (no connections)
                WriteBox(w, 12, VT_Float);

                // Box 13: World Displacement (no connections)
                WriteBox(w, 13, VT_Float3);

                // Box 14: Subsurface Color (no connections)
                WriteBox(w, 14, VT_Float3);

                // Meta: node position
                WriteNodeMeta(w, 400f, 200f);

                // ----- Node 2: Get Parameter (BaseColorMap) -----
                // Texture parameter: boxes 0(UVs), 6(Object), 1(Color),
                //   2(R), 3(G), 4(B), 5(A)
                // Output box 1 -> Material box 1
                WriteTextureGetParamNode(w, paramBaseColor,
                    1, nodeIdMaterial, 1,
                    0f, -200f);

                // ----- Node 3: Get Parameter (NormalMap) -----
                // Output box 1 -> Material box 8
                WriteTextureGetParamNode(w, paramNormal,
                    1, nodeIdMaterial, 8,
                    0f, 400f);

                // ----- Node 4: Get Parameter (ORMMap) -----
                // Outputs: box 2(R)->Material 7(AO), box 3(G)->Material 6(Roughness),
                //          box 4(B)->Material 4(Metalness)
                WriteOrmGetParamNode(w, paramORM, nodeIdMaterial,
                    0f, 100f);

                // ----- Node 5: Get Parameter (EmissiveMap) -----
                // Output box 1 -> Material box 3
                WriteTextureGetParamNode(w, paramEmissive,
                    1, nodeIdMaterial, 3,
                    0f, -500f);

                // ----- Node 6: Get Parameter (RoughnessValue) -----
                // Float parameter with box 0 (value output), no connections.
                WriteFloatGetParamNode(w, paramRoughness, 0f, 700f);

                // ----- Node 7: Get Parameter (MetalnessValue) -----
                // Float parameter with box 0 (value output), no connections.
                WriteFloatGetParamNode(w, paramMetalness, 0f, 850f);

                // =============================================================
                // VISJECT SURFACE META (view position etc.)
                // =============================================================
                WriteEmptyMeta(w);

                // =============================================================
                // ENDING CHAR
                // =============================================================
                w.Write((byte)'\t');

                return stream.ToArray();
            }
        }

        // ==================================================================
        // Surface binary writing helpers
        // ==================================================================

        /// <summary>
        /// Writes a VariantType to the stream matching the format read by
        /// <c>ReadStream::Read(VariantType&amp;)</c>:
        ///   byte(variantType) + int32(typeNameLength)
        /// For simple types, typeNameLength is 0. For types with an extended
        /// name (Asset subtypes, Object subtypes, Enum, Structure), the
        /// sentinel <c>int.MaxValue</c> is written followed by the XOR-77
        /// ANSI-encoded type name.
        /// </summary>
        private static void WriteVariantType(BinaryWriter w, byte variantType, string typeName = null)
        {
            w.Write(variantType);
            if (typeName != null)
            {
                w.Write(int.MaxValue);
                WriteStrAnsi(w, typeName, 77);
            }
            else
            {
                w.Write(0); // No extended type name
            }
        }

        /// <summary>
        /// Writes a Null variant: type=Null(0), typeNameLength=0, no value data.
        /// </summary>
        private static void WriteVariantNull(BinaryWriter w)
        {
            w.Write((byte)VT_Null);
            w.Write(0);
        }

        /// <summary>
        /// Writes a Float variant: type=Float(7), typeNameLength=0, then float value.
        /// </summary>
        private static void WriteVariantFloat(BinaryWriter w, float value)
        {
            WriteVariantType(w, VT_Float);
            w.Write(value);
        }

        /// <summary>
        /// Writes a Guid variant: type=Guid(20), typeNameLength=0, then 16-byte Guid.
        /// </summary>
        private static void WriteVariantGuid(BinaryWriter w, Guid value)
        {
            WriteVariantType(w, VT_Guid);
            w.Write(value.ToByteArray());
        }

        /// <summary>
        /// Writes a Unicode string XOR-encoded with the given check value,
        /// matching the Flax <c>WriteStr</c> format:
        ///   int32(charCount) + XOR-encoded UTF-16 chars
        /// Each 16-bit char is XORed with <paramref name="check"/>.
        /// </summary>
        private static void WriteStr(BinaryWriter w, string str, int check)
        {
            int length = str?.Length ?? 0;
            w.Write(length);
            if (length == 0)
                return;
            var bytes = Encoding.Unicode.GetBytes(str);
            for (int i = 0; i < length; i++)
            {
                int offset = i * 2;
                ushort c = (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
                c = (ushort)(c ^ (ushort)check);
                bytes[offset] = (byte)(c & 0xFF);
                bytes[offset + 1] = (byte)((c >> 8) & 0xFF);
            }
            w.Write(bytes);
        }

        /// <summary>
        /// Writes an ANSI string XOR-encoded with the given check value,
        /// matching the Flax <c>WriteStrAnsi</c> format:
        ///   int32(byteCount) + XOR-encoded ASCII bytes
        /// Each byte is XORed with <c>(byte)check</c>.
        /// </summary>
        private static void WriteStrAnsi(BinaryWriter w, string str, int check)
        {
            int length = str?.Length ?? 0;
            w.Write(length);
            if (length == 0)
                return;
            var bytes = Encoding.ASCII.GetBytes(str);
            for (int i = 0; i < length; i++)
                bytes[i] = (byte)(bytes[i] ^ (byte)check);
            w.Write(bytes);
        }

        /// <summary>
        /// Writes a single box entry with zero connections.
        /// Format: byte(boxId) + VariantType(boxType) + ushort(0)
        /// </summary>
        private static void WriteBox(BinaryWriter w, byte boxId, byte boxVariantType)
        {
            w.Write(boxId);
            WriteVariantType(w, boxVariantType);
            w.Write((ushort)0); // No connections
        }

        /// <summary>
        /// Writes a single box entry with exactly one connection.
        /// Format: byte(boxId) + VariantType(boxType) + ushort(1)
        ///       + uint32(targetNodeId) + byte(targetBoxId)
        /// </summary>
        private static void WriteBox(BinaryWriter w, byte boxId, byte boxVariantType,
                                     uint targetNodeId, byte targetBoxId)
        {
            w.Write(boxId);
            WriteVariantType(w, boxVariantType);
            w.Write((ushort)1);
            w.Write(targetNodeId);
            w.Write(targetBoxId);
        }

        /// <summary>
        /// Writes a surface parameter definition for a Texture type with a
        /// null default value.
        /// </summary>
        private static void WriteSurfaceParam(BinaryWriter w, Guid id, string name, string assetTypeName)
        {
            // Parameter type: Asset with extended type name
            WriteVariantType(w, VT_Asset, assetTypeName);

            // Parameter ID (Guid, 16 bytes)
            w.Write(id.ToByteArray());

            // Parameter name (WriteStr with check=97)
            WriteStr(w, name, 97);

            // IsPublic (1 byte)
            w.Write((byte)1);

            // Default value: Variant null (no texture assigned)
            WriteVariantNull(w);

            // Meta (empty)
            WriteEmptyMeta(w);
        }

        /// <summary>
        /// Writes a surface parameter definition for a float type with a
        /// specified default value.
        /// </summary>
        private static void WriteSurfaceParamFloat(BinaryWriter w, Guid id, string name, float defaultValue)
        {
            // Parameter type: Float (simple, no extended name)
            WriteVariantType(w, VT_Float);

            // Parameter ID
            w.Write(id.ToByteArray());

            // Parameter name
            WriteStr(w, name, 97);

            // IsPublic
            w.Write((byte)1);

            // Default value: float variant
            WriteVariantFloat(w, defaultValue);

            // Meta (empty)
            WriteEmptyMeta(w);
        }

        /// <summary>
        /// Writes the node data for a Texture Get Parameter node with ALL
        /// boxes serialized. The Texture prototype defines 7 boxes:
        ///   Input:  0 = UVs (Float2)
        ///   Output: 6 = Object ref (Object)
        ///           1 = Color (Float4)
        ///           2 = R (float)
        ///           3 = G (float)
        ///           4 = B (float)
        ///           5 = A (float)
        ///
        /// One output box (<paramref name="connectedBoxId"/>) is connected
        /// to the specified material input. All other boxes are written with
        /// zero connections.
        /// </summary>
        private static void WriteTextureGetParamNode(BinaryWriter w, Guid paramId,
            byte connectedBoxId, uint materialNodeId, byte materialBoxId,
            float posX, float posY)
        {
            // Values: [0] = parameter Guid
            w.Write(1);
            WriteVariantGuid(w, paramId);

            // All 7 boxes for Texture parameter prototype
            w.Write((ushort)7);

            // Box 0: UVs input (Float2, no connections)
            WriteBox(w, 0, VT_Float2);

            // Box 6: Object ref output (Object, no connections)
            WriteBox(w, 6, VT_Object);

            // Box 1: Color output (Float4)
            if (connectedBoxId == 1)
                WriteBox(w, 1, VT_Float4, materialNodeId, materialBoxId);
            else
                WriteBox(w, 1, VT_Float4);

            // Box 2: R output (float)
            if (connectedBoxId == 2)
                WriteBox(w, 2, VT_Float, materialNodeId, materialBoxId);
            else
                WriteBox(w, 2, VT_Float);

            // Box 3: G output (float)
            if (connectedBoxId == 3)
                WriteBox(w, 3, VT_Float, materialNodeId, materialBoxId);
            else
                WriteBox(w, 3, VT_Float);

            // Box 4: B output (float)
            if (connectedBoxId == 4)
                WriteBox(w, 4, VT_Float, materialNodeId, materialBoxId);
            else
                WriteBox(w, 4, VT_Float);

            // Box 5: A output (float)
            if (connectedBoxId == 5)
                WriteBox(w, 5, VT_Float, materialNodeId, materialBoxId);
            else
                WriteBox(w, 5, VT_Float);

            // Meta: node position
            WriteNodeMeta(w, posX, posY);
        }

        /// <summary>
        /// Writes the node data for the ORM Texture Get Parameter node with
        /// ALL boxes serialized. Three output channels are connected:
        ///   Box 2 (R) -> Material AO (box 7)
        ///   Box 3 (G) -> Material Roughness (box 6)
        ///   Box 4 (B) -> Material Metalness (box 4)
        /// </summary>
        private static void WriteOrmGetParamNode(BinaryWriter w, Guid paramId,
            uint materialNodeId, float posX, float posY)
        {
            // Values: [0] = parameter Guid
            w.Write(1);
            WriteVariantGuid(w, paramId);

            // All 7 boxes for Texture parameter prototype
            w.Write((ushort)7);

            // Box 0: UVs input (Float2, no connections)
            WriteBox(w, 0, VT_Float2);

            // Box 6: Object ref output (Object, no connections)
            WriteBox(w, 6, VT_Object);

            // Box 1: Color output (Float4, no connections for ORM)
            WriteBox(w, 1, VT_Float4);

            // Box 2: R output (float) -> Material box 7 (AO)
            WriteBox(w, 2, VT_Float, materialNodeId, 7);

            // Box 3: G output (float) -> Material box 6 (Roughness)
            WriteBox(w, 3, VT_Float, materialNodeId, 6);

            // Box 4: B output (float) -> Material box 4 (Metalness)
            WriteBox(w, 4, VT_Float, materialNodeId, 4);

            // Box 5: A output (float, no connections)
            WriteBox(w, 5, VT_Float);

            // Meta: node position
            WriteNodeMeta(w, posX, posY);
        }

        /// <summary>
        /// Writes the node data for a Float Get Parameter node with its
        /// single output box (ID 0). The node is not connected to anything
        /// — it exists as a user-wirable fallback.
        /// </summary>
        private static void WriteFloatGetParamNode(BinaryWriter w, Guid paramId,
            float posX, float posY)
        {
            // Values: [0] = parameter Guid
            w.Write(1);
            WriteVariantGuid(w, paramId);

            // Float parameter prototype has 1 box:
            //   Output: 0 = Value (float)
            w.Write((ushort)1);
            WriteBox(w, 0, VT_Float);

            // Meta: node position
            WriteNodeMeta(w, posX, posY);
        }

        /// <summary>
        /// Writes a SurfaceMeta block containing a single entry for node
        /// position. TypeID 11 corresponds to <c>Meta11</c>:
        ///   Float2 Position (8 bytes) + bool Selected (4 bytes, marshalled
        ///   as Win32 BOOL via <c>Marshal.SizeOf</c>)
        /// Total data size: 12 bytes.
        /// </summary>
        private static void WriteNodeMeta(BinaryWriter w, float x, float y)
        {
            w.Write(1);       // Entry count
            w.Write(11);      // TypeID = 11 (node position)
            w.Write((long)0); // CreationTime (unused, always 0)
            w.Write((uint)12); // Data size
            w.Write(x);       // Position.X
            w.Write(y);       // Position.Y
            w.Write(0);       // Selected = false (4-byte BOOL)
        }

        /// <summary>
        /// Writes an empty SurfaceMeta block (zero entries).
        /// </summary>
        private static void WriteEmptyMeta(BinaryWriter w)
        {
            w.Write(0); // Entry count = 0
        }
    }
}

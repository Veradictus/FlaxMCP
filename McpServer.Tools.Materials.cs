using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEditor;
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// Material-related MCP tool handlers. Covers material creation, material
    /// instance creation, reading and writing material parameters, and assigning
    /// materials to model actor slots.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Materials
        // ==================================================================

        /// <summary>
        /// Creates a new material asset at the specified output path.
        /// </summary>
        private string ToolCreateMaterial(Dictionary<string, object> args)
        {
            var name = GetArgString(args, "name");
            var outputPath = GetArgString(args, "outputPath");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(outputPath))
                return BuildJsonObject("error", "Missing 'name' or 'outputPath' argument.");

            return InvokeOnMainThread(() =>
            {
                var absPath = Path.Combine(Globals.ProjectFolder, outputPath);
                var dir = Path.GetDirectoryName(absPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                if (FlaxEditor.Editor.CreateAsset("Material", absPath))
                    return BuildJsonObject("error", $"Failed to create material at: {outputPath}");

                return BuildJsonObject(
                    "created", "true",
                    "name", name,
                    "path", outputPath
                );
            });
        }

        /// <summary>
        /// Creates a material instance from a base material with optional parameter overrides.
        /// </summary>
        private string ToolCreateMaterialInstance(Dictionary<string, object> args)
        {
            var name = GetArgString(args, "name");
            var outputPath = GetArgString(args, "outputPath");

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(outputPath))
                return BuildJsonObject("error", "Missing 'name' or 'outputPath' argument.");

            return InvokeOnMainThread(() =>
            {
                var absPath = Path.Combine(Globals.ProjectFolder, outputPath);
                var dir = Path.GetDirectoryName(absPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                if (FlaxEditor.Editor.CreateAsset("MaterialInstance", absPath))
                    return BuildJsonObject("error", $"Failed to create material instance at: {outputPath}");

                var instance = FlaxEngine.Content.Load<MaterialInstance>(outputPath);
                if (instance == null)
                    return BuildJsonObject("error", $"Failed to load created material instance: {outputPath}");

                var baseMaterialPath = GetArgString(args, "baseMaterial");
                if (!string.IsNullOrEmpty(baseMaterialPath))
                {
                    var baseMat = FlaxEngine.Content.Load<MaterialBase>(baseMaterialPath);
                    if (baseMat != null)
                        instance.BaseMaterial = baseMat;
                    else
                        Debug.LogWarning($"[McpServer] Base material not found: {baseMaterialPath}");
                }

                if (args.TryGetValue("parameters", out var paramsObj) && paramsObj is Dictionary<string, object> paramDict)
                {
                    foreach (var kvp in paramDict)
                    {
                        try
                        {
                            SetMaterialParameter(instance, kvp.Key, kvp.Value);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[McpServer] Failed to set parameter '{kvp.Key}': {ex.Message}");
                        }
                    }
                }

                instance.Save();

                return BuildJsonObject(
                    "created", "true",
                    "name", name,
                    "path", outputPath
                );
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Material Properties
        // ==================================================================

        /// <summary>
        /// Gets all parameters of a material or material instance.
        /// </summary>
        private string ToolGetMaterialParams(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var material = FlaxEngine.Content.Load<MaterialBase>(path);
                if (material == null)
                    return BuildJsonObject("error", $"Material not found: {path}");

                var parameters = material.Parameters;
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"path\": {JsonEscape(path)},");
                sb.AppendLine($"  \"type\": {JsonEscape(material.GetType().Name)},");
                sb.AppendLine($"  \"parameterCount\": {parameters.Length},");
                sb.AppendLine("  \"parameters\": [");

                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(param.Name)},");
                    sb.AppendLine($"      \"type\": {JsonEscape(param.ParameterType.ToString())},");
                    sb.AppendLine($"      \"isPublic\": {(param.IsPublic ? "true" : "false")},");

                    string valStr;
                    try
                    {
                        var val = param.Value;
                        valStr = val?.ToString() ?? "null";
                    }
                    catch
                    {
                        valStr = "<error>";
                    }

                    sb.AppendLine($"      \"value\": {JsonEscape(valStr)}");
                    sb.Append("    }");
                    if (i < parameters.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        /// <summary>
        /// Sets a parameter on a material instance. Supports float, integer, bool,
        /// color (r,g,b,a), and texture content paths.
        /// </summary>
        private string ToolSetMaterialParam(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            var paramName = GetArgString(args, "paramName");
            var valueStr = GetArgString(args, "value");

            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");
            if (string.IsNullOrEmpty(paramName))
                return BuildJsonObject("error", "Missing 'paramName' argument.");
            if (valueStr == null)
                return BuildJsonObject("error", "Missing 'value' argument.");

            return InvokeOnMainThread(() =>
            {
                var material = FlaxEngine.Content.Load<MaterialBase>(path);
                if (material == null)
                    return BuildJsonObject("error", $"Material not found: {path}");

                var instance = material as MaterialInstance;
                if (instance == null)
                    return BuildJsonObject("error", $"Asset at {path} is not a MaterialInstance. Only material instances support parameter overrides.");

                // Find the parameter to determine its type
                var parameters = instance.Parameters;
                MaterialParameter targetParam = null;
                foreach (var p in parameters)
                {
                    if (string.Equals(p.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetParam = p;
                        break;
                    }
                }

                if (targetParam == null)
                    return BuildJsonObject("error", $"Parameter '{paramName}' not found on material: {path}");

                try
                {
                    switch (targetParam.ParameterType)
                    {
                        case MaterialParameterType.Float:
                            if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var fVal))
                                instance.SetParameterValue(paramName, fVal);
                            else
                                return BuildJsonObject("error", $"Cannot parse '{valueStr}' as float.");
                            break;

                        case MaterialParameterType.Integer:
                            if (int.TryParse(valueStr, out var iVal))
                                instance.SetParameterValue(paramName, iVal);
                            else
                                return BuildJsonObject("error", $"Cannot parse '{valueStr}' as integer.");
                            break;

                        case MaterialParameterType.Bool:
                            if (bool.TryParse(valueStr, out var bVal))
                                instance.SetParameterValue(paramName, bVal);
                            else
                                return BuildJsonObject("error", $"Cannot parse '{valueStr}' as bool.");
                            break;

                        case MaterialParameterType.Color:
                            // Expect "r,g,b,a" format
                            var parts = valueStr.Split(',');
                            if (parts.Length >= 3)
                            {
                                var color = new Color(
                                    ParseFloat(parts[0].Trim(), 1f),
                                    ParseFloat(parts[1].Trim(), 1f),
                                    ParseFloat(parts[2].Trim(), 1f),
                                    parts.Length >= 4 ? ParseFloat(parts[3].Trim(), 1f) : 1f
                                );
                                instance.SetParameterValue(paramName, color);
                            }
                            else
                            {
                                return BuildJsonObject("error", "Color format: r,g,b,a (e.g. 1.0,0.5,0.0,1.0)");
                            }
                            break;

                        case MaterialParameterType.Texture:
                        case MaterialParameterType.NormalMap:
                        case MaterialParameterType.CubeTexture:
                            var texture = FlaxEngine.Content.Load<Texture>(valueStr);
                            if (texture != null)
                                instance.SetParameterValue(paramName, texture);
                            else
                                return BuildJsonObject("error", $"Texture not found: {valueStr}");
                            break;

                        default:
                            // Try setting as string for other types
                            instance.SetParameterValue(paramName, valueStr);
                            break;
                    }

                    instance.Save();

                    return BuildJsonObject(
                        "ok", "true",
                        "path", path,
                        "paramName", paramName,
                        "value", valueStr
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to set parameter: {ex.Message}");
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Model Materials
        // ==================================================================

        /// <summary>
        /// Assigns a material to a specific slot on a StaticModel or AnimatedModel.
        /// </summary>
        private string ToolSetModelMaterial(Dictionary<string, object> args)
        {
            var materialPath = GetArgString(args, "materialPath");
            if (string.IsNullOrEmpty(materialPath))
                return BuildJsonObject("error", "Missing 'materialPath' argument.");

            var slotIndex = GetArgInt(args, "slotIndex", 0);

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var material = FlaxEngine.Content.Load<MaterialBase>(ResolvePath(materialPath));
                if (material == null)
                    return BuildJsonObject("error", $"Material not found: {materialPath}");

                if (actor is StaticModel staticModel)
                {
                    staticModel.SetMaterial(slotIndex, material);
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "slotIndex", slotIndex.ToString(),
                        "material", materialPath
                    );
                }
                else if (actor is AnimatedModel animatedModel)
                {
                    animatedModel.SetMaterial(slotIndex, material);
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "slotIndex", slotIndex.ToString(),
                        "material", materialPath
                    );
                }

                return BuildJsonObject("error", $"Actor '{actor.Name}' is not a StaticModel or AnimatedModel (type: {actor.GetType().Name}).");
            });
        }
    }
}

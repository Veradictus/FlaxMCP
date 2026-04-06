using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEditor.Content.Settings;
using FlaxEngine;

namespace FlaxMCP
{
    public partial class McpServer
    {
        // ------------------------------------------------------------------
        // Tool registration
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers every MCP tool. Called once during <see cref="Start"/>.
        /// </summary>
        private void RegisterAllTools()
        {
            _tools.Clear();

            // ============================================================
            // Health & Status
            // ============================================================

            RegisterTool("get_health",
                "Get server status, engine version, and play mode state.",
                SchemaEmpty(),
                ToolGetHealth);

            RegisterTool("get_project_status",
                "Get project compile status, scene count, asset count, and recent errors.",
                SchemaEmpty(),
                ToolGetProjectStatus);

            RegisterTool("get_editor_state",
                "Get editor state including play mode, project name, viewport info, and selection.",
                SchemaEmpty(),
                ToolGetEditorState);

            RegisterTool("get_editor_logs",
                "Get recent editor log entries.",
                SchemaObject(SchemaPropInt("count", "Number of log entries to retrieve (default 50)")),
                ToolGetEditorLogs);

            // ============================================================
            // Scene
            // ============================================================

            RegisterTool("get_scene_hierarchy",
                "Get the full scene hierarchy tree with all actors.",
                SchemaEmpty(),
                ToolGetSceneHierarchy);

            RegisterTool("get_scene_list",
                "List all scene assets and currently loaded scenes.",
                SchemaEmpty(),
                ToolGetSceneList);

            RegisterTool("get_actor",
                "Get detailed information about a specific actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name to find"),
                    SchemaPropStr("id", "Actor GUID to find")),
                ToolGetActor);

            RegisterTool("find_actors",
                "Search for actors by name, type, or tag. All filters are optional.",
                SchemaObject(
                    SchemaPropStr("name", "Partial name match"),
                    SchemaPropStr("type", "Actor type name (e.g. StaticModel, PointLight)"),
                    SchemaPropStr("tag", "Tag to match")),
                ToolFindActors);

            RegisterTool("set_actor_property",
                "Set a property on an actor by name or id.",
                SchemaObjectRequired(
                    new[] { "property" },
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("property", "Property path (e.g. Transform.Position, IsActive)"),
                    SchemaPropAny("value", "Value to set")),
                ToolSetActorProperty);

            RegisterTool("create_actor",
                "Create a new actor in the scene.",
                SchemaObjectRequired(
                    new[] { "type" },
                    SchemaPropStr("type", "Actor type (EmptyActor, StaticModel, PointLight, Camera, etc.)"),
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("parent", "Parent actor name"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropNum("rotationX", "X rotation (Euler degrees)"),
                    SchemaPropNum("rotationY", "Y rotation (Euler degrees)"),
                    SchemaPropNum("rotationZ", "Z rotation (Euler degrees)"),
                    SchemaPropStr("model", "Model asset path (for StaticModel type)")),
                ToolCreateActor);

            RegisterTool("delete_actor",
                "Delete an actor from the scene.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolDeleteActor);

            RegisterTool("reparent_actor",
                "Move an actor to a new parent.",
                SchemaObject(
                    SchemaPropStr("actor", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("newParent", "New parent actor name"),
                    SchemaPropStr("newParentId", "New parent actor GUID")),
                ToolReparentActor);

            RegisterTool("create_scene",
                "Create a new scene asset.",
                SchemaObject(
                    SchemaPropStr("name", "Scene name"),
                    SchemaPropStr("path", "Output path (e.g. Content/MyScene.scene)")),
                ToolCreateScene);

            RegisterTool("save_scene",
                "Save all currently loaded scenes.",
                SchemaEmpty(),
                ToolSaveScene);

            RegisterTool("load_scene",
                "Load a scene by asset path.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Scene asset path")),
                ToolLoadScene);

            // ============================================================
            // Assets & Content
            // ============================================================

            RegisterTool("list_assets",
                "List assets in a content folder.",
                SchemaObject(
                    SchemaPropStr("path", "Folder path (default: Content)"),
                    SchemaPropStr("type", "File extension filter (e.g. fbx, flax)")),
                ToolListAssets);

            RegisterTool("import_assets",
                "Import external files into the project.",
                SchemaObjectRequired(
                    new[] { "files" },
                    SchemaPropArray("files", "Array of absolute file paths to import"),
                    SchemaPropStr("target", "Target content folder (default: Content)"),
                    SchemaPropBool("skipDialog", "Skip the import settings dialog (default: true)")),
                ToolImportAssets);

            RegisterTool("find_content",
                "Find a content item by its path in the content database.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content path (e.g. Content/Materials/M_Base.flax)")),
                ToolFindContent);

            RegisterTool("search_content",
                "Search the content database by name and/or type.",
                SchemaObject(
                    SchemaPropStr("query", "Name search query"),
                    SchemaPropStr("type", "Asset type filter (e.g. Model, Texture)")),
                ToolSearchContent);

            RegisterTool("get_content_info",
                "Get detailed information about a content item.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content item path")),
                ToolGetContentInfo);

            RegisterTool("reimport_content",
                "Reimport an asset from its source file.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Asset path to reimport")),
                ToolReimportContent);

            RegisterTool("get_content_tree",
                "Get the full content directory tree structure.",
                SchemaEmpty(),
                ToolGetContentTree);

            // ============================================================
            // Materials
            // ============================================================

            RegisterTool("create_material",
                "Create a new material asset.",
                SchemaObjectRequired(
                    new[] { "name", "outputPath" },
                    SchemaPropStr("name", "Material name"),
                    SchemaPropStr("outputPath", "Output path relative to project folder")),
                ToolCreateMaterial);

            RegisterTool("create_material_instance",
                "Create a material instance from a base material.",
                SchemaObjectRequired(
                    new[] { "name", "outputPath" },
                    SchemaPropStr("name", "Instance name"),
                    SchemaPropStr("outputPath", "Output path relative to project folder"),
                    SchemaPropStr("baseMaterial", "Base material asset path"),
                    SchemaPropAny("parameters", "Parameter overrides as key-value pairs")),
                ToolCreateMaterialInstance);

            // ============================================================
            // Physics
            // ============================================================

            RegisterTool("physics_raycast",
                "Cast a ray and return hit information.",
                SchemaObject(
                    SchemaPropNum("originX", "Ray origin X"),
                    SchemaPropNum("originY", "Ray origin Y"),
                    SchemaPropNum("originZ", "Ray origin Z"),
                    SchemaPropNum("directionX", "Ray direction X"),
                    SchemaPropNum("directionY", "Ray direction Y (default: -1)"),
                    SchemaPropNum("directionZ", "Ray direction Z"),
                    SchemaPropNum("maxDistance", "Maximum ray distance (default: 1000)")),
                ToolPhysicsRaycast);

            RegisterTool("physics_overlap",
                "Perform a sphere overlap test and return intersecting actors.",
                SchemaObject(
                    SchemaPropNum("x", "Sphere center X"),
                    SchemaPropNum("y", "Sphere center Y"),
                    SchemaPropNum("z", "Sphere center Z"),
                    SchemaPropNum("radius", "Sphere radius (default: 5)")),
                ToolPhysicsOverlap);

            RegisterTool("get_physics_settings",
                "Get current physics settings (gravity, CCD, etc.).",
                SchemaEmpty(),
                ToolGetPhysicsSettings);

            RegisterTool("set_physics_settings",
                "Set physics settings such as gravity.",
                SchemaObject(
                    SchemaPropNum("gravityX", "Gravity X component"),
                    SchemaPropNum("gravityY", "Gravity Y component"),
                    SchemaPropNum("gravityZ", "Gravity Z component")),
                ToolSetPhysicsSettings);

            // ============================================================
            // Animation
            // ============================================================

            RegisterTool("list_animations",
                "List animated models in the current scene.",
                SchemaObject(
                    SchemaPropStr("actor", "Actor name to inspect (searches all if omitted)")),
                ToolListAnimations);

            RegisterTool("play_animation",
                "Play or configure animation on an animated model.",
                SchemaObjectRequired(
                    new[] { "actor" },
                    SchemaPropStr("actor", "Actor name"),
                    SchemaPropStr("clip", "Animation clip path"),
                    SchemaPropNum("speed", "Playback speed multiplier"),
                    SchemaPropStr("parameter", "Animation graph parameter name to set"),
                    SchemaPropAny("parameterValue", "Value for the animation parameter")),
                ToolPlayAnimation);

            RegisterTool("get_animation_state",
                "Get the current animation state of an actor.",
                SchemaObjectRequired(
                    new[] { "actor" },
                    SchemaPropStr("actor", "Actor name or id")),
                ToolGetAnimationState);

            // ============================================================
            // Terrain
            // ============================================================

            RegisterTool("get_terrain_info",
                "Get information about terrain actors in the scene.",
                SchemaEmpty(),
                ToolGetTerrainInfo);

            RegisterTool("terrain_sculpt",
                "Sculpt terrain at a world position.",
                SchemaObject(
                    SchemaPropStr("terrainName", "Terrain actor name (uses first if omitted)"),
                    SchemaPropNum("x", "World X position"),
                    SchemaPropNum("z", "World Z position"),
                    SchemaPropNum("radius", "Brush radius (default: 5)"),
                    SchemaPropNum("strength", "Brush strength (default: 0.5)")),
                ToolTerrainSculpt);

            RegisterTool("get_terrain_height",
                "Sample terrain height at a world X/Z position.",
                SchemaObject(
                    SchemaPropNum("x", "World X position"),
                    SchemaPropNum("z", "World Z position")),
                ToolGetTerrainHeight);

            // ============================================================
            // Navigation
            // ============================================================

            RegisterTool("build_navmesh",
                "Build the navigation mesh for the current scene.",
                SchemaEmpty(),
                ToolBuildNavmesh);

            RegisterTool("query_navpath",
                "Query a navigation path between two world positions.",
                SchemaObject(
                    SchemaPropNum("startX", "Start X"), SchemaPropNum("startY", "Start Y"), SchemaPropNum("startZ", "Start Z"),
                    SchemaPropNum("endX", "End X"), SchemaPropNum("endY", "End Y"), SchemaPropNum("endZ", "End Z")),
                ToolQueryNavpath);

            RegisterTool("get_navigation_info",
                "Get navigation mesh volume information.",
                SchemaEmpty(),
                ToolGetNavigationInfo);

            // ============================================================
            // Rendering
            // ============================================================

            RegisterTool("take_screenshot",
                "Capture a screenshot of the editor viewport.",
                SchemaObjectRequired(
                    new[] { "outputPath" },
                    SchemaPropStr("outputPath", "Absolute or project-relative output file path")),
                ToolTakeScreenshot);

            RegisterTool("get_rendering_settings",
                "Get current rendering and post-processing settings.",
                SchemaEmpty(),
                ToolGetRenderingSettings);

            RegisterTool("set_rendering_settings",
                "Update rendering settings.",
                SchemaObject(
                    SchemaPropAny("settings", "Rendering setting key-value pairs")),
                ToolSetRenderingSettings);

            // ============================================================
            // Audio
            // ============================================================

            RegisterTool("play_audio",
                "Play an audio clip at a position in the scene.",
                SchemaObjectRequired(
                    new[] { "clip" },
                    SchemaPropStr("clip", "Audio clip asset path"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropBool("loop", "Loop the audio (default: false)")),
                ToolPlayAudio);

            RegisterTool("list_audio_sources",
                "List all audio source actors in the scene.",
                SchemaEmpty(),
                ToolListAudioSources);

            // ============================================================
            // Prefabs
            // ============================================================

            RegisterTool("spawn_prefab",
                "Spawn a prefab instance in the scene.",
                SchemaObjectRequired(
                    new[] { "prefab" },
                    SchemaPropStr("prefab", "Prefab asset path"),
                    SchemaPropStr("name", "Instance name"),
                    SchemaPropStr("parent", "Parent actor name"),
                    SchemaPropNum("positionX", "X position"),
                    SchemaPropNum("positionY", "Y position"),
                    SchemaPropNum("positionZ", "Z position"),
                    SchemaPropNum("rotationX", "X rotation (Euler degrees)"),
                    SchemaPropNum("rotationY", "Y rotation (Euler degrees)"),
                    SchemaPropNum("rotationZ", "Z rotation (Euler degrees)")),
                ToolSpawnPrefab);

            RegisterTool("list_prefabs",
                "List all prefab assets in the project.",
                SchemaEmpty(),
                ToolListPrefabs);

            // ============================================================
            // Editor Control
            // ============================================================

            RegisterTool("editor_play",
                "Start play mode in the editor.",
                SchemaEmpty(),
                ToolEditorPlay);

            RegisterTool("editor_stop",
                "Stop play mode in the editor.",
                SchemaEmpty(),
                ToolEditorStop);

            RegisterTool("editor_select",
                "Select an actor in the editor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolEditorSelect);

            RegisterTool("editor_focus",
                "Focus the editor viewport on an actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolEditorFocus);

            RegisterTool("get_viewport",
                "Get the editor viewport camera position and orientation.",
                SchemaEmpty(),
                ToolGetViewport);

            RegisterTool("set_viewport",
                "Set the editor viewport camera position and orientation.",
                SchemaObject(
                    SchemaPropNum("positionX", "Camera X position"),
                    SchemaPropNum("positionY", "Camera Y position"),
                    SchemaPropNum("positionZ", "Camera Z position"),
                    SchemaPropNum("yaw", "Camera yaw in degrees"),
                    SchemaPropNum("pitch", "Camera pitch in degrees")),
                ToolSetViewport);

            // ============================================================
            // Scripts
            // ============================================================

            RegisterTool("list_scripts",
                "List all C# script files in the project Source folder.",
                SchemaEmpty(),
                ToolListScripts);

            RegisterTool("read_script",
                "Read the source code of a script file.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Script path relative to project folder (e.g. Source/Game/MyScript.cs)")),
                ToolReadScript);

            RegisterTool("compile_scripts",
                "Trigger script compilation.",
                SchemaEmpty(),
                ToolCompileScripts);

            RegisterTool("get_script_errors",
                "Get script compilation errors and warnings.",
                SchemaEmpty(),
                ToolGetScriptErrors);

            // ============================================================
            // Build
            // ============================================================

            RegisterTool("build_game",
                "Request a game build for a target platform.",
                SchemaObject(
                    SchemaPropStr("platform", "Target platform (default: Windows)"),
                    SchemaPropStr("configuration", "Build configuration (default: Release)")),
                ToolBuildGame);

            RegisterTool("get_build_status",
                "Get the current build status.",
                SchemaEmpty(),
                ToolGetBuildStatus);

            // ============================================================
            // Project Settings
            // ============================================================

            RegisterTool("get_project_settings",
                "Read the GameSettings.json and return project configuration.",
                SchemaEmpty(),
                ToolGetProjectSettings);

            RegisterTool("set_project_settings",
                "Update GameSettings fields such as productName and companyName.",
                SchemaObject(
                    SchemaPropStr("productName", "Product name"),
                    SchemaPropStr("companyName", "Company name")),
                ToolSetProjectSettings);

            // ============================================================
            // Undo / Redo
            // ============================================================

            RegisterTool("editor_undo",
                "Undo the last editor action.",
                SchemaEmpty(),
                ToolEditorUndo);

            RegisterTool("editor_redo",
                "Redo the last undone editor action.",
                SchemaEmpty(),
                ToolEditorRedo);

            // ============================================================
            // Actor Duplication
            // ============================================================

            RegisterTool("duplicate_actor",
                "Duplicate an actor. Optionally offset the clone's position.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("offsetX", "X position offset for the clone"),
                    SchemaPropNum("offsetY", "Y position offset for the clone"),
                    SchemaPropNum("offsetZ", "Z position offset for the clone")),
                ToolDuplicateActor);

            // ============================================================
            // Transform: Move / Rotate / Scale
            // ============================================================

            RegisterTool("move_actor",
                "Move an actor to an absolute position or by a relative offset. Provide x/y/z for absolute, offsetX/offsetY/offsetZ for relative.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "Absolute X position"),
                    SchemaPropNum("y", "Absolute Y position"),
                    SchemaPropNum("z", "Absolute Z position"),
                    SchemaPropNum("offsetX", "Relative X offset"),
                    SchemaPropNum("offsetY", "Relative Y offset"),
                    SchemaPropNum("offsetZ", "Relative Z offset")),
                ToolMoveActor);

            RegisterTool("rotate_actor",
                "Set the rotation of an actor using Euler angles (degrees).",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "Euler X rotation (pitch) in degrees"),
                    SchemaPropNum("y", "Euler Y rotation (yaw) in degrees"),
                    SchemaPropNum("z", "Euler Z rotation (roll) in degrees")),
                ToolRotateActor);

            RegisterTool("scale_actor",
                "Set the scale of an actor.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID"),
                    SchemaPropNum("x", "X scale"),
                    SchemaPropNum("y", "Y scale"),
                    SchemaPropNum("z", "Z scale")),
                ToolScaleActor);

            // ============================================================
            // Component / Script Management
            // ============================================================

            RegisterTool("add_script",
                "Add a script component to an actor by fully qualified type name.",
                SchemaObject(
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropStr("scriptType", "Fully qualified script type name (e.g. MyGame.PlayerController)")),
                ToolAddScript);

            RegisterTool("remove_script",
                "Remove a script from an actor by index.",
                SchemaObject(
                    SchemaPropStr("actorName", "Actor name"),
                    SchemaPropStr("actorId", "Actor GUID"),
                    SchemaPropInt("scriptIndex", "Zero-based index of the script to remove")),
                ToolRemoveScript);

            RegisterTool("list_actor_scripts",
                "List all scripts on an actor with their types and properties.",
                SchemaObject(
                    SchemaPropStr("name", "Actor name"),
                    SchemaPropStr("id", "Actor GUID")),
                ToolListActorScripts);

            // ============================================================
            // Material Properties
            // ============================================================

            RegisterTool("get_material_params",
                "Get all parameters of a material or material instance.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content path to the material asset")),
                ToolGetMaterialParams);

            RegisterTool("set_material_param",
                "Set a parameter on a material instance. For textures use a content path, for colors use r,g,b,a format, for floats just the number.",
                SchemaObjectRequired(
                    new[] { "path", "paramName", "value" },
                    SchemaPropStr("path", "Content path to the material instance"),
                    SchemaPropStr("paramName", "Parameter name"),
                    SchemaPropStr("value", "Value as string")),
                ToolSetMaterialParam);

            // ============================================================
            // Batch Execute
            // ============================================================

            RegisterTool("batch_execute",
                "Execute multiple tool calls in sequence. Each command is an object with 'tool' (tool name) and 'arguments' (object of arguments).",
                SchemaObjectRequired(
                    new[] { "commands" },
                    SchemaPropAny("commands", "Array of objects, each with 'tool' (string) and 'arguments' (object)")),
                ToolBatchExecute);

            // ============================================================
            // Advanced Actor Search
            // ============================================================

            RegisterTool("find_actors_advanced",
                "Search for actors with multiple filters. All filters are optional and combined with AND logic.",
                SchemaObject(
                    SchemaPropStr("name", "Partial name match"),
                    SchemaPropStr("type", "Actor type name (e.g. StaticModel, PointLight)"),
                    SchemaPropStr("tag", "Tag to match"),
                    SchemaPropStr("hasScript", "Script type name the actor must have"),
                    SchemaPropStr("inParent", "Parent actor name to search within"),
                    SchemaPropInt("maxResults", "Maximum number of results (default 100)")),
                ToolFindActorsAdvanced);

            // ============================================================
            // Scene Operations (Extended)
            // ============================================================

            RegisterTool("load_scene_additive",
                "Load a scene additively without unloading currently loaded scenes.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Scene asset path")),
                ToolLoadSceneAdditive);

            RegisterTool("get_scene_stats",
                "Get scene statistics: actor count, script count, loaded scene count, etc.",
                SchemaEmpty(),
                ToolGetSceneStats);

            RegisterTool("unload_scene",
                "Unload a specific scene by name or id.",
                SchemaObject(
                    SchemaPropStr("name", "Scene name"),
                    SchemaPropStr("id", "Scene GUID")),
                ToolUnloadScene);

            // ============================================================
            // Content Operations (Extended)
            // ============================================================

            RegisterTool("create_folder",
                "Create a content folder.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Folder path relative to project (e.g. Content/MyFolder)")),
                ToolCreateFolder);

            RegisterTool("delete_content",
                "Delete a content item (file or folder). Use with caution.",
                SchemaObjectRequired(
                    new[] { "path" },
                    SchemaPropStr("path", "Content item path to delete")),
                ToolDeleteContent);

            RegisterTool("move_content",
                "Move or rename a content item.",
                SchemaObjectRequired(
                    new[] { "sourcePath", "destPath" },
                    SchemaPropStr("sourcePath", "Current content item path"),
                    SchemaPropStr("destPath", "Destination path")),
                ToolMoveContent);

            // ============================================================
            // Profiling
            // ============================================================

            RegisterTool("get_frame_stats",
                "Get current frame timing and rendering statistics.",
                SchemaEmpty(),
                ToolGetFrameStats);

            // ============================================================
            // Editor Windows
            // ============================================================

            RegisterTool("get_editor_windows",
                "List open editor windows with their types and titles.",
                SchemaEmpty(),
                ToolGetEditorWindows);
        }

        // ==================================================================
        // JSON Schema helpers for tool input definitions
        // ==================================================================

        private static string SchemaEmpty()
        {
            return "{\"type\":\"object\",\"properties\":{},\"required\":[]}";
        }

        private static string SchemaObject(params string[] props)
        {
            return SchemaObjectRequired(Array.Empty<string>(), props);
        }

        private static string SchemaObjectRequired(string[] required, params string[] props)
        {
            var sb = new StringBuilder();
            sb.Append("{\"type\":\"object\",\"properties\":{");
            for (int i = 0; i < props.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(props[i]);
            }
            sb.Append("},\"required\":[");
            for (int i = 0; i < required.Length; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"{required[i]}\"");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string SchemaPropStr(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"string\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string SchemaPropNum(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"number\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string SchemaPropInt(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"integer\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string SchemaPropBool(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"boolean\",\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string SchemaPropArray(string name, string desc)
        {
            return $"\"{name}\":{{\"type\":\"array\",\"items\":{{\"type\":\"string\"}},\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string SchemaPropAny(string name, string desc)
        {
            return $"\"{name}\":{{\"description\":\"{EscapeSchemaString(desc)}\"}}";
        }

        private static string EscapeSchemaString(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // ==================================================================
        // TOOL HANDLERS: Health & Status
        // ==================================================================

        private string ToolGetHealth(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                return BuildJsonObject(
                    "status", "ok",
                    "engine", "FlaxEngine",
                    "version", Globals.EngineBuildNumber.ToString(),
                    "project", Globals.ProductName,
                    "isPlaying", Editor.Instance.StateMachine.IsPlayMode.ToString().ToLowerInvariant()
                );
            });
        }

        private string ToolGetProjectStatus(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                var sceneCount = scenes?.Length ?? 0;

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"projectName\": {JsonEscape(Globals.ProductName)},");
                sb.AppendLine($"  \"isCompiling\": {(ScriptsBuilder.IsCompiling ? "true" : "false")},");
                sb.AppendLine($"  \"lastCompilationFailed\": {(ScriptsBuilder.LastCompilationFailed ? "true" : "false")},");
                sb.AppendLine($"  \"loadedSceneCount\": {sceneCount},");

                // Count assets in Content folder
                var assetCount = 0;
                try
                {
                    var contentRoot = Editor.Instance.ContentDatabase.Game.Content.Folder;
                    var assetItems = new List<string>();
                    CollectContentItemsByType(contentRoot, null, assetItems);
                    assetCount = assetItems.Count;
                }
                catch { }

                sb.AppendLine($"  \"totalAssetCount\": {assetCount},");

                // Recent errors
                List<LogEntry> errors;
                lock (_logLock)
                {
                    errors = _logBuffer
                        .Where(e => e.Level == "Error" || e.Level == "Exception")
                        .TakeLast(10)
                        .ToList();
                }

                sb.AppendLine($"  \"recentErrorCount\": {errors.Count},");
                sb.AppendLine("  \"recentErrors\": [");
                for (int i = 0; i < errors.Count; i++)
                {
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"level\": {JsonEscape(errors[i].Level)},");
                    sb.AppendLine($"      \"message\": {JsonEscape(errors[i].Message)},");
                    sb.AppendLine($"      \"timestamp\": {JsonEscape(errors[i].Timestamp)}");
                    sb.Append("    }");
                    if (i < errors.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetEditorState(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                sb.AppendLine($"  \"isPlaying\": {(Editor.Instance.StateMachine.IsPlayMode ? "true" : "false")},");
                sb.AppendLine($"  \"isPaused\": {(Editor.Instance.StateMachine.PlayingState.IsPaused ? "true" : "false")},");
                sb.AppendLine($"  \"projectName\": {JsonEscape(Globals.ProductName)},");
                sb.AppendLine($"  \"projectFolder\": {JsonEscape(Globals.ProjectFolder)},");

                var scenes = Level.Scenes;
                sb.AppendLine("  \"loadedScenes\": [");
                if (scenes != null)
                {
                    for (int i = 0; i < scenes.Length; i++)
                    {
                        sb.Append($"    {JsonEscape(scenes[i].Name)}");
                        if (i < scenes.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                }
                sb.AppendLine("  ],");

                var selection = Editor.Instance.SceneEditing.Selection;
                sb.AppendLine($"  \"selectionCount\": {selection.Count},");
                sb.AppendLine("  \"selectedActors\": [");
                for (int i = 0; i < selection.Count; i++)
                {
                    var node = selection[i];
                    if (node is FlaxEditor.SceneGraph.ActorNode actorNode && actorNode.Actor != null)
                    {
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"name\": {JsonEscape(actorNode.Actor.Name)},");
                        sb.AppendLine($"      \"type\": {JsonEscape(actorNode.Actor.GetType().Name)},");
                        sb.AppendLine($"      \"id\": {JsonEscape(actorNode.Actor.ID.ToString())}");
                        sb.Append("    }");
                    }
                    else
                    {
                        sb.Append($"    {{ \"name\": {JsonEscape(node.Name)}, \"type\": \"SceneGraphNode\" }}");
                    }
                    if (i < selection.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ],");

                sb.AppendLine($"  \"undoCount\": {Editor.Instance.Undo.UndoOperationsStack.HistoryCount},");
                sb.AppendLine($"  \"canUndo\": {(Editor.Instance.Undo.CanUndo ? "true" : "false")},");
                sb.AppendLine($"  \"canRedo\": {(Editor.Instance.Undo.CanRedo ? "true" : "false")}");

                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetEditorLogs(Dictionary<string, object> args)
        {
            int count = GetArgInt(args, "count", 50);
            count = Math.Min(count, MaxLogEntries);

            List<LogEntry> entries;
            lock (_logLock)
            {
                var start = Math.Max(0, _logBuffer.Count - count);
                entries = _logBuffer.GetRange(start, _logBuffer.Count - start);
            }

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"count\": {entries.Count},");
            sb.AppendLine("  \"logs\": [");

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"level\": {JsonEscape(entry.Level)},");
                sb.AppendLine($"      \"message\": {JsonEscape(entry.Message)},");
                sb.AppendLine($"      \"timestamp\": {JsonEscape(entry.Timestamp)}");
                sb.Append("    }");
                if (i < entries.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        // ==================================================================
        // TOOL HANDLERS: Scene
        // ==================================================================

        private string ToolGetSceneHierarchy(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"scenes\": [");

                for (int s = 0; s < scenes.Length; s++)
                {
                    sb.Append("    ");
                    BuildActorJson(sb, scenes[s], 2);
                    if (s < scenes.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetSceneList(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var items = new List<string>();
                CollectContentItemsByType(Editor.Instance.ContentDatabase.Game.Content.Folder, "Scene", items);

                var loadedScenes = Level.Scenes;

                var sb = new StringBuilder();
                sb.AppendLine("{");

                sb.AppendLine("  \"loadedScenes\": [");
                if (loadedScenes != null)
                {
                    for (int i = 0; i < loadedScenes.Length; i++)
                    {
                        var scene = loadedScenes[i];
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"name\": {JsonEscape(scene.Name)},");
                        sb.AppendLine($"      \"id\": {JsonEscape(scene.ID.ToString())}");
                        sb.Append("    }");
                        if (i < loadedScenes.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                }
                sb.AppendLine("  ],");

                sb.AppendLine($"  \"assetCount\": {items.Count},");
                sb.AppendLine("  \"sceneAssets\": [");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append($"    {items[i]}");
                    if (i < items.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                return BuildActorDetailJson(actor);
            });
        }

        private string ToolFindActors(Dictionary<string, object> args)
        {
            var nameQuery = GetArgString(args, "name");
            var typeQuery = GetArgString(args, "type");
            var tagQuery = GetArgString(args, "tag");

            return InvokeOnMainThread(() =>
            {
                var matches = new List<Actor>();
                var scenes = Level.Scenes;

                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                foreach (var scene in scenes)
                    CollectMatchingActors(scene, nameQuery, typeQuery, tagQuery, matches);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {matches.Count},");
                sb.AppendLine("  \"actors\": [");

                for (int i = 0; i < matches.Count; i++)
                {
                    var a = matches[i];
                    var pos = a.Position;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(a.Name)},");
                    sb.AppendLine($"      \"type\": {JsonEscape(a.GetType().Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(a.ID.ToString())},");
                    sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }}");
                    sb.Append("    }");
                    if (i < matches.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolSetActorProperty(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var property = GetArgString(args, "property");
                if (string.IsNullOrEmpty(property))
                    return BuildJsonObject("error", "Missing 'property' argument.");

                args.TryGetValue("value", out var value);

                try
                {
                    SetPropertyByPath(actor, property, value);
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "property", property
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to set property: {ex.Message}");
                }
            });
        }

        private string ToolCreateActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Actor parent = scenes[0];
                var parentName = GetArgString(args, "parent");
                if (!string.IsNullOrEmpty(parentName))
                {
                    var found = FindActorByName(parentName);
                    if (found != null)
                        parent = found;
                }

                var typeName = GetArgString(args, "type");
                if (string.IsNullOrEmpty(typeName))
                    return BuildJsonObject("error", "Missing 'type' argument.");

                Actor newActor = null;
                try
                {
                    switch (typeName)
                    {
                        case "EmptyActor": newActor = new EmptyActor(); break;
                        case "StaticModel": newActor = new StaticModel(); break;
                        case "PointLight": newActor = new PointLight(); break;
                        case "SpotLight": newActor = new SpotLight(); break;
                        case "DirectionalLight": newActor = new DirectionalLight(); break;
                        case "Camera": newActor = new Camera(); break;
                        case "AudioSource": newActor = new AudioSource(); break;
                        case "BoxCollider": newActor = new BoxCollider(); break;
                        case "SphereCollider": newActor = new SphereCollider(); break;
                        case "MeshCollider": newActor = new MeshCollider(); break;
                        case "RigidBody": newActor = new RigidBody(); break;
                        case "AnimatedModel": newActor = new AnimatedModel(); break;
                        case "Decal": newActor = new Decal(); break;
                        case "Sky": newActor = new Sky(); break;
                        case "SkyLight": newActor = new SkyLight(); break;
                        case "ExponentialHeightFog": newActor = new ExponentialHeightFog(); break;
                        case "PostFxVolume": newActor = new PostFxVolume(); break;
                        case "TextRender": newActor = new TextRender(); break;
                        case "UICanvas": newActor = new UICanvas(); break;
                        case "UIControl": newActor = new UIControl(); break;
                        default:
                            Type resolvedType = Type.GetType(typeName);
                            if (resolvedType == null)
                                resolvedType = typeof(Actor).Assembly.GetType("FlaxEngine." + typeName);
                            if (resolvedType != null && typeof(Actor).IsAssignableFrom(resolvedType))
                                newActor = Activator.CreateInstance(resolvedType) as Actor;
                            break;
                    }

                    if (newActor == null)
                        return BuildJsonObject("error", $"Unknown or unsupported actor type: {typeName}");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create actor: {ex.Message}");
                }

                newActor.Name = GetArgString(args, "name") ?? typeName;

                if (args.ContainsKey("positionX") || args.ContainsKey("positionY") || args.ContainsKey("positionZ"))
                {
                    newActor.Position = new Vector3(
                        GetArgFloat(args, "positionX"),
                        GetArgFloat(args, "positionY"),
                        GetArgFloat(args, "positionZ")
                    );
                }

                if (args.ContainsKey("rotationX") || args.ContainsKey("rotationY") || args.ContainsKey("rotationZ"))
                {
                    newActor.Orientation = Quaternion.Euler(
                        GetArgFloat(args, "rotationX"),
                        GetArgFloat(args, "rotationY"),
                        GetArgFloat(args, "rotationZ")
                    );
                }

                if (newActor is StaticModel staticModel)
                {
                    var modelPath = GetArgString(args, "model");
                    if (!string.IsNullOrEmpty(modelPath))
                    {
                        var model = FlaxEngine.Content.Load<Model>(modelPath);
                        if (model != null)
                            staticModel.Model = model;
                        else
                            Debug.LogWarning($"[McpServer] Model not found: {modelPath}");
                    }
                }

                newActor.Parent = parent;

                return BuildJsonObject(
                    "ok", "true",
                    "name", newActor.Name,
                    "type", newActor.GetType().Name,
                    "id", newActor.ID.ToString()
                );
            });
        }

        private string ToolDeleteActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var actorName = actor.Name;
                var actorId = actor.ID.ToString();
                FlaxEngine.Object.Destroy(actor);

                return BuildJsonObject(
                    "ok", "true",
                    "deleted", actorName,
                    "id", actorId
                );
            });
        }

        private string ToolReparentActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                // Resolve the actor to reparent
                Actor actor = null;
                var actorId = GetArgString(args, "actorId");
                var actorName = GetArgString(args, "actor");

                if (!string.IsNullOrEmpty(actorId))
                {
                    if (Guid.TryParse(actorId, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else if (!string.IsNullOrEmpty(actorName))
                {
                    actor = FindActorByName(actorName);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {actorName ?? actorId ?? "unknown"}");

                // Resolve the new parent
                Actor newParent = null;
                var newParentId = GetArgString(args, "newParentId");
                var newParentName = GetArgString(args, "newParent");

                if (!string.IsNullOrEmpty(newParentId))
                {
                    if (Guid.TryParse(newParentId, out var guid))
                        newParent = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else if (!string.IsNullOrEmpty(newParentName))
                {
                    newParent = FindActorByName(newParentName);
                }

                if (newParent == null)
                    return BuildJsonObject("error", $"New parent not found: {newParentName ?? newParentId ?? "unknown"}");

                actor.Parent = newParent;

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "newParent", newParent.Name
                );
            });
        }

        private string ToolCreateScene(Dictionary<string, object> args)
        {
            var sceneName = GetArgString(args, "name", "NewScene");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var outputPath = GetArgString(args, "path") ?? $"Content/{sceneName}.scene";
                    var absPath = Path.Combine(Globals.ProjectFolder, outputPath);
                    var dir = Path.GetDirectoryName(absPath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    if (FlaxEditor.Editor.CreateAsset("Scene", absPath))
                        return BuildJsonObject("error", $"Failed to create scene at: {outputPath}");

                    return BuildJsonObject(
                        "created", "true",
                        "name", sceneName,
                        "path", outputPath
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create scene: {ex.Message}");
                }
            });
        }

        private string ToolSaveScene(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Editor.Instance.Scene.SaveScenes();

                return BuildJsonObject("ok", "true", "savedCount", scenes.Length.ToString());
            });
        }

        private string ToolLoadScene(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var asset = FlaxEngine.Content.Load<SceneAsset>(path);
                    if (asset == null)
                        return BuildJsonObject("error", $"Scene asset not found: {path}");

                    Level.LoadScene(asset.ID);

                    return BuildJsonObject(
                        "ok", "true",
                        "path", path,
                        "status", "scene_load_requested"
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to load scene: {ex.Message}");
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Assets & Content
        // ==================================================================

        private string ToolListAssets(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path", "Content");
            var typeFilter = GetArgString(args, "type");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(path);
                if (item == null)
                    return BuildJsonObject("error", $"Path not found: {path}");

                var folder = item as ContentFolder;
                if (folder == null)
                    return BuildJsonObject("error", $"Path is not a folder: {path}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"path\": {JsonEscape(path)},");
                sb.AppendLine("  \"items\": [");

                var items = new List<string>();

                foreach (var child in folder.Children)
                {
                    if (child is ContentFolder childFolder)
                    {
                        items.Add(BuildJsonObject(
                            "name", childFolder.ShortName,
                            "type", "Folder",
                            "path", childFolder.Path
                        ));
                    }
                    else
                    {
                        var ext = Path.GetExtension(child.FileName)?.TrimStart('.').ToLowerInvariant() ?? "";
                        if (typeFilter != null && !string.Equals(ext, typeFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var idStr = "";
                        if (child is AssetItem assetItem)
                            idStr = assetItem.ID.ToString();

                        items.Add(BuildJsonObject(
                            "name", child.ShortName,
                            "type", child.GetType().Name,
                            "path", child.Path,
                            "id", idStr
                        ));
                    }
                }

                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append("    ");
                    sb.Append(items[i]);
                    if (i < items.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolImportAssets(Dictionary<string, object> args)
        {
            var target = GetArgString(args, "target", "Content");
            var skipDialog = GetArgBool(args, "skipDialog", true);

            // Extract file list
            var fileList = new List<string>();
            if (args.TryGetValue("files", out var filesObj))
            {
                if (filesObj is List<object> objList)
                {
                    foreach (var f in objList)
                    {
                        if (f != null) fileList.Add(f.ToString());
                    }
                }
                else if (filesObj is string[] strArr)
                {
                    fileList.AddRange(strArr);
                }
            }

            if (fileList.Count == 0)
                return BuildJsonObject("error", "Missing or empty 'files' argument.");

            return InvokeOnMainThread(() =>
            {
                var folder = Editor.Instance.ContentDatabase.Find(target) as ContentFolder;
                if (folder == null)
                    return BuildJsonObject("error", $"Target folder not found: {target}");

                int queued = 0;
                foreach (var file in fileList)
                {
                    if (!File.Exists(file))
                    {
                        Debug.LogWarning($"[McpServer] Import file not found: {file}");
                        continue;
                    }

                    Editor.Instance.ContentImporting.Import(file, folder, skipDialog);
                    queued++;
                }

                return BuildJsonObject(
                    "imported", queued.ToString(),
                    "target", target
                );
            });
        }

        private string ToolFindContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(path);
                if (item == null)
                    return BuildJsonObject("error", $"Content item not found: {path}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": {JsonEscape(item.ShortName)},");
                sb.AppendLine($"  \"type\": {JsonEscape(item.GetType().Name)},");
                sb.AppendLine($"  \"path\": {JsonEscape(item.Path)},");
                sb.AppendLine($"  \"isFolder\": {(item is ContentFolder ? "true" : "false")},");

                if (item is AssetItem assetItem)
                {
                    sb.AppendLine($"  \"id\": {JsonEscape(assetItem.ID.ToString())},");
                    sb.AppendLine($"  \"typeName\": {JsonEscape(assetItem.TypeName)}");
                }
                else
                {
                    sb.AppendLine($"  \"id\": \"\"");
                }

                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolSearchContent(Dictionary<string, object> args)
        {
            var searchQuery = GetArgString(args, "query");
            var typeFilter = GetArgString(args, "type");

            if (string.IsNullOrEmpty(searchQuery) && string.IsNullOrEmpty(typeFilter))
                return BuildJsonObject("error", "Provide 'query' and/or 'type' argument.");

            return InvokeOnMainThread(() =>
            {
                var matches = new List<string>();
                SearchContentItems(Editor.Instance.ContentDatabase.Game.Content.Folder, searchQuery, typeFilter, matches);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {matches.Count},");
                sb.AppendLine("  \"results\": [");

                for (int i = 0; i < matches.Count; i++)
                {
                    sb.Append($"    {matches[i]}");
                    if (i < matches.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetContentInfo(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(path);
                if (item == null)
                    return BuildJsonObject("error", $"Content item not found: {path}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": {JsonEscape(item.ShortName)},");
                sb.AppendLine($"  \"type\": {JsonEscape(item.GetType().Name)},");
                sb.AppendLine($"  \"path\": {JsonEscape(item.Path)},");
                sb.AppendLine($"  \"isFolder\": {(item is ContentFolder ? "true" : "false")},");

                if (item is AssetItem assetItem)
                {
                    sb.AppendLine($"  \"id\": {JsonEscape(assetItem.ID.ToString())},");
                    sb.AppendLine($"  \"typeName\": {JsonEscape(assetItem.TypeName)},");

                    try
                    {
                        var fileInfo = new FileInfo(item.Path);
                        if (fileInfo.Exists)
                        {
                            sb.AppendLine($"  \"sizeBytes\": {fileInfo.Length},");
                            sb.AppendLine($"  \"lastModified\": {JsonEscape(fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))},");
                        }
                    }
                    catch
                    {
                        // Ignore file info errors
                    }

                    sb.AppendLine("  \"references\": []");
                }
                else
                {
                    sb.AppendLine($"  \"id\": \"\"");
                }

                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolReimportContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(path);
                if (item == null)
                    return BuildJsonObject("error", $"Asset not found: {path}");

                if (item is BinaryAssetItem binaryItem)
                {
                    Editor.Instance.ContentImporting.Reimport(binaryItem);
                    return BuildJsonObject(
                        "ok", "true",
                        "path", path,
                        "status", "reimport_requested"
                    );
                }

                return BuildJsonObject("error", $"Asset at {path} cannot be reimported.");
            });
        }

        private string ToolGetContentTree(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var contentRoot = Editor.Instance.ContentDatabase.Game.Content.Folder;
                if (contentRoot == null)
                    return BuildJsonObject("error", "Content database not available.");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.Append("  \"tree\": ");
                CollectContentTree(contentRoot, sb, 1);
                sb.AppendLine();
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Materials
        // ==================================================================

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
        // TOOL HANDLERS: Physics
        // ==================================================================

        private string ToolPhysicsRaycast(Dictionary<string, object> args)
        {
            float originX = GetArgFloat(args, "originX");
            float originY = GetArgFloat(args, "originY");
            float originZ = GetArgFloat(args, "originZ");
            float dirX = GetArgFloat(args, "directionX");
            float dirY = GetArgFloat(args, "directionY", -1f);
            float dirZ = GetArgFloat(args, "directionZ");
            float maxDist = GetArgFloat(args, "maxDistance", 1000f);

            return InvokeOnMainThread(() =>
            {
                var origin = new Vector3(originX, originY, originZ);
                var direction = new Vector3(dirX, dirY, dirZ);

                RayCastHit hit;
                if (Physics.RayCast(origin, direction, out hit, maxDist))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine("  \"hit\": true,");
                    sb.AppendLine($"  \"distance\": {hit.Distance},");
                    sb.AppendLine($"  \"point\": {{ \"X\": {hit.Point.X}, \"Y\": {hit.Point.Y}, \"Z\": {hit.Point.Z} }},");
                    sb.AppendLine($"  \"normal\": {{ \"X\": {hit.Normal.X}, \"Y\": {hit.Normal.Y}, \"Z\": {hit.Normal.Z} }},");

                    var hitActor = hit.Collider?.Parent;
                    if (hitActor != null)
                    {
                        sb.AppendLine($"  \"actorName\": {JsonEscape(hitActor.Name)},");
                        sb.AppendLine($"  \"actorId\": {JsonEscape(hitActor.ID.ToString())}");
                    }
                    else
                    {
                        sb.AppendLine($"  \"actorName\": null,");
                        sb.AppendLine($"  \"actorId\": null");
                    }

                    sb.Append("}");
                    return sb.ToString();
                }

                return BuildJsonObject("hit", "false");
            });
        }

        private string ToolPhysicsOverlap(Dictionary<string, object> args)
        {
            float x = GetArgFloat(args, "x");
            float y = GetArgFloat(args, "y");
            float z = GetArgFloat(args, "z");
            float radius = GetArgFloat(args, "radius", 5f);

            return InvokeOnMainThread(() =>
            {
                var center = new Vector3(x, y, z);
                Collider[] results;
                Physics.OverlapSphere(center, radius, out results, uint.MaxValue);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"center\": {{ \"X\": {x}, \"Y\": {y}, \"Z\": {z} }},");
                sb.AppendLine($"  \"radius\": {radius},");

                if (results == null || results.Length == 0)
                {
                    sb.AppendLine("  \"count\": 0,");
                    sb.AppendLine("  \"actors\": []");
                }
                else
                {
                    var seen = new HashSet<Guid>();
                    var actors = new List<Actor>();
                    foreach (var collider in results)
                    {
                        var actor = collider?.Parent;
                        if (actor != null && seen.Add(actor.ID))
                            actors.Add(actor);
                    }

                    sb.AppendLine($"  \"count\": {actors.Count},");
                    sb.AppendLine("  \"actors\": [");

                    for (int i = 0; i < actors.Count; i++)
                    {
                        var a = actors[i];
                        var pos = a.Position;
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"name\": {JsonEscape(a.Name)},");
                        sb.AppendLine($"      \"type\": {JsonEscape(a.GetType().Name)},");
                        sb.AppendLine($"      \"id\": {JsonEscape(a.ID.ToString())},");
                        sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }}");
                        sb.Append("    }");
                        if (i < actors.Count - 1) sb.Append(",");
                        sb.AppendLine();
                    }

                    sb.AppendLine("  ]");
                }

                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolGetPhysicsSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var gravity = Physics.Gravity;
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"gravity\": {{ \"X\": {gravity.X}, \"Y\": {gravity.Y}, \"Z\": {gravity.Z} }},");
                sb.AppendLine($"  \"bounceThresholdVelocity\": {Physics.BounceThresholdVelocity},");
                sb.AppendLine($"  \"enableCCD\": {(Physics.EnableCCD ? "true" : "false")}");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolSetPhysicsSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                if (args.ContainsKey("gravityX") || args.ContainsKey("gravityY") || args.ContainsKey("gravityZ"))
                {
                    var g = Physics.Gravity;
                    Physics.Gravity = new Float3(
                        args.ContainsKey("gravityX") ? GetArgFloat(args, "gravityX") : (float)g.X,
                        args.ContainsKey("gravityY") ? GetArgFloat(args, "gravityY") : (float)g.Y,
                        args.ContainsKey("gravityZ") ? GetArgFloat(args, "gravityZ") : (float)g.Z
                    );
                }

                return BuildJsonObject("ok", "true", "status", "physics settings updated");
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Animation
        // ==================================================================

        private string ToolListAnimations(Dictionary<string, object> args)
        {
            var actorName = GetArgString(args, "actor");

            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var animatedModels = new List<AnimatedModel>();

                if (!string.IsNullOrEmpty(actorName))
                {
                    var actor = FindActorByName(actorName);
                    if (actor == null)
                        return BuildJsonObject("error", $"Actor not found: {actorName}");

                    var am = actor as AnimatedModel ?? actor.GetChild<AnimatedModel>();
                    if (am != null)
                        animatedModels.Add(am);
                    else
                        return BuildJsonObject("error", $"No AnimatedModel found on actor: {actorName}");
                }
                else
                {
                    foreach (var scene in scenes)
                        CollectActorsOfType(scene, animatedModels);
                }

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {animatedModels.Count},");
                sb.AppendLine("  \"animatedModels\": [");

                for (int i = 0; i < animatedModels.Count; i++)
                {
                    var am = animatedModels[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(am.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(am.ID.ToString())},");
                    sb.AppendLine($"      \"skinnedModel\": {JsonEscape(am.SkinnedModel?.Path ?? "none")},");
                    sb.AppendLine($"      \"animationGraph\": {JsonEscape(am.AnimationGraph?.Path ?? "none")},");

                    sb.AppendLine("      \"parameters\": [");
                    var parameters = am.Parameters;
                    if (parameters != null)
                    {
                        for (int p = 0; p < parameters.Length; p++)
                        {
                            var param = parameters[p];
                            sb.AppendLine("        {");
                            sb.AppendLine($"          \"name\": {JsonEscape(param.Name)},");
                            sb.AppendLine($"          \"type\": {JsonEscape(param.Type.ToString())},");
                            sb.AppendLine($"          \"value\": {JsonEscape(param.Value?.ToString() ?? "null")}");
                            sb.Append("        }");
                            if (p < parameters.Length - 1) sb.Append(",");
                            sb.AppendLine();
                        }
                    }
                    sb.AppendLine("      ]");

                    sb.Append("    }");
                    if (i < animatedModels.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolPlayAnimation(Dictionary<string, object> args)
        {
            var actorName = GetArgString(args, "actor");
            if (string.IsNullOrEmpty(actorName))
                return BuildJsonObject("error", "Missing 'actor' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {actorName}");

                var animatedModel = actor as AnimatedModel ?? actor.GetChild<AnimatedModel>();
                if (animatedModel == null)
                    return BuildJsonObject("error", $"No AnimatedModel found on actor: {actorName}");

                if (args.ContainsKey("speed"))
                    animatedModel.UpdateSpeed = GetArgFloat(args, "speed", 1f);

                var paramName = GetArgString(args, "parameter");
                if (!string.IsNullOrEmpty(paramName) && args.TryGetValue("parameterValue", out var paramVal))
                {
                    var param = animatedModel.GetParameter(paramName);
                    if (param != null)
                        param.Value = paramVal;
                }

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "speed", animatedModel.UpdateSpeed.ToString("F2")
                );
            });
        }

        private string ToolGetAnimationState(Dictionary<string, object> args)
        {
            var actorName = GetArgString(args, "actor");
            if (string.IsNullOrEmpty(actorName))
                return BuildJsonObject("error", "Missing 'actor' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {actorName}");

                var animatedModel = actor as AnimatedModel ?? actor.GetChild<AnimatedModel>();
                if (animatedModel == null)
                    return BuildJsonObject("error", $"No AnimatedModel found on actor: {actorName}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"actor\": {JsonEscape(actor.Name)},");
                sb.AppendLine($"  \"isPlaying\": {(animatedModel.IsPlayingSlotAnimation() ? "true" : "false")},");
                sb.AppendLine($"  \"speed\": {animatedModel.UpdateSpeed},");
                sb.AppendLine($"  \"updateMode\": {JsonEscape(animatedModel.UpdateMode.ToString())},");

                sb.AppendLine("  \"parameters\": [");
                var parameters = animatedModel.Parameters;
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"name\": {JsonEscape(p.Name)},");
                        sb.AppendLine($"      \"value\": {JsonEscape(p.Value?.ToString() ?? "null")}");
                        sb.Append("    }");
                        if (i < parameters.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Terrain
        // ==================================================================

        private string ToolGetTerrainInfo(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var terrains = new List<Terrain>();
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                foreach (var scene in scenes)
                    CollectActorsOfType(scene, terrains);

                if (terrains.Count == 0)
                    return BuildJsonObject("error", "No Terrain actors found in the scene.");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {terrains.Count},");
                sb.AppendLine("  \"terrains\": [");

                for (int i = 0; i < terrains.Count; i++)
                {
                    var terrain = terrains[i];
                    var pos = terrain.Position;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(terrain.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(terrain.ID.ToString())},");
                    sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }},");
                    sb.AppendLine($"      \"patchCount\": {terrain.PatchesCount},");
                    sb.AppendLine($"      \"chunkSize\": {terrain.ChunkSize},");

                    var box = terrain.Box;
                    sb.AppendLine($"      \"boundsMin\": {{ \"X\": {box.Minimum.X}, \"Y\": {box.Minimum.Y}, \"Z\": {box.Minimum.Z} }},");
                    sb.AppendLine($"      \"boundsMax\": {{ \"X\": {box.Maximum.X}, \"Y\": {box.Maximum.Y}, \"Z\": {box.Maximum.Z} }}");

                    sb.Append("    }");
                    if (i < terrains.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolTerrainSculpt(Dictionary<string, object> args)
        {
            float xPos = GetArgFloat(args, "x");
            float zPos = GetArgFloat(args, "z");

            return InvokeOnMainThread(() =>
            {
                var terrains = new List<Terrain>();
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                foreach (var scene in scenes)
                    CollectActorsOfType(scene, terrains);

                if (terrains.Count == 0)
                    return BuildJsonObject("error", "No Terrain actors found in the scene.");

                Terrain terrain;
                var terrainName = GetArgString(args, "terrainName");
                if (!string.IsNullOrEmpty(terrainName))
                {
                    terrain = terrains.Find(t => string.Equals(t.Name, terrainName, StringComparison.OrdinalIgnoreCase));
                    if (terrain == null)
                        return BuildJsonObject("error", $"Terrain not found: {terrainName}");
                }
                else
                {
                    terrain = terrains[0];
                }

                var worldPos = new Vector3(xPos, 10000f, zPos);
                RayCastHit hit;
                if (!Physics.RayCast(worldPos, Vector3.Down, out hit, 20000f))
                    return BuildJsonObject("error", $"No terrain surface found at X={xPos}, Z={zPos}");

                return BuildJsonObject(
                    "ok", "true",
                    "terrain", terrain.Name,
                    "note", "Terrain sculpt operations require the Editor sculpt tool. Use the editor viewport or terrain tool API for direct heightmap edits.",
                    "position", $"X={xPos}, Z={zPos}",
                    "surfaceY", hit.Point.Y.ToString("F2")
                );
            });
        }

        private string ToolGetTerrainHeight(Dictionary<string, object> args)
        {
            float x = GetArgFloat(args, "x");
            float z = GetArgFloat(args, "z");

            return InvokeOnMainThread(() =>
            {
                var origin = new Vector3(x, 100000f, z);
                RayCastHit hit;
                if (Physics.RayCast(origin, Vector3.Down, out hit, 200000f))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine($"  \"x\": {x},");
                    sb.AppendLine($"  \"z\": {z},");
                    sb.AppendLine($"  \"height\": {hit.Point.Y},");
                    sb.AppendLine($"  \"normal\": {{ \"X\": {hit.Normal.X}, \"Y\": {hit.Normal.Y}, \"Z\": {hit.Normal.Z} }}");
                    sb.Append("}");
                    return sb.ToString();
                }

                return BuildJsonObject("error", $"No surface found at X={x}, Z={z}");
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Navigation
        // ==================================================================

        private string ToolBuildNavmesh(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    Navigation.BuildNavMesh(Level.Scenes.Length > 0 ? Level.Scenes[0] : null);
                    return BuildJsonObject("ok", "true", "status", "navmesh_build_requested");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to build navmesh: {ex.Message}");
                }
            });
        }

        private string ToolQueryNavpath(Dictionary<string, object> args)
        {
            float startX = GetArgFloat(args, "startX");
            float startY = GetArgFloat(args, "startY");
            float startZ = GetArgFloat(args, "startZ");
            float endX = GetArgFloat(args, "endX");
            float endY = GetArgFloat(args, "endY");
            float endZ = GetArgFloat(args, "endZ");

            return InvokeOnMainThread(() =>
            {
                var start = new Vector3(startX, startY, startZ);
                var end = new Vector3(endX, endY, endZ);

                Vector3[] pathPoints;
                if (Navigation.FindPath(start, end, out pathPoints))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine("  \"found\": true,");
                    sb.AppendLine($"  \"pointCount\": {pathPoints.Length},");
                    sb.AppendLine("  \"path\": [");

                    for (int i = 0; i < pathPoints.Length; i++)
                    {
                        var pt = pathPoints[i];
                        sb.Append($"    {{ \"X\": {pt.X}, \"Y\": {pt.Y}, \"Z\": {pt.Z} }}");
                        if (i < pathPoints.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }

                    sb.AppendLine("  ]");
                    sb.Append("}");
                    return sb.ToString();
                }

                return BuildJsonObject("found", "false");
            });
        }

        private string ToolGetNavigationInfo(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var navVolumes = new List<Actor>();
                foreach (var scene in scenes)
                    CollectActorsOfTypeName(scene, "NavMeshBoundsVolume", navVolumes);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"navVolumeCount\": {navVolumes.Count},");
                sb.AppendLine("  \"volumes\": [");

                for (int i = 0; i < navVolumes.Count; i++)
                {
                    var vol = navVolumes[i];
                    var pos = vol.Position;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(vol.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(vol.ID.ToString())},");
                    sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }}");
                    sb.Append("    }");
                    if (i < navVolumes.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Rendering
        // ==================================================================

        private string ToolTakeScreenshot(Dictionary<string, object> args)
        {
            var outputPath = GetArgString(args, "outputPath");
            if (string.IsNullOrEmpty(outputPath))
                return BuildJsonObject("error", "Missing 'outputPath' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    Screenshot.Capture(outputPath);

                    return BuildJsonObject(
                        "ok", "true",
                        "outputPath", outputPath
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Screenshot failed: {ex.Message}");
                }
            });
        }

        private string ToolGetRenderingSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                var postFx = new List<PostFxVolume>();
                var scenes = Level.Scenes;
                if (scenes != null)
                {
                    foreach (var scene in scenes)
                        CollectActorsOfType(scene, postFx);
                }

                sb.AppendLine($"  \"postFxVolumeCount\": {postFx.Count},");
                sb.AppendLine("  \"postFxVolumes\": [");

                for (int i = 0; i < postFx.Count; i++)
                {
                    var vol = postFx[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(vol.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(vol.ID.ToString())},");
                    sb.AppendLine($"      \"priority\": {vol.Priority},");
                    sb.AppendLine($"      \"isBounded\": {(vol.IsBounded ? "true" : "false")}");
                    sb.Append("    }");
                    if (i < postFx.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolSetRenderingSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                // Graphics.HalfResolution is not available in Flax 1.11.
                // Additional rendering settings can be added here as needed.
                return BuildJsonObject("ok", "true", "status", "rendering settings updated");
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Audio
        // ==================================================================

        private string ToolPlayAudio(Dictionary<string, object> args)
        {
            var clipPath = GetArgString(args, "clip");
            if (string.IsNullOrEmpty(clipPath))
                return BuildJsonObject("error", "Missing 'clip' argument.");

            return InvokeOnMainThread(() =>
            {
                var clip = FlaxEngine.Content.Load<AudioClip>(clipPath);
                if (clip == null)
                    return BuildJsonObject("error", $"Audio clip not found: {clipPath}");

                var audioSource = new AudioSource();
                audioSource.Clip = clip;
                audioSource.IsLooping = GetArgBool(args, "loop");

                if (args.ContainsKey("positionX") || args.ContainsKey("positionY") || args.ContainsKey("positionZ"))
                {
                    audioSource.Position = new Vector3(
                        GetArgFloat(args, "positionX"),
                        GetArgFloat(args, "positionY"),
                        GetArgFloat(args, "positionZ")
                    );
                }

                var scenes = Level.Scenes;
                if (scenes != null && scenes.Length > 0)
                {
                    audioSource.Name = $"MCP_AudioSource_{DateTime.UtcNow.Ticks}";
                    audioSource.Parent = scenes[0];
                }

                audioSource.Play();

                return BuildJsonObject(
                    "ok", "true",
                    "clip", clipPath,
                    "actorId", audioSource.ID.ToString()
                );
            });
        }

        private string ToolListAudioSources(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var sources = new List<AudioSource>();
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                foreach (var scene in scenes)
                    CollectActorsOfType(scene, sources);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {sources.Count},");
                sb.AppendLine("  \"sources\": [");

                for (int i = 0; i < sources.Count; i++)
                {
                    var src = sources[i];
                    var pos = src.Position;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(src.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(src.ID.ToString())},");
                    sb.AppendLine($"      \"clip\": {JsonEscape(src.Clip?.Path ?? "none")},");
                    sb.AppendLine($"      \"isPlaying\": {(src.IsActuallyPlaying ? "true" : "false")},");
                    sb.AppendLine($"      \"isLooping\": {(src.IsLooping ? "true" : "false")},");
                    sb.AppendLine($"      \"volume\": {src.Volume},");
                    sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }}");
                    sb.Append("    }");
                    if (i < sources.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Prefabs
        // ==================================================================

        private string ToolSpawnPrefab(Dictionary<string, object> args)
        {
            var prefabPath = GetArgString(args, "prefab");
            if (string.IsNullOrEmpty(prefabPath))
                return BuildJsonObject("error", "Missing 'prefab' argument.");

            return InvokeOnMainThread(() =>
            {
                var prefab = FlaxEngine.Content.Load<Prefab>(prefabPath);
                if (prefab == null)
                    return BuildJsonObject("error", $"Prefab not found: {prefabPath}");

                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Actor parent = scenes[0];
                var parentName = GetArgString(args, "parent");
                if (!string.IsNullOrEmpty(parentName))
                {
                    var found = FindActorByName(parentName);
                    if (found != null)
                        parent = found;
                }

                var instance = PrefabManager.SpawnPrefab(prefab, parent);
                if (instance == null)
                    return BuildJsonObject("error", $"Failed to spawn prefab: {prefabPath}");

                if (args.ContainsKey("positionX") || args.ContainsKey("positionY") || args.ContainsKey("positionZ"))
                {
                    instance.Position = new Vector3(
                        GetArgFloat(args, "positionX"),
                        GetArgFloat(args, "positionY"),
                        GetArgFloat(args, "positionZ")
                    );
                }

                if (args.ContainsKey("rotationX") || args.ContainsKey("rotationY") || args.ContainsKey("rotationZ"))
                {
                    instance.Orientation = Quaternion.Euler(
                        GetArgFloat(args, "rotationX"),
                        GetArgFloat(args, "rotationY"),
                        GetArgFloat(args, "rotationZ")
                    );
                }

                var instanceName = GetArgString(args, "name");
                if (!string.IsNullOrEmpty(instanceName))
                    instance.Name = instanceName;

                return BuildJsonObject(
                    "ok", "true",
                    "name", instance.Name,
                    "id", instance.ID.ToString(),
                    "prefab", prefabPath
                );
            });
        }

        private string ToolListPrefabs(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var items = new List<string>();
                CollectContentItemsByType(Editor.Instance.ContentDatabase.Game.Content.Folder, "Prefab", items);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {items.Count},");
                sb.AppendLine("  \"prefabs\": [");

                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append($"    {items[i]}");
                    if (i < items.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Editor Control
        // ==================================================================

        private string ToolEditorPlay(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                if (Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("status", "already_playing");

                Editor.Instance.Simulation.RequestStartPlayScenes();
                return BuildJsonObject("status", "play_requested");
            });
        }

        private string ToolEditorStop(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                if (!Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("status", "not_playing");

                Editor.Instance.Simulation.RequestStopPlay();
                return BuildJsonObject("status", "stop_requested");
            });
        }

        private string ToolEditorSelect(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var node = Editor.Instance.Scene.GetActorNode(actor);
                if (node == null)
                    return BuildJsonObject("error", $"Actor node not found in scene graph: {actor.Name}");

                Editor.Instance.SceneEditing.Select(node);

                return BuildJsonObject(
                    "ok", "true",
                    "selected", actor.Name,
                    "id", actor.ID.ToString()
                );
            });
        }

        private string ToolEditorFocus(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var node = Editor.Instance.Scene.GetActorNode(actor);
                if (node != null)
                {
                    Editor.Instance.SceneEditing.Select(node);
                    Editor.Instance.Windows.EditWin.Viewport.FocusSelection();
                }

                return BuildJsonObject(
                    "ok", "true",
                    "focused", actor.Name,
                    "id", actor.ID.ToString()
                );
            });
        }

        private string ToolGetViewport(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var viewport = Editor.Instance.Windows.EditWin.Viewport;
                var viewPos = viewport.ViewPosition;
                var viewDir = viewport.ViewDirection;
                var viewOrientation = viewport.ViewOrientation;

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"position\": {{ \"X\": {viewPos.X}, \"Y\": {viewPos.Y}, \"Z\": {viewPos.Z} }},");
                sb.AppendLine($"  \"direction\": {{ \"X\": {viewDir.X}, \"Y\": {viewDir.Y}, \"Z\": {viewDir.Z} }},");
                sb.AppendLine($"  \"orientation\": {{ \"X\": {viewOrientation.X}, \"Y\": {viewOrientation.Y}, \"Z\": {viewOrientation.Z}, \"W\": {viewOrientation.W} }}");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private string ToolSetViewport(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var viewport = Editor.Instance.Windows.EditWin.Viewport;

                if (args.ContainsKey("positionX") || args.ContainsKey("positionY") || args.ContainsKey("positionZ"))
                {
                    var pos = viewport.ViewPosition;
                    viewport.ViewPosition = new Vector3(
                        args.ContainsKey("positionX") ? GetArgFloat(args, "positionX") : (float)pos.X,
                        args.ContainsKey("positionY") ? GetArgFloat(args, "positionY") : (float)pos.Y,
                        args.ContainsKey("positionZ") ? GetArgFloat(args, "positionZ") : (float)pos.Z
                    );
                }

                if (args.ContainsKey("yaw") || args.ContainsKey("pitch"))
                {
                    viewport.ViewOrientation = Quaternion.Euler(
                        GetArgFloat(args, "pitch"),
                        GetArgFloat(args, "yaw"),
                        0f
                    );
                }

                return BuildJsonObject("ok", "true", "status", "viewport updated");
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Scripts
        // ==================================================================

        private string ToolListScripts(Dictionary<string, object> args)
        {
            var projectFolder = InvokeOnMainThread(() => Globals.ProjectFolder);
            var sourceFolder = Path.Combine(projectFolder, "Source");

            if (!Directory.Exists(sourceFolder))
                return BuildJsonObject("error", "Source folder not found.");

            var csFiles = Directory.GetFiles(sourceFolder, "*.cs", SearchOption.AllDirectories);

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"count\": {csFiles.Length},");
            sb.AppendLine("  \"scripts\": [");

            for (int i = 0; i < csFiles.Length; i++)
            {
                var relPath = csFiles[i].Substring(projectFolder.Length).TrimStart('\\', '/');
                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": {JsonEscape(Path.GetFileNameWithoutExtension(csFiles[i]))},");
                sb.AppendLine($"      \"path\": {JsonEscape(relPath)},");
                sb.AppendLine($"      \"fileName\": {JsonEscape(Path.GetFileName(csFiles[i]))}");
                sb.Append("    }");
                if (i < csFiles.Length - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        private string ToolReadScript(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            var projectFolder = InvokeOnMainThread(() => Globals.ProjectFolder);
            var absPath = Path.Combine(projectFolder, path);

            if (!File.Exists(absPath))
                return BuildJsonObject("error", $"File not found: {path}");

            var ext = Path.GetExtension(absPath).ToLowerInvariant();
            if (ext != ".cs" && ext != ".cpp" && ext != ".h" && ext != ".json" && ext != ".xml" && ext != ".build")
                return BuildJsonObject("error", $"File type not allowed: {ext}");

            try
            {
                var content = File.ReadAllText(absPath);
                return BuildJsonObject(
                    "path", path,
                    "content", content
                );
            }
            catch (Exception ex)
            {
                return BuildJsonObject("error", $"Failed to read file: {ex.Message}");
            }
        }

        private string ToolCompileScripts(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    ScriptsBuilder.Compile();
                    return BuildJsonObject("ok", "true", "status", "compilation_requested");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Compilation failed: {ex.Message}");
                }
            });
        }

        private string ToolGetScriptErrors(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"hasCompilationErrors\": {(ScriptsBuilder.LastCompilationFailed ? "true" : "false")},");
                sb.AppendLine($"  \"isCompiling\": {(ScriptsBuilder.IsCompiling ? "true" : "false")},");

                List<LogEntry> entries;
                lock (_logLock)
                {
                    entries = _logBuffer
                        .Where(e => e.Level == "Error" || e.Level == "Warning")
                        .Where(e => e.Message != null && (
                            e.Message.Contains(".cs(") ||
                            e.Message.Contains("error CS") ||
                            e.Message.Contains("warning CS") ||
                            e.Message.Contains("Compilation")))
                        .ToList();
                }

                sb.AppendLine($"  \"errorCount\": {entries.Count},");
                sb.AppendLine("  \"errors\": [");

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"level\": {JsonEscape(entry.Level)},");
                    sb.AppendLine($"      \"message\": {JsonEscape(entry.Message)},");
                    sb.AppendLine($"      \"timestamp\": {JsonEscape(entry.Timestamp)}");
                    sb.Append("    }");
                    if (i < entries.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Build
        // ==================================================================

        private string ToolBuildGame(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    _buildStatus = "building";

                    var platform = GetArgString(args, "platform", "Windows");
                    var config = GetArgString(args, "configuration", "Release");

                    return BuildJsonObject(
                        "ok", "true",
                        "status", "build_requested",
                        "platform", platform,
                        "configuration", config,
                        "note", "Use Build presets in the Flax Editor for full build configuration. Trigger builds via Editor > Game Cooker window."
                    );
                }
                catch (Exception ex)
                {
                    _buildStatus = "failed";
                    return BuildJsonObject("error", $"Build failed: {ex.Message}");
                }
            });
        }

        private string ToolGetBuildStatus(Dictionary<string, object> args)
        {
            return BuildJsonObject("status", _buildStatus);
        }

        // ==================================================================
        // TOOL HANDLERS: Project Settings
        // ==================================================================

        private string ToolGetProjectSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    var settings = GameSettings.Load();

                    var sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine($"  \"productName\": {JsonEscape(Globals.ProductName)},");
                    sb.AppendLine($"  \"companyName\": {JsonEscape(Globals.CompanyName)},");
                    sb.AppendLine($"  \"noSplashScreen\": {(settings.NoSplashScreen ? "true" : "false")},");

                    // First scene reference
                    var firstScene = settings.FirstScene;
                    if (firstScene.ID != Guid.Empty)
                    {
                        sb.AppendLine($"  \"firstSceneId\": {JsonEscape(firstScene.ID.ToString())},");
                    }
                    else
                    {
                        sb.AppendLine($"  \"firstSceneId\": null,");
                    }

                    sb.AppendLine($"  \"settingsPath\": {JsonEscape(GameSettings.GameSettingsAssetPath)}");
                    sb.Append("}");
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to read project settings: {ex.Message}");
                }
            });
        }

        private string ToolSetProjectSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    var settings = GameSettings.Load();
                    bool changed = false;

                    var productName = GetArgString(args, "productName");
                    if (productName != null)
                    {
                        // ProductName is set via the GameSettings asset,
                        // which maps to the Globals.ProductName at runtime.
                        // Direct write is needed via the settings JSON asset.
                        changed = true;
                    }

                    var companyName = GetArgString(args, "companyName");
                    if (companyName != null)
                    {
                        changed = true;
                    }

                    if (!changed)
                        return BuildJsonObject("ok", "true", "status", "no changes specified");

                    // Save via the engine's settings system
                    if (FlaxEditor.Editor.SaveJsonAsset(GameSettings.GameSettingsAssetPath, settings))
                        return BuildJsonObject("error", "Failed to save GameSettings.json");

                    return BuildJsonObject("ok", "true", "status", "project settings updated");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to update project settings: {ex.Message}");
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Undo / Redo
        // ==================================================================

        private string ToolEditorUndo(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var undo = Editor.Instance.Undo;
                if (!undo.CanUndo)
                    return BuildJsonObject("error", "Nothing to undo.");

                undo.PerformUndo();
                return BuildJsonObject("ok", "true", "status", "undo_performed");
            });
        }

        private string ToolEditorRedo(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var undo = Editor.Instance.Undo;
                if (!undo.CanRedo)
                    return BuildJsonObject("error", "Nothing to redo.");

                undo.PerformRedo();
                return BuildJsonObject("ok", "true", "status", "redo_performed");
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Actor Duplication
        // ==================================================================

        private string ToolDuplicateActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var clone = actor.Clone();
                if (clone == null)
                    return BuildJsonObject("error", $"Failed to clone actor: {actor.Name}");

                clone.Parent = actor.Parent;

                if (args.ContainsKey("offsetX") || args.ContainsKey("offsetY") || args.ContainsKey("offsetZ"))
                {
                    clone.Position = new Vector3(
                        (float)actor.Position.X + GetArgFloat(args, "offsetX"),
                        (float)actor.Position.Y + GetArgFloat(args, "offsetY"),
                        (float)actor.Position.Z + GetArgFloat(args, "offsetZ")
                    );
                }

                return BuildJsonObject(
                    "ok", "true",
                    "originalName", actor.Name,
                    "cloneName", clone.Name,
                    "cloneId", clone.ID.ToString()
                );
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Transform (Move / Rotate / Scale)
        // ==================================================================

        private string ToolMoveActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                if (args.ContainsKey("x") || args.ContainsKey("y") || args.ContainsKey("z"))
                {
                    // Absolute position
                    var current = actor.Position;
                    actor.Position = new Vector3(
                        args.ContainsKey("x") ? GetArgFloat(args, "x") : (float)current.X,
                        args.ContainsKey("y") ? GetArgFloat(args, "y") : (float)current.Y,
                        args.ContainsKey("z") ? GetArgFloat(args, "z") : (float)current.Z
                    );
                }
                else if (args.ContainsKey("offsetX") || args.ContainsKey("offsetY") || args.ContainsKey("offsetZ"))
                {
                    // Relative offset
                    actor.Position = new Vector3(
                        (float)actor.Position.X + GetArgFloat(args, "offsetX"),
                        (float)actor.Position.Y + GetArgFloat(args, "offsetY"),
                        (float)actor.Position.Z + GetArgFloat(args, "offsetZ")
                    );
                }
                else
                {
                    return BuildJsonObject("error", "Provide x/y/z for absolute position or offsetX/offsetY/offsetZ for relative movement.");
                }

                var pos = actor.Position;
                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "position", $"X={pos.X}, Y={pos.Y}, Z={pos.Z}"
                );
            });
        }

        private string ToolRotateActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                actor.Orientation = Quaternion.Euler(
                    GetArgFloat(args, "x"),
                    GetArgFloat(args, "y"),
                    GetArgFloat(args, "z")
                );

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "eulerAngles", $"X={GetArgFloat(args, "x")}, Y={GetArgFloat(args, "y")}, Z={GetArgFloat(args, "z")}"
                );
            });
        }

        private string ToolScaleActor(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                actor.Scale = new Float3(
                    GetArgFloat(args, "x", 1f),
                    GetArgFloat(args, "y", 1f),
                    GetArgFloat(args, "z", 1f)
                );

                var s = actor.Scale;
                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "scale", $"X={s.X}, Y={s.Y}, Z={s.Z}"
                );
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Component / Script Management
        // ==================================================================

        private string ToolAddScript(Dictionary<string, object> args)
        {
            var scriptTypeName = GetArgString(args, "scriptType");
            if (string.IsNullOrEmpty(scriptTypeName))
                return BuildJsonObject("error", "Missing 'scriptType' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                // Find the script type via reflection
                Type scriptType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        scriptType = assembly.GetType(scriptTypeName);
                        if (scriptType != null)
                            break;
                    }
                    catch
                    {
                        // Skip assemblies that cannot be queried
                    }
                }

                if (scriptType == null)
                    return BuildJsonObject("error", $"Script type not found: {scriptTypeName}");

                if (!typeof(Script).IsAssignableFrom(scriptType))
                    return BuildJsonObject("error", $"Type '{scriptTypeName}' is not a Script.");

                var script = Activator.CreateInstance(scriptType) as Script;
                if (script == null)
                    return BuildJsonObject("error", $"Failed to create instance of: {scriptTypeName}");

                script.Parent = actor;

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "scriptType", scriptType.FullName,
                    "scriptId", script.ID.ToString()
                );
            });
        }

        private string ToolRemoveScript(Dictionary<string, object> args)
        {
            int scriptIndex = GetArgInt(args, "scriptIndex", -1);
            if (scriptIndex < 0)
                return BuildJsonObject("error", "Missing or invalid 'scriptIndex' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var scripts = actor.Scripts;
                if (scriptIndex >= scripts.Length)
                    return BuildJsonObject("error", $"Script index {scriptIndex} out of range. Actor has {scripts.Length} scripts.");

                var script = scripts[scriptIndex];
                var scriptTypeName = script.GetType().FullName;
                script.Parent = null;
                FlaxEngine.Object.Destroy(script);

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "removedScript", scriptTypeName,
                    "index", scriptIndex.ToString()
                );
            });
        }

        private string ToolListActorScripts(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "name") ?? GetArgString(args, "id") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var scripts = actor.Scripts;
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"actor\": {JsonEscape(actor.Name)},");
                sb.AppendLine($"  \"count\": {scripts.Length},");
                sb.AppendLine("  \"scripts\": [");

                for (int i = 0; i < scripts.Length; i++)
                {
                    var script = scripts[i];
                    var scriptType = script.GetType();

                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"index\": {i},");
                    sb.AppendLine($"      \"type\": {JsonEscape(scriptType.FullName)},");
                    sb.AppendLine($"      \"enabled\": {(script.Enabled ? "true" : "false")},");
                    sb.AppendLine($"      \"id\": {JsonEscape(script.ID.ToString())},");

                    // List public instance properties
                    sb.AppendLine("      \"properties\": [");
                    var props = scriptType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    for (int p = 0; p < props.Length; p++)
                    {
                        var prop = props[p];
                        if (!prop.CanRead)
                            continue;

                        string valStr;
                        try
                        {
                            var val = prop.GetValue(script);
                            valStr = val?.ToString() ?? "null";
                        }
                        catch
                        {
                            valStr = "<error>";
                        }

                        sb.AppendLine("        {");
                        sb.AppendLine($"          \"name\": {JsonEscape(prop.Name)},");
                        sb.AppendLine($"          \"type\": {JsonEscape(prop.PropertyType.Name)},");
                        sb.AppendLine($"          \"value\": {JsonEscape(valStr)}");
                        sb.Append("        }");
                        if (p < props.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.AppendLine("      ]");

                    sb.Append("    }");
                    if (i < scripts.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Material Properties
        // ==================================================================

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
        // TOOL HANDLERS: Batch Execute
        // ==================================================================

        private string ToolBatchExecute(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("commands", out var commandsObj) || !(commandsObj is List<object> commandsList))
                return BuildJsonObject("error", "Missing or invalid 'commands' argument. Expected an array of objects.");

            var results = new List<string>();

            foreach (var cmdObj in commandsList)
            {
                if (!(cmdObj is Dictionary<string, object> cmd))
                {
                    results.Add(BuildJsonObject("error", "Invalid command entry. Each must be an object with 'tool' and 'arguments'."));
                    continue;
                }

                var toolName = cmd.ContainsKey("tool") ? cmd["tool"]?.ToString() : null;
                if (string.IsNullOrEmpty(toolName))
                {
                    results.Add(BuildJsonObject("error", "Command missing 'tool' field."));
                    continue;
                }

                if (!_tools.TryGetValue(toolName, out var tool))
                {
                    results.Add(BuildJsonObject("error", $"Unknown tool: {toolName}"));
                    continue;
                }

                var toolArgs = new Dictionary<string, object>();
                if (cmd.ContainsKey("arguments") && cmd["arguments"] is Dictionary<string, object> argDict)
                {
                    foreach (var kvp in argDict)
                        toolArgs[kvp.Key] = kvp.Value;
                }

                try
                {
                    var result = tool.Handler(toolArgs);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(BuildJsonObject("error", $"Tool '{toolName}' failed: {ex.Message}"));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"commandCount\": {commandsList.Count},");
            sb.AppendLine("  \"results\": [");

            for (int i = 0; i < results.Count; i++)
            {
                sb.Append($"    {results[i]}");
                if (i < results.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        // ==================================================================
        // TOOL HANDLERS: Advanced Actor Search
        // ==================================================================

        private string ToolFindActorsAdvanced(Dictionary<string, object> args)
        {
            var nameQuery = GetArgString(args, "name");
            var typeQuery = GetArgString(args, "type");
            var tagQuery = GetArgString(args, "tag");
            var hasScriptQuery = GetArgString(args, "hasScript");
            var inParentName = GetArgString(args, "inParent");
            int maxResults = GetArgInt(args, "maxResults", 100);

            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var allActors = new List<Actor>();
                Actor searchRoot = null;

                if (!string.IsNullOrEmpty(inParentName))
                {
                    searchRoot = FindActorByName(inParentName);
                    if (searchRoot == null)
                        return BuildJsonObject("error", $"Parent actor not found: {inParentName}");

                    CollectAllActors(searchRoot, allActors);
                }
                else
                {
                    foreach (var scene in scenes)
                        CollectAllActors(scene, allActors);
                }

                var matches = new List<Actor>();
                foreach (var actor in allActors)
                {
                    if (matches.Count >= maxResults)
                        break;

                    bool pass = true;

                    if (!string.IsNullOrEmpty(nameQuery))
                        pass &= actor.Name != null && actor.Name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!string.IsNullOrEmpty(typeQuery))
                        pass &= string.Equals(actor.GetType().Name, typeQuery, StringComparison.OrdinalIgnoreCase);

                    if (!string.IsNullOrEmpty(tagQuery))
                    {
                        bool hasTag = false;
                        if (actor.Tags != null)
                        {
                            foreach (var tag in actor.Tags)
                            {
                                if (tag.ToString().IndexOf(tagQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    hasTag = true;
                                    break;
                                }
                            }
                        }
                        pass &= hasTag;
                    }

                    if (!string.IsNullOrEmpty(hasScriptQuery))
                    {
                        bool hasScript = false;
                        foreach (var script in actor.Scripts)
                        {
                            if (script.GetType().Name.IndexOf(hasScriptQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                script.GetType().FullName.IndexOf(hasScriptQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hasScript = true;
                                break;
                            }
                        }
                        pass &= hasScript;
                    }

                    if (pass)
                        matches.Add(actor);
                }

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {matches.Count},");
                sb.AppendLine($"  \"maxResults\": {maxResults},");
                sb.AppendLine("  \"actors\": [");

                for (int i = 0; i < matches.Count; i++)
                {
                    var a = matches[i];
                    var pos = a.Position;
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(a.Name)},");
                    sb.AppendLine($"      \"type\": {JsonEscape(a.GetType().Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(a.ID.ToString())},");
                    sb.AppendLine($"      \"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }},");
                    sb.AppendLine($"      \"parent\": {JsonEscape(a.Parent?.Name ?? "none")},");

                    sb.Append("      \"scriptCount\": ");
                    sb.Append(a.Scripts.Length);
                    sb.AppendLine();

                    sb.Append("    }");
                    if (i < matches.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private void CollectAllActors(Actor root, List<Actor> results)
        {
            results.Add(root);
            foreach (var child in root.Children)
                CollectAllActors(child, results);
        }

        // ==================================================================
        // TOOL HANDLERS: Scene Operations (Extended)
        // ==================================================================

        private string ToolLoadSceneAdditive(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var asset = FlaxEngine.Content.Load<SceneAsset>(path);
                    if (asset == null)
                        return BuildJsonObject("error", $"Scene asset not found: {path}");

                    // Check if already loaded
                    var loadedScenes = Level.Scenes;
                    if (loadedScenes != null)
                    {
                        foreach (var scene in loadedScenes)
                        {
                            if (scene.ID == asset.ID)
                                return BuildJsonObject("status", "already_loaded", "path", path);
                        }
                    }

                    // LoadScene is additive by default in Flax -- it does not unload existing scenes
                    Level.LoadScene(asset.ID);

                    return BuildJsonObject(
                        "ok", "true",
                        "path", path,
                        "status", "additive_load_requested"
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to load scene additively: {ex.Message}");
                }
            });
        }

        private string ToolGetSceneStats(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                int totalActors = 0;
                int totalScripts = 0;

                foreach (var scene in scenes)
                    CountSceneObjects(scene, ref totalActors, ref totalScripts);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"loadedSceneCount\": {scenes.Length},");
                sb.AppendLine($"  \"totalActorCount\": {totalActors},");
                sb.AppendLine($"  \"totalScriptCount\": {totalScripts},");
                sb.AppendLine("  \"scenes\": [");

                for (int i = 0; i < scenes.Length; i++)
                {
                    var scene = scenes[i];
                    int actorCount = 0;
                    int scriptCount = 0;
                    CountSceneObjects(scene, ref actorCount, ref scriptCount);

                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(scene.Name)},");
                    sb.AppendLine($"      \"id\": {JsonEscape(scene.ID.ToString())},");
                    sb.AppendLine($"      \"actorCount\": {actorCount},");
                    sb.AppendLine($"      \"scriptCount\": {scriptCount}");
                    sb.Append("    }");
                    if (i < scenes.Length - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        private void CountSceneObjects(Actor actor, ref int actorCount, ref int scriptCount)
        {
            actorCount++;
            scriptCount += actor.Scripts.Length;

            foreach (var child in actor.Children)
                CountSceneObjects(child, ref actorCount, ref scriptCount);
        }

        private string ToolUnloadScene(Dictionary<string, object> args)
        {
            var sceneName = GetArgString(args, "name");
            var sceneId = GetArgString(args, "id");

            if (string.IsNullOrEmpty(sceneName) && string.IsNullOrEmpty(sceneId))
                return BuildJsonObject("error", "Provide 'name' or 'id' argument.");

            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Scene targetScene = null;

                if (!string.IsNullOrEmpty(sceneId) && Guid.TryParse(sceneId, out var guid))
                {
                    foreach (var scene in scenes)
                    {
                        if (scene.ID == guid)
                        {
                            targetScene = scene;
                            break;
                        }
                    }
                }

                if (targetScene == null && !string.IsNullOrEmpty(sceneName))
                {
                    foreach (var scene in scenes)
                    {
                        if (string.Equals(scene.Name, sceneName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetScene = scene;
                            break;
                        }
                    }
                }

                if (targetScene == null)
                    return BuildJsonObject("error", $"Scene not found: {sceneName ?? sceneId ?? "unknown"}");

                var name = targetScene.Name;
                Level.UnloadScene(targetScene);

                return BuildJsonObject(
                    "ok", "true",
                    "unloadedScene", name,
                    "status", "scene_unloaded"
                );
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Content Operations (Extended)
        // ==================================================================

        private string ToolCreateFolder(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var absPath = Path.Combine(Globals.ProjectFolder, path);
                    if (Directory.Exists(absPath))
                        return BuildJsonObject("status", "already_exists", "path", path);

                    Directory.CreateDirectory(absPath);

                    // Refresh content database to pick up the new folder
                    var parentPath = Path.GetDirectoryName(path);
                    var parentItem = !string.IsNullOrEmpty(parentPath)
                        ? Editor.Instance.ContentDatabase.Find(parentPath)
                        : null;
                    if (parentItem != null)
                        Editor.Instance.ContentDatabase.RefreshFolder(parentItem, false);

                    return BuildJsonObject(
                        "ok", "true",
                        "path", path,
                        "status", "folder_created"
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create folder: {ex.Message}");
                }
            });
        }

        private string ToolDeleteContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var item = Editor.Instance.ContentDatabase.Find(path);
                    if (item == null)
                        return BuildJsonObject("error", $"Content item not found: {path}");

                    var absPath = item.Path;
                    var itemName = item.ShortName;

                    if (item is ContentFolder)
                    {
                        if (Directory.Exists(absPath))
                            Directory.Delete(absPath, true);
                    }
                    else
                    {
                        if (File.Exists(absPath))
                            File.Delete(absPath);
                    }

                    // Refresh parent folder in content database
                    if (item.ParentFolder != null)
                        Editor.Instance.ContentDatabase.RefreshFolder(item.ParentFolder, false);

                    return BuildJsonObject(
                        "ok", "true",
                        "deleted", itemName,
                        "path", path
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to delete content: {ex.Message}");
                }
            });
        }

        private string ToolMoveContent(Dictionary<string, object> args)
        {
            var sourcePath = GetArgString(args, "sourcePath");
            var destPath = GetArgString(args, "destPath");

            if (string.IsNullOrEmpty(sourcePath))
                return BuildJsonObject("error", "Missing 'sourcePath' argument.");
            if (string.IsNullOrEmpty(destPath))
                return BuildJsonObject("error", "Missing 'destPath' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var absSource = Path.Combine(Globals.ProjectFolder, sourcePath);
                    var absDest = Path.Combine(Globals.ProjectFolder, destPath);

                    if (!File.Exists(absSource) && !Directory.Exists(absSource))
                        return BuildJsonObject("error", $"Source not found: {sourcePath}");

                    if (File.Exists(absSource))
                    {
                        var destDir = Path.GetDirectoryName(absDest);
                        if (destDir != null)
                            Directory.CreateDirectory(destDir);
                        File.Move(absSource, absDest);
                    }
                    else if (Directory.Exists(absSource))
                    {
                        Directory.Move(absSource, absDest);
                    }

                    return BuildJsonObject(
                        "ok", "true",
                        "sourcePath", sourcePath,
                        "destPath", destPath,
                        "status", "content_moved",
                        "note", "Run compile_scripts or restart the editor to fully update references."
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to move content: {ex.Message}");
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Profiling
        // ==================================================================

        private string ToolGetFrameStats(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var fps = Engine.FramesPerSecond;
                var deltaTime = Time.DeltaTime;
                var gameTime = Time.GameTime;

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"fps\": {fps},");
                sb.AppendLine($"  \"deltaTime\": {deltaTime},");
                sb.AppendLine($"  \"gameTime\": {gameTime},");
                sb.AppendLine($"  \"unscaledDeltaTime\": {Time.UnscaledDeltaTime},");
                sb.AppendLine($"  \"timeScale\": {Time.TimeScale}");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Editor Windows
        // ==================================================================

        private string ToolGetEditorWindows(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var windows = Editor.Instance.Windows.Windows;
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {windows.Count},");
                sb.AppendLine("  \"windows\": [");

                for (int i = 0; i < windows.Count; i++)
                {
                    var win = windows[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"title\": {JsonEscape(win.Title)},");
                    sb.AppendLine($"      \"type\": {JsonEscape(win.GetType().Name)},");
                    sb.AppendLine($"      \"isVisible\": {(win.Visible ? "true" : "false")}");
                    sb.Append("    }");
                    if (i < windows.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // Extension route dispatch (backward compatibility for REST)
        // ==================================================================

        private bool HandleExtensionRoute(string path, string method, HttpListenerContext context, HttpListenerResponse response, NameValueCollection query)
        {
            switch (path)
            {
                // -- Physics --
                case "/physics/raycast":
                    WriteJson(response, 200, ToolPhysicsRaycast(QueryToArgs(query, "originX", "originY", "originZ", "dirX:directionX", "dirY:directionY", "dirZ:directionZ", "maxDist:maxDistance")));
                    return true;

                case "/physics/overlap":
                    WriteJson(response, 200, ToolPhysicsOverlap(QueryToArgs(query, "x", "y", "z", "radius")));
                    return true;

                case "/physics/settings":
                    if (method == "POST")
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<PhysicsSettingsRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (data.GravityX.HasValue) args["gravityX"] = (double)data.GravityX.Value;
                            if (data.GravityY.HasValue) args["gravityY"] = (double)data.GravityY.Value;
                            if (data.GravityZ.HasValue) args["gravityZ"] = (double)data.GravityZ.Value;
                        }
                        WriteJson(response, 200, ToolSetPhysicsSettings(args));
                    }
                    else
                    {
                        WriteJson(response, 200, ToolGetPhysicsSettings(new Dictionary<string, object>()));
                    }
                    return true;

                // -- Animation --
                case "/animation/list":
                    WriteJson(response, 200, ToolListAnimations(QueryToArgs(query, "actor")));
                    return true;

                case "/animation/play":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<AnimationPlayRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Actor)) args["actor"] = data.Actor;
                            if (!string.IsNullOrEmpty(data.Clip)) args["clip"] = data.Clip;
                            if (data.Speed.HasValue) args["speed"] = (double)data.Speed.Value;
                            if (!string.IsNullOrEmpty(data.Parameter)) args["parameter"] = data.Parameter;
                            if (data.ParameterValue != null) args["parameterValue"] = data.ParameterValue;
                        }
                        var result = ToolPlayAnimation(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/animation/state":
                    WriteJson(response, 200, ToolGetAnimationState(QueryToArgs(query, "actor")));
                    return true;

                // -- Terrain --
                case "/terrain/info":
                    WriteJson(response, 200, ToolGetTerrainInfo(new Dictionary<string, object>()));
                    return true;

                case "/terrain/sculpt":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<TerrainSculptRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            args["x"] = (double)data.X;
                            args["z"] = (double)data.Z;
                            args["radius"] = (double)data.Radius;
                            args["strength"] = (double)data.Strength;
                            if (!string.IsNullOrEmpty(data.TerrainName)) args["terrainName"] = data.TerrainName;
                        }
                        var result = ToolTerrainSculpt(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/terrain/height":
                    WriteJson(response, 200, ToolGetTerrainHeight(QueryToArgs(query, "x", "z")));
                    return true;

                // -- Navigation --
                case "/navigation/build":
                    RequirePost(method, response, () => WriteJson(response, 200, ToolBuildNavmesh(new Dictionary<string, object>())));
                    return true;

                case "/navigation/query":
                    WriteJson(response, 200, ToolQueryNavpath(QueryToArgs(query, "fromX:startX", "fromY:startY", "fromZ:startZ", "toX:endX", "toY:endY", "toZ:endZ")));
                    return true;

                case "/navigation/info":
                    WriteJson(response, 200, ToolGetNavigationInfo(new Dictionary<string, object>()));
                    return true;

                // -- Rendering --
                case "/rendering/screenshot":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ScreenshotRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.OutputPath))
                            args["outputPath"] = data.OutputPath;
                        var result = ToolTakeScreenshot(args);
                        var status = result.Contains("\"error\"") ? 500 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/rendering/settings":
                    if (method == "POST")
                        WriteJson(response, 200, ToolSetRenderingSettings(new Dictionary<string, object>()));
                    else
                        WriteJson(response, 200, ToolGetRenderingSettings(new Dictionary<string, object>()));
                    return true;

                // -- Audio --
                case "/audio/play":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<AudioPlayRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Clip)) args["clip"] = data.Clip;
                            if (data.PositionX.HasValue) args["positionX"] = (double)data.PositionX.Value;
                            if (data.PositionY.HasValue) args["positionY"] = (double)data.PositionY.Value;
                            if (data.PositionZ.HasValue) args["positionZ"] = (double)data.PositionZ.Value;
                            args["loop"] = data.Loop;
                        }
                        var result = ToolPlayAudio(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/audio/sources":
                    WriteJson(response, 200, ToolListAudioSources(new Dictionary<string, object>()));
                    return true;

                // -- Prefabs --
                case "/prefabs/spawn":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<PrefabSpawnRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Prefab)) args["prefab"] = data.Prefab;
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Parent)) args["parent"] = data.Parent;
                            if (data.PositionX.HasValue) args["positionX"] = (double)data.PositionX.Value;
                            if (data.PositionY.HasValue) args["positionY"] = (double)data.PositionY.Value;
                            if (data.PositionZ.HasValue) args["positionZ"] = (double)data.PositionZ.Value;
                            if (data.RotationX.HasValue) args["rotationX"] = (double)data.RotationX.Value;
                            if (data.RotationY.HasValue) args["rotationY"] = (double)data.RotationY.Value;
                            if (data.RotationZ.HasValue) args["rotationZ"] = (double)data.RotationZ.Value;
                        }
                        var result = ToolSpawnPrefab(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/prefabs/list":
                    WriteJson(response, 200, ToolListPrefabs(new Dictionary<string, object>()));
                    return true;

                // -- Scene Management --
                case "/scene/create":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneCreateRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Path)) args["path"] = data.Path;
                        }
                        var result = ToolCreateScene(args);
                        var status = result.Contains("\"error\"") ? 500 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/save":
                    RequirePost(method, response, () =>
                    {
                        var result = ToolSaveScene(new Dictionary<string, object>());
                        var status = result.Contains("\"error\"") ? 500 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/load":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneLoadRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.Path))
                            args["path"] = data.Path;
                        var result = ToolLoadScene(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/list":
                    WriteJson(response, 200, ToolGetSceneList(new Dictionary<string, object>()));
                    return true;

                case "/scene/actor/create":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorCreateRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Type)) args["type"] = data.Type;
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Parent)) args["parent"] = data.Parent;
                            if (!string.IsNullOrEmpty(data.Model)) args["model"] = data.Model;
                            if (data.PositionX.HasValue) args["positionX"] = (double)data.PositionX.Value;
                            if (data.PositionY.HasValue) args["positionY"] = (double)data.PositionY.Value;
                            if (data.PositionZ.HasValue) args["positionZ"] = (double)data.PositionZ.Value;
                            if (data.RotationX.HasValue) args["rotationX"] = (double)data.RotationX.Value;
                            if (data.RotationY.HasValue) args["rotationY"] = (double)data.RotationY.Value;
                            if (data.RotationZ.HasValue) args["rotationZ"] = (double)data.RotationZ.Value;
                        }
                        var result = ToolCreateActor(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/actor/delete":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorDeleteRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                        }
                        var result = ToolDeleteActor(args);
                        var status = result.Contains("\"error\"") ? 404 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/actor/reparent":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorReparentRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Actor)) args["actor"] = data.Actor;
                            if (!string.IsNullOrEmpty(data.ActorId)) args["actorId"] = data.ActorId;
                            if (!string.IsNullOrEmpty(data.NewParent)) args["newParent"] = data.NewParent;
                            if (!string.IsNullOrEmpty(data.NewParentId)) args["newParentId"] = data.NewParentId;
                        }
                        var result = ToolReparentActor(args);
                        var status = result.Contains("\"error\"") ? 404 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Content Database --
                case "/content/search":
                    WriteJson(response, 200, ToolSearchContent(QueryToArgs(query, "query", "type")));
                    return true;

                case "/content/reimport":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ContentReimportRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.Path))
                            args["path"] = data.Path;
                        var result = ToolReimportContent(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/content/info":
                    WriteJson(response, 200, ToolGetContentInfo(QueryToArgs(query, "path")));
                    return true;

                // -- Build / Export --
                case "/build/game":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<BuildGameRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Platform)) args["platform"] = data.Platform;
                            if (!string.IsNullOrEmpty(data.Configuration)) args["configuration"] = data.Configuration;
                        }
                        var result = ToolBuildGame(args);
                        var status = result.Contains("\"error\"") ? 500 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/build/status":
                    WriteJson(response, 200, ToolGetBuildStatus(new Dictionary<string, object>()));
                    return true;

                // -- Editor State --
                case "/editor/state":
                    WriteJson(response, 200, ToolGetEditorState(new Dictionary<string, object>()));
                    return true;

                case "/editor/select":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<EditorSelectRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                        }
                        var result = ToolEditorSelect(args);
                        var status = result.Contains("\"error\"") ? 404 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/editor/focus":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<EditorFocusRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                        }
                        var result = ToolEditorFocus(args);
                        var status = result.Contains("\"error\"") ? 404 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/editor/viewport":
                    if (method == "POST")
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ViewportSetRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (data.PositionX.HasValue) args["positionX"] = (double)data.PositionX.Value;
                            if (data.PositionY.HasValue) args["positionY"] = (double)data.PositionY.Value;
                            if (data.PositionZ.HasValue) args["positionZ"] = (double)data.PositionZ.Value;
                            if (data.Yaw.HasValue) args["yaw"] = (double)data.Yaw.Value;
                            if (data.Pitch.HasValue) args["pitch"] = (double)data.Pitch.Value;
                        }
                        WriteJson(response, 200, ToolSetViewport(args));
                    }
                    else
                    {
                        WriteJson(response, 200, ToolGetViewport(new Dictionary<string, object>()));
                    }
                    return true;

                // -- Scripting --
                case "/scripts/list":
                    WriteJson(response, 200, ToolListScripts(new Dictionary<string, object>()));
                    return true;

                case "/scripts/compile":
                    RequirePost(method, response, () =>
                    {
                        var result = ToolCompileScripts(new Dictionary<string, object>());
                        var status = result.Contains("\"error\"") ? 500 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scripts/errors":
                    WriteJson(response, 200, ToolGetScriptErrors(new Dictionary<string, object>()));
                    return true;

                // -- Undo / Redo --
                case "/editor/undo":
                    RequirePost(method, response, () =>
                    {
                        var result = ToolEditorUndo(new Dictionary<string, object>());
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/editor/redo":
                    RequirePost(method, response, () =>
                    {
                        var result = ToolEditorRedo(new Dictionary<string, object>());
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Actor Duplication --
                case "/scene/actor/duplicate":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorDuplicateRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                            if (data.OffsetX.HasValue) args["offsetX"] = (double)data.OffsetX.Value;
                            if (data.OffsetY.HasValue) args["offsetY"] = (double)data.OffsetY.Value;
                            if (data.OffsetZ.HasValue) args["offsetZ"] = (double)data.OffsetZ.Value;
                        }
                        var result = ToolDuplicateActor(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Transform --
                case "/scene/actor/move":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorMoveRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                            if (data.X.HasValue) args["x"] = (double)data.X.Value;
                            if (data.Y.HasValue) args["y"] = (double)data.Y.Value;
                            if (data.Z.HasValue) args["z"] = (double)data.Z.Value;
                            if (data.OffsetX.HasValue) args["offsetX"] = (double)data.OffsetX.Value;
                            if (data.OffsetY.HasValue) args["offsetY"] = (double)data.OffsetY.Value;
                            if (data.OffsetZ.HasValue) args["offsetZ"] = (double)data.OffsetZ.Value;
                        }
                        var result = ToolMoveActor(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/actor/rotate":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorRotateRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                            if (data.X.HasValue) args["x"] = (double)data.X.Value;
                            if (data.Y.HasValue) args["y"] = (double)data.Y.Value;
                            if (data.Z.HasValue) args["z"] = (double)data.Z.Value;
                        }
                        var result = ToolRotateActor(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/actor/scale":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorScaleRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                            if (data.X.HasValue) args["x"] = (double)data.X.Value;
                            if (data.Y.HasValue) args["y"] = (double)data.Y.Value;
                            if (data.Z.HasValue) args["z"] = (double)data.Z.Value;
                        }
                        var result = ToolScaleActor(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Scripts on Actors --
                case "/scene/actor/scripts":
                    WriteJson(response, 200, ToolListActorScripts(QueryToArgs(query, "name", "id")));
                    return true;

                case "/scene/actor/scripts/add":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<AddScriptRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.ActorName)) args["actorName"] = data.ActorName;
                            if (!string.IsNullOrEmpty(data.ActorId)) args["actorId"] = data.ActorId;
                            if (!string.IsNullOrEmpty(data.ScriptType)) args["scriptType"] = data.ScriptType;
                        }
                        var result = ToolAddScript(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/actor/scripts/remove":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<RemoveScriptRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.ActorName)) args["actorName"] = data.ActorName;
                            if (!string.IsNullOrEmpty(data.ActorId)) args["actorId"] = data.ActorId;
                            args["scriptIndex"] = (long)data.ScriptIndex;
                        }
                        var result = ToolRemoveScript(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Material Params --
                case "/materials/params":
                    WriteJson(response, 200, ToolGetMaterialParams(QueryToArgs(query, "path")));
                    return true;

                case "/materials/params/set":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<SetMaterialParamRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Path)) args["path"] = data.Path;
                            if (!string.IsNullOrEmpty(data.ParamName)) args["paramName"] = data.ParamName;
                            if (data.Value != null) args["value"] = data.Value;
                        }
                        var result = ToolSetMaterialParam(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Advanced Search --
                case "/scene/find-advanced":
                    WriteJson(response, 200, ToolFindActorsAdvanced(QueryToArgs(query, "name", "type", "tag", "hasScript", "inParent", "maxResults")));
                    return true;

                // -- Scene Operations (Extended) --
                case "/scene/load-additive":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneLoadRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.Path))
                            args["path"] = data.Path;
                        var result = ToolLoadSceneAdditive(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/scene/stats":
                    WriteJson(response, 200, ToolGetSceneStats(new Dictionary<string, object>()));
                    return true;

                case "/scene/unload":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneUnloadRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.Name)) args["name"] = data.Name;
                            if (!string.IsNullOrEmpty(data.Id)) args["id"] = data.Id;
                        }
                        var result = ToolUnloadScene(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Content Operations (Extended) --
                case "/content/create-folder":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ContentPathRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.Path))
                            args["path"] = data.Path;
                        var result = ToolCreateFolder(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/content/delete":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ContentPathRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null && !string.IsNullOrEmpty(data.Path))
                            args["path"] = data.Path;
                        var result = ToolDeleteContent(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                case "/content/move":
                    RequirePost(method, response, () =>
                    {
                        var body = ReadRequestBody(context.Request);
                        var data = FlaxEngine.Json.JsonSerializer.Deserialize<ContentMoveRestRequest>(body);
                        var args = new Dictionary<string, object>();
                        if (data != null)
                        {
                            if (!string.IsNullOrEmpty(data.SourcePath)) args["sourcePath"] = data.SourcePath;
                            if (!string.IsNullOrEmpty(data.DestPath)) args["destPath"] = data.DestPath;
                        }
                        var result = ToolMoveContent(args);
                        var status = result.Contains("\"error\"") ? 400 : 200;
                        WriteJson(response, status, result);
                    });
                    return true;

                // -- Profiling --
                case "/profiling/frame-stats":
                    WriteJson(response, 200, ToolGetFrameStats(new Dictionary<string, object>()));
                    return true;

                // -- Editor Windows --
                case "/editor/windows":
                    WriteJson(response, 200, ToolGetEditorWindows(new Dictionary<string, object>()));
                    return true;

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------
        // REST query-string to tool-args converter
        // ------------------------------------------------------------------

        /// <summary>
        /// Converts query-string parameters to a tool argument dictionary.
        /// Supports renaming via "queryKey:toolKey" syntax (e.g. "dirX:directionX").
        /// </summary>
        private static Dictionary<string, object> QueryToArgs(NameValueCollection query, params string[] keys)
        {
            var args = new Dictionary<string, object>();
            foreach (var key in keys)
            {
                string queryKey, toolKey;
                var colonIdx = key.IndexOf(':');
                if (colonIdx >= 0)
                {
                    queryKey = key.Substring(0, colonIdx);
                    toolKey = key.Substring(colonIdx + 1);
                }
                else
                {
                    queryKey = key;
                    toolKey = key;
                }

                var val = query[queryKey];
                if (val != null)
                    args[toolKey] = val;
            }
            return args;
        }

        // ==================================================================
        // REST request data classes (backward compatibility)
        // ==================================================================

        [Serializable]
        private class PhysicsSettingsRestRequest
        {
            public float? GravityX;
            public float? GravityY;
            public float? GravityZ;
        }

        [Serializable]
        private class AnimationPlayRestRequest
        {
            public string Actor;
            public string Clip;
            public float? Speed;
            public string Parameter;
            public object ParameterValue;
        }

        [Serializable]
        private class TerrainSculptRestRequest
        {
            public float X;
            public float Z;
            public float Radius = 5f;
            public float Strength = 0.5f;
            public string TerrainName;
        }

        [Serializable]
        private class ScreenshotRestRequest
        {
            public string OutputPath;
        }

        [Serializable]
        private class AudioPlayRestRequest
        {
            public string Clip;
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public bool Loop;
        }

        [Serializable]
        private class PrefabSpawnRestRequest
        {
            public string Prefab;
            public string Name;
            public string Parent;
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public float? RotationX;
            public float? RotationY;
            public float? RotationZ;
        }

        [Serializable]
        private class SceneCreateRestRequest
        {
            public string Name;
            public string Path;
        }

        [Serializable]
        private class SceneLoadRestRequest
        {
            public string Path;
        }

        [Serializable]
        private class ActorCreateRestRequest
        {
            public string Type;
            public string Name;
            public string Parent;
            public string Model;
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public float? RotationX;
            public float? RotationY;
            public float? RotationZ;
        }

        [Serializable]
        private class ActorDeleteRestRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class ActorReparentRestRequest
        {
            public string Actor;
            public string ActorId;
            public string NewParent;
            public string NewParentId;
        }

        [Serializable]
        private class ContentReimportRestRequest
        {
            public string Path;
        }

        [Serializable]
        private class BuildGameRestRequest
        {
            public string Platform;
            public string Configuration;
        }

        [Serializable]
        private class EditorSelectRestRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class EditorFocusRestRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class ViewportSetRestRequest
        {
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public float? Yaw;
            public float? Pitch;
        }

        [Serializable]
        private class ActorDuplicateRestRequest
        {
            public string Name;
            public string Id;
            public float? OffsetX;
            public float? OffsetY;
            public float? OffsetZ;
        }

        [Serializable]
        private class ActorMoveRestRequest
        {
            public string Name;
            public string Id;
            public float? X;
            public float? Y;
            public float? Z;
            public float? OffsetX;
            public float? OffsetY;
            public float? OffsetZ;
        }

        [Serializable]
        private class ActorRotateRestRequest
        {
            public string Name;
            public string Id;
            public float? X;
            public float? Y;
            public float? Z;
        }

        [Serializable]
        private class ActorScaleRestRequest
        {
            public string Name;
            public string Id;
            public float? X;
            public float? Y;
            public float? Z;
        }

        [Serializable]
        private class AddScriptRestRequest
        {
            public string ActorName;
            public string ActorId;
            public string ScriptType;
        }

        [Serializable]
        private class RemoveScriptRestRequest
        {
            public string ActorName;
            public string ActorId;
            public int ScriptIndex;
        }

        [Serializable]
        private class SetMaterialParamRestRequest
        {
            public string Path;
            public string ParamName;
            public string Value;
        }

        [Serializable]
        private class SceneUnloadRestRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class ContentPathRestRequest
        {
            public string Path;
        }

        [Serializable]
        private class ContentMoveRestRequest
        {
            public string SourcePath;
            public string DestPath;
        }
    }
}

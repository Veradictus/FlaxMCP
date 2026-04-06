using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlaxEditor;
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// Scene-related MCP tool handlers. Covers scene CRUD (create, save, load,
    /// unload, save-as), actor CRUD (create, get, find, find-advanced, delete),
    /// actor transforms (move, rotate, scale, duplicate), actor properties
    /// (set property, set tag), reparenting, and scene hierarchy/stats/list queries.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Scene
        // ==================================================================

        /// <summary>
        /// Returns the full scene hierarchy tree with all actors.
        /// </summary>
        private string ToolGetSceneHierarchy(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return "{ \"scenes\": [] }";

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

        /// <summary>
        /// Lists all scene assets and currently loaded scenes.
        /// </summary>
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

        /// <summary>
        /// Gets detailed information about a specific actor by name or GUID.
        /// </summary>
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

        /// <summary>
        /// Searches for actors by name, type, or tag. All filters are optional.
        /// </summary>
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

        /// <summary>
        /// Sets a property on an actor by name or id using reflection-based path resolution.
        /// </summary>
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

        /// <summary>
        /// Creates a new actor in the scene with optional position, rotation, and model.
        /// </summary>
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

        /// <summary>
        /// Deletes an actor from the scene by name or GUID.
        /// </summary>
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

        /// <summary>
        /// Moves an actor to a new parent in the scene hierarchy.
        /// </summary>
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

        /// <summary>
        /// Creates a new scene asset at the specified path.
        /// </summary>
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

                    Debug.Log($"[McpServer] Creating scene at: {absPath}");
                    Editor.Instance.Scene.CreateSceneFile(absPath);

                    if (!File.Exists(absPath))
                        return BuildJsonObject("error", $"Scene file not found after creation at: {absPath}");

                    return BuildJsonObject(
                        "created", "true",
                        "name", sceneName,
                        "path", outputPath,
                        "absolutePath", absPath
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create scene: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Saves all currently loaded scenes.
        /// </summary>
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

        /// <summary>
        /// Loads a scene by asset path, replacing currently loaded scenes.
        /// </summary>
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
        // TOOL HANDLERS: Actor Duplication
        // ==================================================================

        /// <summary>
        /// Duplicates an actor with an optional positional offset for the clone.
        /// </summary>
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

                // Generate unique name for the clone
                var baseName = actor.Name;
                var siblings = actor.Parent?.Children;
                if (siblings != null)
                {
                    int suffix = 1;
                    var candidateName = $"{baseName} ({suffix})";
                    while (siblings.Any(c => c.Name == candidateName))
                    {
                        suffix++;
                        candidateName = $"{baseName} ({suffix})";
                    }
                    clone.Name = candidateName;
                }

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

        /// <summary>
        /// Moves an actor to an absolute position or by a relative offset.
        /// </summary>
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

        /// <summary>
        /// Sets the rotation of an actor using Euler angles (degrees).
        /// </summary>
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

        /// <summary>
        /// Sets the scale of an actor.
        /// </summary>
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
        // TOOL HANDLERS: Advanced Actor Search
        // ==================================================================

        /// <summary>
        /// Searches for actors with multiple filters combined with AND logic.
        /// Supports name, type, tag, script presence, and parent-scoped search.
        /// </summary>
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

        /// <summary>
        /// Recursively collects all actors starting from a root actor.
        /// </summary>
        private void CollectAllActors(Actor root, List<Actor> results)
        {
            results.Add(root);
            foreach (var child in root.Children)
                CollectAllActors(child, results);
        }

        // ==================================================================
        // TOOL HANDLERS: Scene Operations (Extended)
        // ==================================================================

        /// <summary>
        /// Loads a scene additively without unloading currently loaded scenes.
        /// </summary>
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

        /// <summary>
        /// Returns scene statistics: actor count, script count, loaded scene count, etc.
        /// </summary>
        private string ToolGetSceneStats(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return "{ \"loadedSceneCount\": 0, \"totalActorCount\": 0, \"totalScriptCount\": 0, \"scenes\": [] }";

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

        /// <summary>
        /// Recursively counts actors and scripts starting from a root actor.
        /// </summary>
        private void CountSceneObjects(Actor actor, ref int actorCount, ref int scriptCount)
        {
            actorCount++;
            scriptCount += actor.Scripts.Length;

            foreach (var child in actor.Children)
                CountSceneObjects(child, ref actorCount, ref scriptCount);
        }

        /// <summary>
        /// Unloads a specific scene by name or GUID.
        /// </summary>
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
        // TOOL HANDLERS: Actor Tags
        // ==================================================================

        /// <summary>
        /// Adds or removes a tag on an actor. Flax 1.11 uses a Tags array.
        /// </summary>
        private string ToolSetActorTag(Dictionary<string, object> args)
        {
            var tag = GetArgString(args, "tag");
            if (string.IsNullOrEmpty(tag))
                return BuildJsonObject("error", "Missing 'tag' argument.");

            var remove = GetArgBool(args, "remove", false);

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var tagObj = Tags.Get(tag);

                if (remove)
                {
                    var tags = new List<Tag>(actor.Tags);
                    tags.RemoveAll(t => t.ToString() == tag);
                    actor.Tags = tags.ToArray();
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "action", "removed",
                        "tag", tag
                    );
                }
                else
                {
                    // Check if already has the tag
                    var tags = new List<Tag>(actor.Tags);
                    if (!tags.Any(t => t.ToString() == tag))
                    {
                        tags.Add(tagObj);
                        actor.Tags = tags.ToArray();
                    }
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "action", "added",
                        "tag", tag
                    );
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Save Scene As
        // ==================================================================

        /// <summary>
        /// Saves a loaded scene to a new file path, effectively cloning it.
        /// </summary>
        private string ToolSaveSceneAs(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var sceneName = GetArgString(args, "sceneName");
                Scene targetScene = null;

                if (!string.IsNullOrEmpty(sceneName))
                {
                    foreach (var s in scenes)
                    {
                        if (string.Equals(s.Name, sceneName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetScene = s;
                            break;
                        }
                    }
                    if (targetScene == null)
                        return BuildJsonObject("error", $"Scene not found: {sceneName}");
                }
                else
                {
                    targetScene = scenes[0];
                }

                try
                {
                    var absPath = Path.Combine(Globals.ProjectFolder, path);
                    var dir = Path.GetDirectoryName(absPath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    // Serialize scene to bytes and write to new location
                    var bytes = Level.SaveSceneToBytes(targetScene);
                    if (bytes == null || bytes.Length == 0)
                        return BuildJsonObject("error", "Failed to serialize scene.");

                    using (var fs = new FileStream(absPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        fs.Write(bytes, 0, bytes.Length);

                    return BuildJsonObject(
                        "ok", "true",
                        "scene", targetScene.Name,
                        "path", path
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to save scene: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }
    }
}

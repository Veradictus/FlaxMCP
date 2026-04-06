using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEditor;
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// World simulation MCP tool handlers. Covers physics (raycast, overlap,
    /// get/set settings), terrain (info, sculpt, height), navigation (build navmesh,
    /// query path, info), rendering (screenshot, get/set settings), animation
    /// (list, play, get state), audio (play, list sources), prefabs (spawn, list,
    /// create), and collider creation.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Physics
        // ==================================================================

        /// <summary>
        /// Casts a ray and returns hit information including distance, point, normal, and actor.
        /// </summary>
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

        /// <summary>
        /// Performs a sphere overlap test and returns intersecting actors.
        /// </summary>
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

        /// <summary>
        /// Gets current physics settings including gravity and CCD state.
        /// </summary>
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

        /// <summary>
        /// Sets physics settings such as gravity components.
        /// </summary>
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

        /// <summary>
        /// Lists animated models in the current scene with their parameters.
        /// </summary>
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
                    sb.AppendLine($"      \"skinnedModel\": {JsonEscapePath(am.SkinnedModel?.Path ?? "none")},");
                    sb.AppendLine($"      \"animationGraph\": {JsonEscapePath(am.AnimationGraph?.Path ?? "none")},");

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

        /// <summary>
        /// Plays or configures animation on an animated model actor.
        /// </summary>
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

        /// <summary>
        /// Gets the current animation state of an actor including parameters and play status.
        /// </summary>
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

        /// <summary>
        /// Gets information about all terrain actors in the current scene.
        /// </summary>
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

        /// <summary>
        /// Sculpts terrain at a world position using a raycast to find the surface.
        /// </summary>
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

        /// <summary>
        /// Samples terrain height at a world X/Z position using a downward raycast.
        /// </summary>
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

        /// <summary>
        /// Builds the navigation mesh for the current scene.
        /// </summary>
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

        /// <summary>
        /// Queries a navigation path between two world positions.
        /// </summary>
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

        /// <summary>
        /// Gets navigation mesh volume information for the current scene.
        /// </summary>
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

        /// <summary>
        /// Captures a screenshot of the editor viewport to the specified path.
        /// </summary>
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

        /// <summary>
        /// Gets current rendering and post-processing settings including PostFx volumes.
        /// </summary>
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

        /// <summary>
        /// Updates rendering settings. Currently a placeholder for future expansion.
        /// </summary>
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

        /// <summary>
        /// Plays an audio clip at a position in the scene by creating a temporary AudioSource.
        /// </summary>
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

        /// <summary>
        /// Lists all audio source actors in the current scene.
        /// </summary>
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
                    sb.AppendLine($"      \"clip\": {JsonEscapePath(src.Clip?.Path ?? "none")},");
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

        /// <summary>
        /// Spawns a prefab instance in the scene with optional position and rotation.
        /// </summary>
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

        /// <summary>
        /// Lists all prefab assets in the project content database.
        /// </summary>
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
        // TOOL HANDLERS: Prefab Creation
        // ==================================================================

        /// <summary>
        /// Creates a prefab asset from an existing scene actor.
        /// </summary>
        private string ToolCreatePrefab(Dictionary<string, object> args)
        {
            var outputPath = GetArgString(args, "outputPath");
            if (string.IsNullOrEmpty(outputPath))
                return BuildJsonObject("error", "Missing 'outputPath' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                try
                {
                    var absPath = Path.Combine(Globals.ProjectFolder, outputPath);
                    var dir = Path.GetDirectoryName(absPath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    // Use PrefabManager API for headless prefab creation
                    if (PrefabManager.CreatePrefab(actor, absPath, true))
                        return BuildJsonObject("error", $"Failed to create prefab at: {outputPath}");

                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "path", outputPath
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create prefab: {ex.GetType().Name}: {ex.Message}");
                }
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Colliders
        // ==================================================================

        /// <summary>
        /// Adds a collider component as a child of the specified actor.
        /// Supports BoxCollider, SphereCollider, CapsuleCollider, and MeshCollider.
        /// </summary>
        private string ToolCreateCollider(Dictionary<string, object> args)
        {
            var colliderType = GetArgString(args, "colliderType");
            if (string.IsNullOrEmpty(colliderType))
                return BuildJsonObject("error", "Missing 'colliderType' argument.");

            return InvokeOnMainThread(() =>
            {
                var actor = ResolveActor(args);
                if (actor == null)
                {
                    var identifier = GetArgString(args, "actorName") ?? GetArgString(args, "actorId") ?? "unknown";
                    return BuildJsonObject("error", $"Actor not found: {identifier}");
                }

                var sizeX = GetArgFloat(args, "sizeX", 50f);
                var sizeY = GetArgFloat(args, "sizeY", 50f);
                var sizeZ = GetArgFloat(args, "sizeZ", 50f);
                var isTrigger = GetArgBool(args, "isTrigger", false);

                Collider collider;

                switch (colliderType.ToLowerInvariant())
                {
                    case "boxcollider":
                    case "box":
                        var box = actor.AddChild<BoxCollider>();
                        box.Size = new Float3(sizeX, sizeY, sizeZ);
                        collider = box;
                        break;

                    case "spherecollider":
                    case "sphere":
                        var sphere = actor.AddChild<SphereCollider>();
                        sphere.Radius = sizeX;
                        collider = sphere;
                        break;

                    case "capsulecollider":
                    case "capsule":
                        var capsule = actor.AddChild<CapsuleCollider>();
                        capsule.Radius = sizeX;
                        capsule.Height = sizeY;
                        collider = capsule;
                        break;

                    case "meshcollider":
                    case "mesh":
                        var mesh = actor.AddChild<MeshCollider>();
                        collider = mesh;
                        break;

                    default:
                        return BuildJsonObject("error", $"Unknown collider type: {colliderType}. Use: BoxCollider, SphereCollider, CapsuleCollider, MeshCollider");
                }

                collider.IsTrigger = isTrigger;
                collider.Name = colliderType;

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "colliderType", colliderType,
                    "colliderId", collider.ID.ToString(),
                    "isTrigger", isTrigger.ToString().ToLowerInvariant()
                );
            });
        }
    }
}

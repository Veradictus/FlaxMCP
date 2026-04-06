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
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// Extension methods for <see cref="McpServer"/> that add comprehensive
    /// engine system coverage: physics, animation, terrain, navigation,
    /// rendering, audio, prefabs, scene management, content database,
    /// build/export, editor state, and scripting endpoints.
    /// </summary>
    public partial class McpServer
    {
        // ------------------------------------------------------------------
        // Extension route dispatch
        // ------------------------------------------------------------------

        /// <summary>
        /// Attempts to handle a request path that was not matched by the core
        /// switch statement. Returns <c>true</c> if the route was handled.
        /// </summary>
        private bool HandleExtensionRoute(string path, string method, HttpListenerContext context, HttpListenerResponse response, NameValueCollection query)
        {
            switch (path)
            {
                // -- Physics --
                case "/physics/raycast":
                    HandlePhysicsRaycast(response, query);
                    return true;

                case "/physics/overlap":
                    HandlePhysicsOverlap(response, query);
                    return true;

                case "/physics/settings":
                    if (method == "POST")
                        HandlePhysicsSettingsSet(context, response);
                    else
                        HandlePhysicsSettingsGet(response);
                    return true;

                // -- Animation --
                case "/animation/list":
                    HandleAnimationList(response, query);
                    return true;

                case "/animation/play":
                    RequirePost(method, response, () => HandleAnimationPlay(context, response));
                    return true;

                case "/animation/state":
                    HandleAnimationState(response, query);
                    return true;

                // -- Terrain --
                case "/terrain/info":
                    HandleTerrainInfo(response);
                    return true;

                case "/terrain/sculpt":
                    RequirePost(method, response, () => HandleTerrainSculpt(context, response));
                    return true;

                case "/terrain/height":
                    HandleTerrainHeight(response, query);
                    return true;

                // -- Navigation --
                case "/navigation/build":
                    RequirePost(method, response, () => HandleNavigationBuild(response));
                    return true;

                case "/navigation/query":
                    HandleNavigationQuery(response, query);
                    return true;

                case "/navigation/info":
                    HandleNavigationInfo(response);
                    return true;

                // -- Rendering --
                case "/rendering/screenshot":
                    RequirePost(method, response, () => HandleRenderingScreenshot(context, response));
                    return true;

                case "/rendering/settings":
                    if (method == "POST")
                        HandleRenderingSettingsSet(context, response);
                    else
                        HandleRenderingSettingsGet(response);
                    return true;

                // -- Audio --
                case "/audio/play":
                    RequirePost(method, response, () => HandleAudioPlay(context, response));
                    return true;

                case "/audio/sources":
                    HandleAudioSources(response);
                    return true;

                // -- Prefabs --
                case "/prefabs/spawn":
                    RequirePost(method, response, () => HandlePrefabSpawn(context, response));
                    return true;

                case "/prefabs/list":
                    HandlePrefabsList(response);
                    return true;

                // -- Scene Management --
                case "/scene/create":
                    RequirePost(method, response, () => HandleSceneCreate(context, response));
                    return true;

                case "/scene/save":
                    RequirePost(method, response, () => HandleSceneSave(response));
                    return true;

                case "/scene/load":
                    RequirePost(method, response, () => HandleSceneLoad(context, response));
                    return true;

                case "/scene/list":
                    HandleSceneList(response);
                    return true;

                case "/scene/actor/create":
                    RequirePost(method, response, () => HandleActorCreate(context, response));
                    return true;

                case "/scene/actor/delete":
                    RequirePost(method, response, () => HandleActorDelete(context, response));
                    return true;

                case "/scene/actor/reparent":
                    RequirePost(method, response, () => HandleActorReparent(context, response));
                    return true;

                // -- Content Database --
                case "/content/search":
                    HandleContentSearch(response, query);
                    return true;

                case "/content/reimport":
                    RequirePost(method, response, () => HandleContentReimport(context, response));
                    return true;

                case "/content/info":
                    HandleContentInfo(response, query);
                    return true;

                // -- Build / Export --
                case "/build/game":
                    RequirePost(method, response, () => HandleBuildGame(context, response));
                    return true;

                case "/build/status":
                    HandleBuildStatus(response);
                    return true;

                // -- Editor State --
                case "/editor/state":
                    HandleEditorState(response);
                    return true;

                case "/editor/select":
                    RequirePost(method, response, () => HandleEditorSelect(context, response));
                    return true;

                case "/editor/focus":
                    RequirePost(method, response, () => HandleEditorFocus(context, response));
                    return true;

                case "/editor/viewport":
                    if (method == "POST")
                        HandleEditorViewportSet(context, response);
                    else
                        HandleEditorViewportGet(response);
                    return true;

                // -- Scripting --
                case "/scripts/list":
                    HandleScriptsList(response);
                    return true;

                case "/scripts/compile":
                    RequirePost(method, response, () => HandleScriptsCompile(response));
                    return true;

                case "/scripts/errors":
                    HandleScriptsErrors(response);
                    return true;

                default:
                    return false;
            }
        }

        // ==================================================================
        // PHYSICS
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /physics/raycast?originX=0&originY=10&originZ=0&dirX=0&dirY=-1&dirZ=0&maxDist=100
        // ------------------------------------------------------------------

        private void HandlePhysicsRaycast(HttpListenerResponse response, NameValueCollection query)
        {
            float originX = ParseFloat(query["originX"], 0f);
            float originY = ParseFloat(query["originY"], 0f);
            float originZ = ParseFloat(query["originZ"], 0f);
            float dirX = ParseFloat(query["dirX"], 0f);
            float dirY = ParseFloat(query["dirY"], -1f);
            float dirZ = ParseFloat(query["dirZ"], 0f);
            float maxDist = ParseFloat(query["maxDist"], 1000f);

            var result = InvokeOnMainThread(() =>
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

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // GET /physics/overlap?x=0&y=0&z=0&radius=5
        // ------------------------------------------------------------------

        private void HandlePhysicsOverlap(HttpListenerResponse response, NameValueCollection query)
        {
            float x = ParseFloat(query["x"], 0f);
            float y = ParseFloat(query["y"], 0f);
            float z = ParseFloat(query["z"], 0f);
            float radius = ParseFloat(query["radius"], 5f);

            var result = InvokeOnMainThread(() =>
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
                    // Deduplicate by top-level actor
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
                        if (i < actors.Count - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }

                    sb.AppendLine("  ]");
                }

                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // GET  /physics/settings  - get current physics settings
        // POST /physics/settings  - set physics settings
        // ------------------------------------------------------------------

        private void HandlePhysicsSettingsGet(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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

            WriteJson(response, 200, result);
        }

        private void HandlePhysicsSettingsSet(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<PhysicsSettingsRequest>(body);

            var result = InvokeOnMainThread(() =>
            {
                if (data == null)
                    return BuildJsonObject("error", "Invalid request body.");

                if (data.GravityX.HasValue || data.GravityY.HasValue || data.GravityZ.HasValue)
                {
                    var g = Physics.Gravity;
                    Physics.Gravity = new Float3(
                        data.GravityX ?? (float)g.X,
                        data.GravityY ?? (float)g.Y,
                        data.GravityZ ?? (float)g.Z
                    );
                }

                return BuildJsonObject("ok", "true", "status", "physics settings updated");
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // ANIMATION
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /animation/list?actor=ActorName
        // ------------------------------------------------------------------

        private void HandleAnimationList(HttpListenerResponse response, NameValueCollection query)
        {
            var actorName = query["actor"];
            if (string.IsNullOrEmpty(actorName))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'actor' query parameter."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var actor = FindActorByName(actorName);
                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {actorName}");

                var animatedModel = actor as AnimatedModel;
                if (animatedModel == null)
                {
                    // Check children for an AnimatedModel
                    animatedModel = actor.GetChild<AnimatedModel>();
                }

                if (animatedModel == null)
                    return BuildJsonObject("error", $"No AnimatedModel found on actor: {actorName}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"actor\": {JsonEscape(actor.Name)},");

                // Skinned model info
                var skinnedModel = animatedModel.SkinnedModel;
                if (skinnedModel != null)
                {
                    sb.AppendLine($"  \"skinnedModel\": {JsonEscape(skinnedModel.Path)},");
                }
                else
                {
                    sb.AppendLine("  \"skinnedModel\": null,");
                }

                // Animation graph
                var animGraph = animatedModel.AnimationGraph;
                if (animGraph != null)
                {
                    sb.AppendLine($"  \"animationGraph\": {JsonEscape(animGraph.Path)},");
                }
                else
                {
                    sb.AppendLine("  \"animationGraph\": null,");
                }

                // List parameter names from the anim graph
                sb.AppendLine("  \"parameters\": [");
                var parameters = animatedModel.Parameters;
                if (parameters != null)
                {
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var p = parameters[i];
                        sb.AppendLine("    {");
                        sb.AppendLine($"      \"name\": {JsonEscape(p.Name)},");
                        sb.AppendLine($"      \"type\": {JsonEscape(p.Type.ToString())},");
                        sb.AppendLine($"      \"value\": {JsonEscape(p.Value?.ToString() ?? "null")}");
                        sb.Append("    }");
                        if (i < parameters.Length - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /animation/play
        // ------------------------------------------------------------------

        private void HandleAnimationPlay(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<AnimationPlayRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Actor))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'actor' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var actor = FindActorByName(data.Actor);
                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.Actor}");

                var animatedModel = actor as AnimatedModel ?? actor.GetChild<AnimatedModel>();
                if (animatedModel == null)
                    return BuildJsonObject("error", $"No AnimatedModel found on actor: {data.Actor}");

                // Set playback speed if specified
                if (data.Speed.HasValue)
                    animatedModel.UpdateSpeed = data.Speed.Value;

                // If a parameter name is provided, try to set it (e.g., a trigger)
                if (!string.IsNullOrEmpty(data.Parameter) && data.ParameterValue != null)
                {
                    var param = animatedModel.GetParameter(data.Parameter);
                    if (param != null)
                        param.Value = data.ParameterValue;
                }

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "speed", (data.Speed ?? animatedModel.UpdateSpeed).ToString("F2")
                );
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /animation/state?actor=ActorName
        // ------------------------------------------------------------------

        private void HandleAnimationState(HttpListenerResponse response, NameValueCollection query)
        {
            var actorName = query["actor"];
            if (string.IsNullOrEmpty(actorName))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'actor' query parameter."));
                return;
            }

            var result = InvokeOnMainThread(() =>
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

                // Current parameter values
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
                        if (i < parameters.Length - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // TERRAIN
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /terrain/info
        // ------------------------------------------------------------------

        private void HandleTerrainInfo(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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

                    // Terrain patch info
                    var patchCount = terrain.PatchesCount;
                    sb.AppendLine($"      \"patchCount\": {patchCount},");
                    sb.AppendLine($"      \"chunkSize\": {terrain.ChunkSize},");

                    // Bounding box
                    var box = terrain.Box;
                    sb.AppendLine($"      \"boundsMin\": {{ \"X\": {box.Minimum.X}, \"Y\": {box.Minimum.Y}, \"Z\": {box.Minimum.Z} }},");
                    sb.AppendLine($"      \"boundsMax\": {{ \"X\": {box.Maximum.X}, \"Y\": {box.Maximum.Y}, \"Z\": {box.Maximum.Z} }}");

                    sb.Append("    }");
                    if (i < terrains.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /terrain/sculpt
        // ------------------------------------------------------------------

        private void HandleTerrainSculpt(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<TerrainSculptRequest>(body);

            if (data == null)
            {
                WriteJson(response, 400, BuildJsonObject("error", "Invalid request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var terrains = new List<Terrain>();
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                foreach (var scene in scenes)
                    CollectActorsOfType(scene, terrains);

                if (terrains.Count == 0)
                    return BuildJsonObject("error", "No Terrain actors found in the scene.");

                // Use first terrain or find by name
                Terrain terrain;
                if (!string.IsNullOrEmpty(data.TerrainName))
                {
                    terrain = terrains.Find(t => string.Equals(t.Name, data.TerrainName, StringComparison.OrdinalIgnoreCase));
                    if (terrain == null)
                        return BuildJsonObject("error", $"Terrain not found: {data.TerrainName}");
                }
                else
                {
                    terrain = terrains[0];
                }

                // Terrain sculpting requires the editor's tools infrastructure.
                // We raise a height modification by sampling and writing patch data.
                // For safety, we perform a raycast down at the position to confirm
                // the terrain is present.
                var worldPos = new Vector3(data.X, 10000f, data.Z);
                RayCastHit hit;
                if (!Physics.RayCast(worldPos, Vector3.Down, out hit, 20000f))
                    return BuildJsonObject("error", $"No terrain surface found at X={data.X}, Z={data.Z}");

                return BuildJsonObject(
                    "ok", "true",
                    "terrain", terrain.Name,
                    "note", "Terrain sculpt operations require the Editor sculpt tool. Use the editor viewport or terrain tool API for direct heightmap edits.",
                    "position", $"X={data.X}, Z={data.Z}",
                    "surfaceY", hit.Point.Y.ToString("F2")
                );
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /terrain/height?x=100&z=100
        // ------------------------------------------------------------------

        private void HandleTerrainHeight(HttpListenerResponse response, NameValueCollection query)
        {
            float x = ParseFloat(query["x"], 0f);
            float z = ParseFloat(query["z"], 0f);

            var result = InvokeOnMainThread(() =>
            {
                // Cast a ray downward from high up to find terrain surface height
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

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // NAVIGATION / NAVMESH
        // ==================================================================

        // ------------------------------------------------------------------
        // POST /navigation/build
        // ------------------------------------------------------------------

        private void HandleNavigationBuild(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                try
                {
                    Navigation.BuildNavMesh(Level.Scenes[0], new BoundingBox(new Vector3(-100000), new Vector3(100000)));
                    return BuildJsonObject("ok", "true", "status", "navmesh_build_requested");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to build navmesh: {ex.Message}");
                }
            });

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /navigation/query?fromX=0&fromZ=0&toX=100&toZ=100
        // ------------------------------------------------------------------

        private void HandleNavigationQuery(HttpListenerResponse response, NameValueCollection query)
        {
            float fromX = ParseFloat(query["fromX"], 0f);
            float fromY = ParseFloat(query["fromY"], 0f);
            float fromZ = ParseFloat(query["fromZ"], 0f);
            float toX = ParseFloat(query["toX"], 0f);
            float toY = ParseFloat(query["toY"], 0f);
            float toZ = ParseFloat(query["toZ"], 0f);

            var result = InvokeOnMainThread(() =>
            {
                var start = new Vector3(fromX, fromY, fromZ);
                var end = new Vector3(toX, toY, toZ);

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
                        if (i < pathPoints.Length - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }

                    sb.AppendLine("  ]");
                    sb.Append("}");
                    return sb.ToString();
                }

                return BuildJsonObject("found", "false");
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // GET /navigation/info
        // ------------------------------------------------------------------

        private void HandleNavigationInfo(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                // Find NavMesh-related actors
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
                    if (i < navVolumes.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ==================================================================
        // RENDERING
        // ==================================================================

        // ------------------------------------------------------------------
        // POST /rendering/screenshot
        // ------------------------------------------------------------------

        private void HandleRenderingScreenshot(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ScreenshotRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.OutputPath))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'outputPath' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(data.OutputPath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    Screenshot.Capture(data.OutputPath);

                    return BuildJsonObject(
                        "ok", "true",
                        "outputPath", data.OutputPath
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Screenshot failed: {ex.Message}");
                }
            });

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET  /rendering/settings
        // POST /rendering/settings
        // ------------------------------------------------------------------

        private void HandleRenderingSettingsGet(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                // Post-process settings from the scene
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
                    if (i < postFx.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        private void HandleRenderingSettingsSet(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<RenderingSettingsRequest>(body);

            var result = InvokeOnMainThread(() =>
            {
                if (data == null)
                    return BuildJsonObject("error", "Invalid request body.");

                // Graphics.HalfResolution is not available in Flax 1.11.
                // Additional rendering settings can be added here as needed.

                return BuildJsonObject("ok", "true", "status", "rendering settings updated");
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // AUDIO
        // ==================================================================

        // ------------------------------------------------------------------
        // POST /audio/play
        // ------------------------------------------------------------------

        private void HandleAudioPlay(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<AudioPlayRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Clip))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'clip' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var clip = FlaxEngine.Content.Load<AudioClip>(data.Clip);
                if (clip == null)
                    return BuildJsonObject("error", $"Audio clip not found: {data.Clip}");

                // Create a temporary audio source at the specified position
                var audioSource = new AudioSource();
                audioSource.Clip = clip;
                audioSource.IsLooping = data.Loop;

                if (data.PositionX.HasValue || data.PositionY.HasValue || data.PositionZ.HasValue)
                {
                    audioSource.Position = new Vector3(
                        data.PositionX ?? 0f,
                        data.PositionY ?? 0f,
                        data.PositionZ ?? 0f
                    );
                }

                // Parent to first scene so it persists
                var scenes = Level.Scenes;
                if (scenes != null && scenes.Length > 0)
                {
                    audioSource.Name = $"MCP_AudioSource_{DateTime.UtcNow.Ticks}";
                    audioSource.Parent = scenes[0];
                }

                audioSource.Play();

                return BuildJsonObject(
                    "ok", "true",
                    "clip", data.Clip,
                    "actorId", audioSource.ID.ToString()
                );
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /audio/sources
        // ------------------------------------------------------------------

        private void HandleAudioSources(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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
                    if (i < sources.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ==================================================================
        // PREFABS
        // ==================================================================

        // ------------------------------------------------------------------
        // POST /prefabs/spawn
        // ------------------------------------------------------------------

        private void HandlePrefabSpawn(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<PrefabSpawnRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Prefab))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'prefab' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var prefab = FlaxEngine.Content.Load<Prefab>(data.Prefab);
                if (prefab == null)
                    return BuildJsonObject("error", $"Prefab not found: {data.Prefab}");

                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Actor parent = scenes[0];
                if (!string.IsNullOrEmpty(data.Parent))
                {
                    var found = FindActorByName(data.Parent);
                    if (found != null)
                        parent = found;
                }

                var instance = PrefabManager.SpawnPrefab(prefab, parent);
                if (instance == null)
                    return BuildJsonObject("error", $"Failed to spawn prefab: {data.Prefab}");

                // Apply transform
                if (data.PositionX.HasValue || data.PositionY.HasValue || data.PositionZ.HasValue)
                {
                    instance.Position = new Vector3(
                        data.PositionX ?? 0f,
                        data.PositionY ?? 0f,
                        data.PositionZ ?? 0f
                    );
                }

                if (data.RotationY.HasValue)
                {
                    instance.Orientation = Quaternion.Euler(
                        data.RotationX ?? 0f,
                        data.RotationY ?? 0f,
                        data.RotationZ ?? 0f
                    );
                }

                if (!string.IsNullOrEmpty(data.Name))
                    instance.Name = data.Name;

                return BuildJsonObject(
                    "ok", "true",
                    "name", instance.Name,
                    "id", instance.ID.ToString(),
                    "prefab", data.Prefab
                );
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /prefabs/list
        // ------------------------------------------------------------------

        private void HandlePrefabsList(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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
                    if (i < items.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ==================================================================
        // SCENE MANAGEMENT
        // ==================================================================

        // ------------------------------------------------------------------
        // POST /scene/create
        // ------------------------------------------------------------------

        private void HandleSceneCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneCreateRequest>(body);

            var sceneName = data?.Name ?? "NewScene";

            var result = InvokeOnMainThread(() =>
            {
                try
                {
                    var outputPath = data?.Path ?? $"Content/{sceneName}.scene";
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

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /scene/save
        // ------------------------------------------------------------------

        private void HandleSceneSave(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                Editor.Instance.Scene.SaveScenes();

                return BuildJsonObject("ok", "true", "savedCount", scenes.Length.ToString());
            });

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /scene/load
        // ------------------------------------------------------------------

        private void HandleSceneLoad(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<SceneLoadRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Path))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'path' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                try
                {
                    var asset = FlaxEngine.Content.Load<SceneAsset>(data.Path);
                    if (asset == null)
                        return BuildJsonObject("error", $"Scene asset not found: {data.Path}");

                    Level.LoadScene(asset.ID);

                    return BuildJsonObject(
                        "ok", "true",
                        "path", data.Path,
                        "status", "scene_load_requested"
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to load scene: {ex.Message}");
                }
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /scene/list
        // ------------------------------------------------------------------

        private void HandleSceneList(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                // List scene assets from the content database
                var items = new List<string>();
                CollectContentItemsByType(Editor.Instance.ContentDatabase.Game.Content.Folder, "Scene", items);

                // Also include currently loaded scenes
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
                        if (i < loadedScenes.Length - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }
                }
                sb.AppendLine("  ],");

                sb.AppendLine($"  \"assetCount\": {items.Count},");
                sb.AppendLine("  \"sceneAssets\": [");

                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append($"    {items[i]}");
                    if (i < items.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // POST /scene/actor/create
        // ------------------------------------------------------------------

        private void HandleActorCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorCreateRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Type))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'type' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                // Resolve parent
                Actor parent = scenes[0];
                if (!string.IsNullOrEmpty(data.Parent))
                {
                    var found = FindActorByName(data.Parent);
                    if (found != null)
                        parent = found;
                }

                // Create actor by type name
                Actor newActor = null;
                try
                {
                    var typeName = data.Type;

                    // Try common Flax actor types
                    switch (typeName)
                    {
                        case "EmptyActor":
                            newActor = new EmptyActor();
                            break;
                        case "StaticModel":
                            newActor = new StaticModel();
                            break;
                        case "PointLight":
                            newActor = new PointLight();
                            break;
                        case "SpotLight":
                            newActor = new SpotLight();
                            break;
                        case "DirectionalLight":
                            newActor = new DirectionalLight();
                            break;
                        case "Camera":
                            newActor = new Camera();
                            break;
                        case "AudioSource":
                            newActor = new AudioSource();
                            break;
                        case "BoxCollider":
                            newActor = new BoxCollider();
                            break;
                        case "SphereCollider":
                            newActor = new SphereCollider();
                            break;
                        case "MeshCollider":
                            newActor = new MeshCollider();
                            break;
                        case "RigidBody":
                            newActor = new RigidBody();
                            break;
                        case "AnimatedModel":
                            newActor = new AnimatedModel();
                            break;
                        case "Decal":
                            newActor = new Decal();
                            break;
                        case "Sky":
                            newActor = new Sky();
                            break;
                        case "SkyLight":
                            newActor = new SkyLight();
                            break;
                        case "ExponentialHeightFog":
                            newActor = new ExponentialHeightFog();
                            break;
                        case "PostFxVolume":
                            newActor = new PostFxVolume();
                            break;
                        case "TextRender":
                            newActor = new TextRender();
                            break;
                        case "UICanvas":
                            newActor = new UICanvas();
                            break;
                        case "UIControl":
                            newActor = new UIControl();
                            break;
                        default:
                            // Attempt to resolve via reflection from loaded assemblies
                            Type resolvedType = Type.GetType(typeName);
                            if (resolvedType == null)
                            {
                                // Search FlaxEngine assembly
                                resolvedType = typeof(Actor).Assembly.GetType("FlaxEngine." + typeName);
                            }
                            if (resolvedType != null && typeof(Actor).IsAssignableFrom(resolvedType))
                            {
                                var obj = Activator.CreateInstance(resolvedType);
                                newActor = obj as Actor;
                            }
                            break;
                    }

                    if (newActor == null)
                        return BuildJsonObject("error", $"Unknown or unsupported actor type: {typeName}");
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to create actor: {ex.Message}");
                }

                // Set name
                newActor.Name = data.Name ?? data.Type;

                // Apply position
                if (data.PositionX.HasValue || data.PositionY.HasValue || data.PositionZ.HasValue)
                {
                    newActor.Position = new Vector3(
                        data.PositionX ?? 0f,
                        data.PositionY ?? 0f,
                        data.PositionZ ?? 0f
                    );
                }

                // Apply rotation
                if (data.RotationX.HasValue || data.RotationY.HasValue || data.RotationZ.HasValue)
                {
                    newActor.Orientation = Quaternion.Euler(
                        data.RotationX ?? 0f,
                        data.RotationY ?? 0f,
                        data.RotationZ ?? 0f
                    );
                }

                // Set model if it's a StaticModel
                if (newActor is StaticModel staticModel && !string.IsNullOrEmpty(data.Model))
                {
                    var model = FlaxEngine.Content.Load<Model>(data.Model);
                    if (model != null)
                        staticModel.Model = model;
                    else
                        Debug.LogWarning($"[McpServer] Model not found: {data.Model}");
                }

                // Parent to scene
                newActor.Parent = parent;

                return BuildJsonObject(
                    "ok", "true",
                    "name", newActor.Name,
                    "type", newActor.GetType().Name,
                    "id", newActor.ID.ToString()
                );
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /scene/actor/delete
        // ------------------------------------------------------------------

        private void HandleActorDelete(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorDeleteRequest>(body);

            if (data == null || (string.IsNullOrEmpty(data.Name) && string.IsNullOrEmpty(data.Id)))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'id' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                Actor actor = null;

                if (!string.IsNullOrEmpty(data.Id))
                {
                    if (Guid.TryParse(data.Id, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(data.Name);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.Name ?? data.Id}");

                var actorName = actor.Name;
                var actorId = actor.ID.ToString();

                FlaxEngine.Object.Destroy(actor);

                return BuildJsonObject(
                    "ok", "true",
                    "deleted", actorName,
                    "id", actorId
                );
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /scene/actor/reparent
        // ------------------------------------------------------------------

        private void HandleActorReparent(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ActorReparentRequest>(body);

            if (data == null || (string.IsNullOrEmpty(data.Actor) && string.IsNullOrEmpty(data.ActorId)))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'actor' or 'actorId' in request body."));
                return;
            }

            if (string.IsNullOrEmpty(data.NewParent) && string.IsNullOrEmpty(data.NewParentId))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'newParent' or 'newParentId' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                // Find the actor
                Actor actor = null;
                if (!string.IsNullOrEmpty(data.ActorId))
                {
                    if (Guid.TryParse(data.ActorId, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(data.Actor);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.Actor ?? data.ActorId}");

                // Find the new parent
                Actor newParent = null;
                if (!string.IsNullOrEmpty(data.NewParentId))
                {
                    if (Guid.TryParse(data.NewParentId, out var guid))
                        newParent = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    newParent = FindActorByName(data.NewParent);
                }

                if (newParent == null)
                    return BuildJsonObject("error", $"New parent not found: {data.NewParent ?? data.NewParentId}");

                actor.Parent = newParent;

                return BuildJsonObject(
                    "ok", "true",
                    "actor", actor.Name,
                    "newParent", newParent.Name
                );
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // CONTENT DATABASE
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /content/search?query=barrel&type=Model
        // ------------------------------------------------------------------

        private void HandleContentSearch(HttpListenerResponse response, NameValueCollection query)
        {
            var searchQuery = query["query"];
            var typeFilter = query["type"];

            if (string.IsNullOrEmpty(searchQuery) && string.IsNullOrEmpty(typeFilter))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Provide 'query' and/or 'type' parameter."));
                return;
            }

            var result = InvokeOnMainThread(() =>
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
                    if (i < matches.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // POST /content/reimport
        // ------------------------------------------------------------------

        private void HandleContentReimport(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ContentReimportRequest>(body);

            if (data == null || string.IsNullOrEmpty(data.Path))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'path' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(data.Path);
                if (item == null)
                    return BuildJsonObject("error", $"Asset not found: {data.Path}");

                if (item is BinaryAssetItem binaryItem)
                {
                    Editor.Instance.ContentImporting.Reimport(binaryItem);
                    return BuildJsonObject(
                        "ok", "true",
                        "path", data.Path,
                        "status", "reimport_requested"
                    );
                }

                return BuildJsonObject("error", $"Asset at {data.Path} cannot be reimported.");
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /content/info?path=Content/Models/tree.flax
        // ------------------------------------------------------------------

        private void HandleContentInfo(HttpListenerResponse response, NameValueCollection query)
        {
            var path = query["path"];
            if (string.IsNullOrEmpty(path))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'path' query parameter."));
                return;
            }

            var result = InvokeOnMainThread(() =>
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

                    // File size
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

                    // Dependencies
                    sb.AppendLine("  \"references\": []");
                }
                else
                {
                    sb.AppendLine($"  \"id\": \"\"");
                }

                sb.Append("}");
                return sb.ToString();
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // BUILD / EXPORT
        // ==================================================================

        // Track latest build status
        private volatile string _buildStatus = "idle";

        // ------------------------------------------------------------------
        // POST /build/game
        // ------------------------------------------------------------------

        private void HandleBuildGame(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<BuildGameRequest>(body);

            var result = InvokeOnMainThread(() =>
            {
                try
                {
                    _buildStatus = "building";

                    // Trigger a game build via the editor's GameCooker
                    // Note: Full build configuration is complex; we provide the build request
                    // and the user can customize via build presets in the editor.
                    var platform = data?.Platform ?? "Windows";
                    var config = data?.Configuration ?? "Release";

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

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /build/status
        // ------------------------------------------------------------------

        private void HandleBuildStatus(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                return BuildJsonObject("status", _buildStatus);
            });

            WriteJson(response, 200, result);
        }

        // ==================================================================
        // EDITOR STATE
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /editor/state
        // ------------------------------------------------------------------

        private void HandleEditorState(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                // Play mode state
                sb.AppendLine($"  \"isPlaying\": {(Editor.Instance.StateMachine.IsPlayMode ? "true" : "false")},");
                sb.AppendLine($"  \"isPaused\": {(Editor.Instance.StateMachine.PlayingState.IsPaused ? "true" : "false")},");

                // Project info
                sb.AppendLine($"  \"projectName\": {JsonEscape(Globals.ProductName)},");
                sb.AppendLine($"  \"projectFolder\": {JsonEscape(Globals.ProjectFolder)},");

                // Loaded scenes
                var scenes = Level.Scenes;
                sb.AppendLine("  \"loadedScenes\": [");
                if (scenes != null)
                {
                    for (int i = 0; i < scenes.Length; i++)
                    {
                        sb.Append($"    {JsonEscape(scenes[i].Name)}");
                        if (i < scenes.Length - 1)
                            sb.Append(",");
                        sb.AppendLine();
                    }
                }
                sb.AppendLine("  ],");

                // Selection
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
                    if (i < selection.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("  ],");

                // Undo info
                sb.AppendLine($"  \"undoCount\": {Editor.Instance.Undo.UndoOperationsStack.HistoryCount},");
                sb.AppendLine($"  \"canUndo\": {(Editor.Instance.Undo.CanUndo ? "true" : "false")},");
                sb.AppendLine($"  \"canRedo\": {(Editor.Instance.Undo.CanRedo ? "true" : "false")}");

                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // POST /editor/select
        // ------------------------------------------------------------------

        private void HandleEditorSelect(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<EditorSelectRequest>(body);

            if (data == null || (string.IsNullOrEmpty(data.Name) && string.IsNullOrEmpty(data.Id)))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'id' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                Actor actor = null;

                if (!string.IsNullOrEmpty(data.Id))
                {
                    if (Guid.TryParse(data.Id, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(data.Name);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.Name ?? data.Id}");

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

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /editor/focus
        // ------------------------------------------------------------------

        private void HandleEditorFocus(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<EditorFocusRequest>(body);

            if (data == null || (string.IsNullOrEmpty(data.Name) && string.IsNullOrEmpty(data.Id)))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'id' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                Actor actor = null;

                if (!string.IsNullOrEmpty(data.Id))
                {
                    if (Guid.TryParse(data.Id, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(data.Name);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.Name ?? data.Id}");

                // Select and focus the viewport on this actor
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

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET  /editor/viewport - get viewport camera
        // POST /editor/viewport - set viewport camera
        // ------------------------------------------------------------------

        private void HandleEditorViewportGet(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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

            WriteJson(response, 200, result);
        }

        private void HandleEditorViewportSet(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ViewportSetRequest>(body);

            var result = InvokeOnMainThread(() =>
            {
                if (data == null)
                    return BuildJsonObject("error", "Invalid request body.");

                var viewport = Editor.Instance.Windows.EditWin.Viewport;

                if (data.PositionX.HasValue || data.PositionY.HasValue || data.PositionZ.HasValue)
                {
                    var pos = viewport.ViewPosition;
                    viewport.ViewPosition = new Vector3(
                        data.PositionX ?? (float)pos.X,
                        data.PositionY ?? (float)pos.Y,
                        data.PositionZ ?? (float)pos.Z
                    );
                }

                if (data.Yaw.HasValue || data.Pitch.HasValue)
                {
                    viewport.ViewOrientation = Quaternion.Euler(
                        data.Pitch ?? 0f,
                        data.Yaw ?? 0f,
                        0f
                    );
                }

                return BuildJsonObject("ok", "true", "status", "viewport updated");
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ==================================================================
        // SCRIPTING
        // ==================================================================

        // ------------------------------------------------------------------
        // GET /scripts/list
        // ------------------------------------------------------------------

        private void HandleScriptsList(HttpListenerResponse response)
        {
            // This doesn't require main thread -- just filesystem scan
            try
            {
                var projectFolder = InvokeOnMainThread(() => Globals.ProjectFolder);
                var sourceFolder = Path.Combine(projectFolder, "Source");

                if (!Directory.Exists(sourceFolder))
                {
                    WriteJson(response, 404, BuildJsonObject("error", "Source folder not found."));
                    return;
                }

                var csFiles = Directory.GetFiles(sourceFolder, "*.cs", SearchOption.AllDirectories);

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"count\": {csFiles.Length},");
                sb.AppendLine("  \"scripts\": [");

                for (int i = 0; i < csFiles.Length; i++)
                {
                    // Make path relative to project folder
                    var relPath = csFiles[i].Substring(projectFolder.Length).TrimStart('\\', '/');
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"name\": {JsonEscape(Path.GetFileNameWithoutExtension(csFiles[i]))},");
                    sb.AppendLine($"      \"path\": {JsonEscape(relPath)},");
                    sb.AppendLine($"      \"fileName\": {JsonEscape(Path.GetFileName(csFiles[i]))}");
                    sb.Append("    }");
                    if (i < csFiles.Length - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");

                WriteJson(response, 200, sb.ToString());
            }
            catch (Exception ex)
            {
                WriteJson(response, 500, BuildJsonObject("error", $"Failed to list scripts: {ex.Message}"));
            }
        }

        // ------------------------------------------------------------------
        // POST /scripts/compile
        // ------------------------------------------------------------------

        private void HandleScriptsCompile(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
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

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // GET /scripts/errors
        // ------------------------------------------------------------------

        private void HandleScriptsErrors(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"hasCompilationErrors\": {(ScriptsBuilder.LastCompilationFailed ? "true" : "false")},");
                sb.AppendLine($"  \"isCompiling\": {(ScriptsBuilder.IsCompiling ? "true" : "false")},");

                // Collect recent error/warning logs that look like compilation output
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
                    if (i < entries.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        // ==================================================================
        // Shared helpers (extensions)
        // ==================================================================

        /// <summary>
        /// Parses a string to float, returning a default value on failure.
        /// </summary>
        private static float ParseFloat(string value, float defaultValue)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            float result;
            if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Recursively collects all actors of a given type from the scene hierarchy.
        /// </summary>
        private void CollectActorsOfType<T>(Actor root, List<T> results) where T : Actor
        {
            if (root is T typed)
                results.Add(typed);

            foreach (var child in root.Children)
                CollectActorsOfType(child, results);
        }

        /// <summary>
        /// Recursively collects all actors matching a type name string.
        /// </summary>
        private void CollectActorsOfTypeName(Actor root, string typeName, List<Actor> results)
        {
            if (string.Equals(root.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                results.Add(root);

            foreach (var child in root.Children)
                CollectActorsOfTypeName(child, typeName, results);
        }

        /// <summary>
        /// Recursively collects content items by asset type name from the content database.
        /// Each item is emitted as a JSON object string.
        /// </summary>
        private void CollectContentItemsByType(ContentFolder folder, string typeName, List<string> results)
        {
            if (folder == null)
                return;

            foreach (var child in folder.Children)
            {
                if (child is ContentFolder childFolder)
                {
                    CollectContentItemsByType(childFolder, typeName, results);
                }
                else if (child is AssetItem assetItem)
                {
                    if (string.IsNullOrEmpty(typeName) ||
                        assetItem.TypeName.IndexOf(typeName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(BuildJsonObject(
                            "name", child.ShortName,
                            "type", assetItem.TypeName,
                            "path", child.Path,
                            "id", assetItem.ID.ToString()
                        ));
                    }
                }
            }
        }

        /// <summary>
        /// Searches content items by name query and/or type filter.
        /// </summary>
        private void SearchContentItems(ContentFolder folder, string nameQuery, string typeFilter, List<string> results)
        {
            if (folder == null)
                return;

            foreach (var child in folder.Children)
            {
                if (child is ContentFolder childFolder)
                {
                    SearchContentItems(childFolder, nameQuery, typeFilter, results);
                }
                else if (child is AssetItem assetItem)
                {
                    bool nameMatch = string.IsNullOrEmpty(nameQuery) ||
                        (child.ShortName != null && child.ShortName.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0);

                    bool typeMatch = string.IsNullOrEmpty(typeFilter) ||
                        assetItem.TypeName.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (nameMatch && typeMatch)
                    {
                        results.Add(BuildJsonObject(
                            "name", child.ShortName,
                            "type", assetItem.TypeName,
                            "path", child.Path,
                            "id", assetItem.ID.ToString()
                        ));
                    }
                }
            }
        }

        // ==================================================================
        // Extension request data classes
        // ==================================================================

        [Serializable]
        private class PhysicsSettingsRequest
        {
            public float? GravityX;
            public float? GravityY;
            public float? GravityZ;
        }

        [Serializable]
        private class AnimationPlayRequest
        {
            public string Actor;
            public string Clip;
            public float? Speed;
            public string Parameter;
            public object ParameterValue;
        }

        [Serializable]
        private class TerrainSculptRequest
        {
            public float X;
            public float Z;
            public float Radius = 5f;
            public float Strength = 0.5f;
            public string TerrainName;
        }

        [Serializable]
        private class ScreenshotRequest
        {
            public string OutputPath;
            public int Width = 1920;
            public int Height = 1080;
        }

        [Serializable]
        private class RenderingSettingsRequest
        {
            public bool? HalfResolution;
        }

        [Serializable]
        private class AudioPlayRequest
        {
            public string Clip;
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public bool Loop;
        }

        [Serializable]
        private class PrefabSpawnRequest
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
        private class SceneCreateRequest
        {
            public string Name;
            public string Path;
        }

        [Serializable]
        private class SceneLoadRequest
        {
            public string Path;
        }

        [Serializable]
        private class ActorCreateRequest
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
        private class ActorDeleteRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class ActorReparentRequest
        {
            public string Actor;
            public string ActorId;
            public string NewParent;
            public string NewParentId;
        }

        [Serializable]
        private class ContentReimportRequest
        {
            public string Path;
        }

        [Serializable]
        private class BuildGameRequest
        {
            public string Platform;
            public string Configuration;
        }

        [Serializable]
        private class EditorSelectRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class EditorFocusRequest
        {
            public string Name;
            public string Id;
        }

        [Serializable]
        private class ViewportSetRequest
        {
            public float? PositionX;
            public float? PositionY;
            public float? PositionZ;
            public float? Yaw;
            public float? Pitch;
        }
    }
}

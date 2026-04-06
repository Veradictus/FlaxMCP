using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;

namespace FlaxMCP
{
    /// <summary>
    /// REST endpoint routing and request data classes for backward compatibility.
    /// Contains <see cref="HandleExtensionRoute"/> which dispatches legacy REST
    /// HTTP requests to the corresponding MCP tool handlers, plus the
    /// <see cref="QueryToArgs"/> helper and all <c>[Serializable]</c> request DTOs.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // Extension route dispatch (backward compatibility for REST)
        // ==================================================================

        /// <summary>
        /// Routes a legacy REST HTTP request to the appropriate MCP tool handler.
        /// Returns <c>true</c> if the route was handled, <c>false</c> otherwise.
        /// </summary>
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

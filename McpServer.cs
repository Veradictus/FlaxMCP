using System;
using System.Collections.Generic;
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
    /// Lightweight HTTP server that exposes Flax Editor functionality over a
    /// REST-like API on localhost. Designed for MCP-compatible external tools
    /// (such as Claude Code) to inspect scenes, modify actors, import assets,
    /// and control the editor programmatically.
    ///
    /// The server runs on a background thread using <see cref="HttpListener"/>
    /// and marshals all Flax API calls back to the main thread via
    /// <see cref="Scripting.InvokeOnUpdate"/>.
    ///
    /// Start/stop is managed by <see cref="AssetConverterPlugin"/>.
    /// </summary>
    public partial class McpServer : IDisposable
    {
        private const string Prefix = "http://localhost:9100/";
        private const int MaxLogEntries = 500;

        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        // Circular buffer for captured log entries
        private readonly List<LogEntry> _logBuffer = new List<LogEntry>();
        private readonly object _logLock = new object();

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts the HTTP listener on a background thread.
        /// </summary>
        public void Start()
        {
            if (_running)
                return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(Prefix);
                _listener.Start();
                _running = true;

                // Subscribe to engine log output
                Debug.Logger.LogHandler.SendLog += OnLogMessage;
                Debug.Logger.LogHandler.SendExceptionLog += OnExceptionLog;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "McpServer"
                };
                _listenerThread.Start();

                Debug.Log($"[McpServer] Started on {Prefix}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[McpServer] Failed to start: {ex.Message}");
                _running = false;
            }
        }

        /// <summary>
        /// Stops the HTTP listener and releases resources.
        /// </summary>
        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            try
            {
                Debug.Logger.LogHandler.SendLog -= OnLogMessage;
                Debug.Logger.LogHandler.SendExceptionLog -= OnExceptionLog;
            }
            catch
            {
                // Ignore if already unsubscribed
            }

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // Ignore listener shutdown errors
            }

            _listenerThread?.Join(2000);
            Debug.Log("[McpServer] Stopped.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
        }

        // ------------------------------------------------------------------
        // Listener loop
        // ------------------------------------------------------------------

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Expected when listener is stopped
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogWarning($"[McpServer] Listener error: {ex.Message}");
                }
            }
        }

        // ------------------------------------------------------------------
        // Request dispatch
        // ------------------------------------------------------------------

        private void HandleRequest(HttpListenerContext context)
        {
            var response = context.Response;

            try
            {
                // CORS headers for browser-based tools
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (context.Request.HttpMethod == "OPTIONS")
                {
                    WriteJson(response, 200, "{\"ok\":true}");
                    return;
                }

                var method = context.Request.HttpMethod;
                var path = context.Request.Url.AbsolutePath.TrimEnd('/');
                var query = context.Request.QueryString;

                // Route to handler
                switch (path)
                {
                    case "/health":
                        HandleHealth(response);
                        break;

                    case "/assets/list":
                        HandleAssetsList(response, query);
                        break;

                    case "/assets/import":
                        RequirePost(method, response, () => HandleAssetsImport(context, response));
                        break;

                    case "/materials/create":
                        RequirePost(method, response, () => HandleMaterialCreate(context, response));
                        break;

                    case "/materials/create-instance":
                        RequirePost(method, response, () => HandleMaterialInstanceCreate(context, response));
                        break;

                    case "/scene/hierarchy":
                        HandleSceneHierarchy(response);
                        break;

                    case "/scene/actor":
                        HandleSceneActor(response, query);
                        break;

                    case "/scene/actor/property":
                        RequirePost(method, response, () => HandleSetActorProperty(context, response));
                        break;

                    case "/scene/find":
                        HandleFindActors(response, query);
                        break;

                    case "/editor/logs":
                        HandleEditorLogs(response, query);
                        break;

                    case "/editor/play":
                        RequirePost(method, response, () => HandleEditorPlay(response));
                        break;

                    case "/editor/stop":
                        RequirePost(method, response, () => HandleEditorStop(response));
                        break;

                    case "/content/find":
                        HandleContentFind(response, query);
                        break;

                    case "/scripts/read":
                        HandleScriptRead(response, query);
                        break;

                    default:
                        if (!HandleExtensionRoute(path, method, context, response, query))
                        {
                            WriteJson(response, 404, BuildJsonObject(
                                "error", "Not found",
                                "path", path
                            ));
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(response, 500, BuildJsonObject(
                        "error", ex.Message,
                        "type", ex.GetType().Name
                    ));
                }
                catch
                {
                    // Last resort -- don't let a response error crash the thread
                }
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch
                {
                    // Ignore
                }
            }
        }

        private void RequirePost(string method, HttpListenerResponse response, Action handler)
        {
            if (method != "POST")
            {
                WriteJson(response, 405, BuildJsonObject("error", "Method not allowed. Use POST."));
                return;
            }

            handler();
        }

        // ------------------------------------------------------------------
        // GET /health
        // ------------------------------------------------------------------

        private void HandleHealth(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                return BuildJsonObject(
                    "status", "ok",
                    "engine", "FlaxEngine",
                    "version", Globals.EngineBuildNumber.ToString(),
                    "project", Globals.ProductName,
                    "isPlaying", Editor.Instance.StateMachine.IsPlayMode.ToString().ToLowerInvariant()
                );
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // GET /assets/list?path=Content/Imports&type=fbx
        // ------------------------------------------------------------------

        private void HandleAssetsList(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var path = query["path"] ?? "Content";
            var typeFilter = query["type"];

            var result = InvokeOnMainThread(() =>
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

                // List child folders
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
                    if (i < items.Count - 1)
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
        // POST /assets/import
        // ------------------------------------------------------------------

        private void HandleAssetsImport(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ImportRequest>(body);

            if (data?.Files == null || data.Files.Length == 0)
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'files' array in request body."));
                return;
            }

            var target = data.Target ?? "Content";
            var skipDialog = data.SkipDialog;

            var result = InvokeOnMainThread(() =>
            {
                var folder = Editor.Instance.ContentDatabase.Find(target) as ContentFolder;
                if (folder == null)
                    return BuildJsonObject("error", $"Target folder not found: {target}");

                int queued = 0;
                foreach (var file in data.Files)
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

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // POST /materials/create
        // ------------------------------------------------------------------

        private void HandleMaterialCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<MaterialCreateRequest>(body);

            if (string.IsNullOrEmpty(data?.Name) || string.IsNullOrEmpty(data?.OutputPath))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'outputPath' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var absPath = Path.Combine(Globals.ProjectFolder, data.OutputPath);
                var dir = Path.GetDirectoryName(absPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                if (FlaxEditor.Editor.CreateAsset("Material", absPath))
                    return BuildJsonObject("error", $"Failed to create material at: {data.OutputPath}");

                return BuildJsonObject(
                    "created", "true",
                    "name", data.Name,
                    "path", data.OutputPath
                );
            });

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // POST /materials/create-instance
        // ------------------------------------------------------------------

        private void HandleMaterialInstanceCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<MaterialInstanceCreateRequest>(body);

            if (string.IsNullOrEmpty(data?.Name) || string.IsNullOrEmpty(data?.OutputPath))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'outputPath' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                var absPath = Path.Combine(Globals.ProjectFolder, data.OutputPath);
                var dir = Path.GetDirectoryName(absPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                if (FlaxEditor.Editor.CreateAsset("MaterialInstance", absPath))
                    return BuildJsonObject("error", $"Failed to create material instance at: {data.OutputPath}");

                var instance = FlaxEngine.Content.Load<MaterialInstance>(data.OutputPath);
                if (instance == null)
                    return BuildJsonObject("error", $"Failed to load created material instance: {data.OutputPath}");

                // Set base material if specified
                if (!string.IsNullOrEmpty(data.BaseMaterial))
                {
                    var baseMat = FlaxEngine.Content.Load<MaterialBase>(data.BaseMaterial);
                    if (baseMat != null)
                        instance.BaseMaterial = baseMat;
                    else
                        Debug.LogWarning($"[McpServer] Base material not found: {data.BaseMaterial}");
                }

                // Set parameters if specified
                if (data.Parameters != null)
                {
                    foreach (var kvp in data.Parameters)
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
                    "name", data.Name,
                    "path", data.OutputPath
                );
            });

            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        private void SetMaterialParameter(MaterialInstance instance, string name, object value)
        {
            if (value is double d)
            {
                instance.SetParameterValue(name, (float)d);
            }
            else if (value is long l)
            {
                instance.SetParameterValue(name, (float)l);
            }
            else if (value is string s)
            {
                // Assume string values are asset paths (textures)
                var texture = FlaxEngine.Content.Load<Texture>(s);
                if (texture != null)
                    instance.SetParameterValue(name, texture);
                else
                    Debug.LogWarning($"[McpServer] Texture not found for parameter '{name}': {s}");
            }
            else if (value != null)
            {
                instance.SetParameterValue(name, value);
            }
        }

        // ------------------------------------------------------------------
        // GET /scene/hierarchy
        // ------------------------------------------------------------------

        private void HandleSceneHierarchy(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                var scenes = Level.Scenes;
                if (scenes == null || scenes.Length == 0)
                    return BuildJsonObject("error", "No scenes loaded.");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"scenes\": [");

                for (int s = 0; s < scenes.Length; s++)
                {
                    var scene = scenes[s];
                    sb.Append("    ");
                    BuildActorJson(sb, scene, 2);
                    if (s < scenes.Length - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }

                sb.AppendLine("  ]");
                sb.Append("}");
                return sb.ToString();
            });

            WriteJson(response, 200, result);
        }

        private void BuildActorJson(StringBuilder sb, Actor actor, int indent)
        {
            var pad = new string(' ', indent * 2);
            var innerPad = new string(' ', (indent + 1) * 2);

            sb.AppendLine("{");
            sb.AppendLine($"{innerPad}\"name\": {JsonEscape(actor.Name)},");
            sb.AppendLine($"{innerPad}\"type\": {JsonEscape(actor.GetType().Name)},");
            sb.AppendLine($"{innerPad}\"id\": {JsonEscape(actor.ID.ToString())},");
            sb.AppendLine($"{innerPad}\"isActive\": {(actor.IsActive ? "true" : "false")},");

            // Position
            var pos = actor.Position;
            sb.AppendLine($"{innerPad}\"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }},");

            // Children
            sb.AppendLine($"{innerPad}\"children\": [");
            var children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                sb.Append($"{innerPad}  ");
                BuildActorJson(sb, children[i], indent + 2);
                if (i < children.Length - 1)
                    sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine($"{innerPad}]");
            sb.Append($"{pad}}}");
        }

        // ------------------------------------------------------------------
        // GET /scene/actor?name=MyActor or ?id=guid
        // ------------------------------------------------------------------

        private void HandleSceneActor(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var name = query["name"];
            var id = query["id"];

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(id))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Provide 'name' or 'id' query parameter."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                Actor actor = null;

                if (!string.IsNullOrEmpty(id))
                {
                    if (Guid.TryParse(id, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(name);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {name ?? id}");

                return BuildActorDetailJson(actor);
            });

            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        private string BuildActorDetailJson(Actor actor)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"name\": {JsonEscape(actor.Name)},");
            sb.AppendLine($"  \"type\": {JsonEscape(actor.GetType().Name)},");
            sb.AppendLine($"  \"id\": {JsonEscape(actor.ID.ToString())},");
            sb.AppendLine($"  \"isActive\": {(actor.IsActive ? "true" : "false")},");

            // Full transform
            var t = actor.Transform;
            sb.AppendLine("  \"transform\": {");
            sb.AppendLine($"    \"position\": {{ \"X\": {t.Translation.X}, \"Y\": {t.Translation.Y}, \"Z\": {t.Translation.Z} }},");
            sb.AppendLine($"    \"rotation\": {{ \"X\": {t.Orientation.X}, \"Y\": {t.Orientation.Y}, \"Z\": {t.Orientation.Z}, \"W\": {t.Orientation.W} }},");
            sb.AppendLine($"    \"scale\": {{ \"X\": {t.Scale.X}, \"Y\": {t.Scale.Y}, \"Z\": {t.Scale.Z} }}");
            sb.AppendLine("  },");

            // Tags
            sb.Append("  \"tags\": [");
            if (actor.Tags != null && actor.Tags.Length > 0)
            {
                for (int t = 0; t < actor.Tags.Length; t++)
                {
                    sb.Append(JsonEscape(actor.Tags[t].ToString()));
                    if (t < actor.Tags.Length - 1)
                        sb.Append(", ");
                }
            }
            sb.AppendLine("],");

            // Scripts / components
            sb.AppendLine("  \"scripts\": [");
            var scripts = actor.Scripts;
            for (int i = 0; i < scripts.Length; i++)
            {
                var script = scripts[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"type\": {JsonEscape(script.GetType().FullName)},");
                sb.AppendLine($"      \"enabled\": {(script.Enabled ? "true" : "false")},");
                sb.AppendLine($"      \"id\": {JsonEscape(script.ID.ToString())}");
                sb.Append("    }");
                if (i < scripts.Length - 1)
                    sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            // Children summary
            sb.AppendLine("  \"children\": [");
            var children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": {JsonEscape(children[i].Name)},");
                sb.AppendLine($"      \"type\": {JsonEscape(children[i].GetType().Name)},");
                sb.AppendLine($"      \"id\": {JsonEscape(children[i].ID.ToString())}");
                sb.Append("    }");
                if (i < children.Length - 1)
                    sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendLine("  ]");

            sb.Append("}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // POST /scene/actor/property
        // ------------------------------------------------------------------

        private void HandleSetActorProperty(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<SetPropertyRequest>(body);

            if (data == null || (string.IsNullOrEmpty(data.ActorName) && string.IsNullOrEmpty(data.ActorId)))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'actorName' or 'actorId' in request body."));
                return;
            }

            if (string.IsNullOrEmpty(data.Property))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'property' in request body."));
                return;
            }

            var result = InvokeOnMainThread(() =>
            {
                Actor actor = null;

                if (!string.IsNullOrEmpty(data.ActorId))
                {
                    if (Guid.TryParse(data.ActorId, out var guid))
                        actor = FlaxEngine.Object.Find<Actor>(ref guid);
                }
                else
                {
                    actor = FindActorByName(data.ActorName);
                }

                if (actor == null)
                    return BuildJsonObject("error", $"Actor not found: {data.ActorName ?? data.ActorId}");

                try
                {
                    SetPropertyByPath(actor, data.Property, data.Value);
                    return BuildJsonObject(
                        "ok", "true",
                        "actor", actor.Name,
                        "property", data.Property
                    );
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to set property: {ex.Message}");
                }
            });

            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        private void SetPropertyByPath(Actor actor, string propertyPath, object value)
        {
            // Handle common transform shortcuts
            if (propertyPath.StartsWith("Transform."))
            {
                var sub = propertyPath.Substring("Transform.".Length);
                var transform = actor.Transform;

                switch (sub)
                {
                    case "Position":
                    case "Translation":
                        transform.Translation = ParseVector3(value);
                        actor.Transform = transform;
                        return;

                    case "Rotation":
                    case "Orientation":
                        transform.Orientation = ParseQuaternion(value);
                        actor.Transform = transform;
                        return;

                    case "Scale":
                        transform.Scale = ParseFloat3(value);
                        actor.Transform = transform;
                        return;
                }
            }

            // Generic reflection-based property setter
            var parts = propertyPath.Split('.');
            object current = actor;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var prop = current.GetType().GetProperty(parts[i],
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    throw new Exception($"Property '{parts[i]}' not found on {current.GetType().Name}");
                current = prop.GetValue(current);
            }

            var finalProp = current.GetType().GetProperty(parts[parts.Length - 1],
                BindingFlags.Public | BindingFlags.Instance);
            if (finalProp == null)
                throw new Exception($"Property '{parts[parts.Length - 1]}' not found on {current.GetType().Name}");

            var converted = ConvertValue(value, finalProp.PropertyType);
            finalProp.SetValue(current, converted);
        }

        private object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            if (targetType == typeof(float))
                return Convert.ToSingle(value);
            if (targetType == typeof(double))
                return Convert.ToDouble(value);
            if (targetType == typeof(int))
                return Convert.ToInt32(value);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(value);
            if (targetType == typeof(string))
                return value.ToString();
            if (targetType == typeof(Vector3) || targetType == typeof(Float3))
                return ParseVector3(value);
            if (targetType == typeof(Quaternion))
                return ParseQuaternion(value);

            return value;
        }

        private Vector3 ParseVector3(object value)
        {
            if (value is Dictionary<string, object> dict)
            {
                return new Vector3(
                    dict.ContainsKey("X") ? Convert.ToSingle(dict["X"]) : 0f,
                    dict.ContainsKey("Y") ? Convert.ToSingle(dict["Y"]) : 0f,
                    dict.ContainsKey("Z") ? Convert.ToSingle(dict["Z"]) : 0f
                );
            }

            return Vector3.Zero;
        }

        private Float3 ParseFloat3(object value)
        {
            if (value is Dictionary<string, object> dict)
            {
                return new Float3(
                    dict.ContainsKey("X") ? Convert.ToSingle(dict["X"]) : 0f,
                    dict.ContainsKey("Y") ? Convert.ToSingle(dict["Y"]) : 0f,
                    dict.ContainsKey("Z") ? Convert.ToSingle(dict["Z"]) : 0f
                );
            }

            return Float3.One;
        }

        private Quaternion ParseQuaternion(object value)
        {
            if (value is Dictionary<string, object> dict)
            {
                return new Quaternion(
                    dict.ContainsKey("X") ? Convert.ToSingle(dict["X"]) : 0f,
                    dict.ContainsKey("Y") ? Convert.ToSingle(dict["Y"]) : 0f,
                    dict.ContainsKey("Z") ? Convert.ToSingle(dict["Z"]) : 0f,
                    dict.ContainsKey("W") ? Convert.ToSingle(dict["W"]) : 1f
                );
            }

            return Quaternion.Identity;
        }

        // ------------------------------------------------------------------
        // GET /scene/find?name=partial&type=StaticModel&tag=MyTag
        // ------------------------------------------------------------------

        private void HandleFindActors(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var nameQuery = query["name"];
            var typeQuery = query["type"];
            var tagQuery = query["tag"];

            var result = InvokeOnMainThread(() =>
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

        private void CollectMatchingActors(Actor actor, string nameQuery, string typeQuery, string tagQuery, List<Actor> results)
        {
            bool matches = true;

            if (!string.IsNullOrEmpty(nameQuery))
                matches &= actor.Name != null && actor.Name.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0;

            if (!string.IsNullOrEmpty(typeQuery))
                matches &= string.Equals(actor.GetType().Name, typeQuery, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(tagQuery))
            {
                bool hasMatchingTag = false;
                if (actor.Tags != null)
                {
                    foreach (var tag in actor.Tags)
                    {
                        if (tag.ToString().IndexOf(tagQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasMatchingTag = true;
                            break;
                        }
                    }
                }
                matches &= hasMatchingTag;
            }

            if (matches)
                results.Add(actor);

            foreach (var child in actor.Children)
                CollectMatchingActors(child, nameQuery, typeQuery, tagQuery, results);
        }

        // ------------------------------------------------------------------
        // GET /editor/logs?count=50
        // ------------------------------------------------------------------

        private void HandleEditorLogs(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            int count = 50;
            if (query["count"] != null)
                int.TryParse(query["count"], out count);

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
                if (i < entries.Count - 1)
                    sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.Append("}");

            WriteJson(response, 200, sb.ToString());
        }

        // ------------------------------------------------------------------
        // POST /editor/play
        // ------------------------------------------------------------------

        private void HandleEditorPlay(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                if (Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("status", "already_playing");

                Editor.Instance.Simulation.RequestStartPlayScenes();
                return BuildJsonObject("status", "play_requested");
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // POST /editor/stop
        // ------------------------------------------------------------------

        private void HandleEditorStop(HttpListenerResponse response)
        {
            var result = InvokeOnMainThread(() =>
            {
                if (!Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("status", "not_playing");

                Editor.Instance.Simulation.RequestStopPlay();
                return BuildJsonObject("status", "stop_requested");
            });

            WriteJson(response, 200, result);
        }

        // ------------------------------------------------------------------
        // GET /content/find?path=Content/Materials/M_Base.flax
        // ------------------------------------------------------------------

        private void HandleContentFind(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
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
                    sb.AppendLine($"  \"typeName\": {JsonEscape(assetItem.TypeName)}");
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

        // ------------------------------------------------------------------
        // GET /scripts/read?path=Source/Game/MyScript.cs
        // ------------------------------------------------------------------

        private void HandleScriptRead(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var path = query["path"];
            if (string.IsNullOrEmpty(path))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'path' query parameter."));
                return;
            }

            var absPath = Path.Combine(Globals.ProjectFolder, path);

            if (!File.Exists(absPath))
            {
                WriteJson(response, 404, BuildJsonObject("error", $"File not found: {path}"));
                return;
            }

            // Only allow reading source files for safety
            var ext = Path.GetExtension(absPath).ToLowerInvariant();
            if (ext != ".cs" && ext != ".cpp" && ext != ".h" && ext != ".json" && ext != ".xml" && ext != ".build")
            {
                WriteJson(response, 403, BuildJsonObject("error", $"File type not allowed: {ext}"));
                return;
            }

            try
            {
                var content = File.ReadAllText(absPath);
                WriteJson(response, 200, BuildJsonObject(
                    "path", path,
                    "content", content
                ));
            }
            catch (Exception ex)
            {
                WriteJson(response, 500, BuildJsonObject("error", $"Failed to read file: {ex.Message}"));
            }
        }

        // ------------------------------------------------------------------
        // Log capture
        // ------------------------------------------------------------------

        private void OnLogMessage(LogType level, string msg, FlaxEngine.Object obj, string stackTrace)
        {
            AddLogEntry(level.ToString(), msg);
        }

        private void OnExceptionLog(Exception exception, FlaxEngine.Object obj)
        {
            AddLogEntry("Exception", exception?.ToString() ?? "Unknown exception");
        }

        private void AddLogEntry(string level, string message)
        {
            lock (_logLock)
            {
                _logBuffer.Add(new LogEntry
                {
                    Level = level,
                    Message = message,
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                });

                // Trim to max size
                while (_logBuffer.Count > MaxLogEntries)
                    _logBuffer.RemoveAt(0);
            }
        }

        // ------------------------------------------------------------------
        // Actor lookup helpers
        // ------------------------------------------------------------------

        private Actor FindActorByName(string name)
        {
            var scenes = Level.Scenes;
            if (scenes == null)
                return null;

            foreach (var scene in scenes)
            {
                var found = FindActorByNameRecursive(scene, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private Actor FindActorByNameRecursive(Actor parent, string name)
        {
            if (string.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase))
                return parent;

            foreach (var child in parent.Children)
            {
                var found = FindActorByNameRecursive(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Main thread marshaling
        // ------------------------------------------------------------------

        /// <summary>
        /// Invokes a function on the main thread via <see cref="Scripting.InvokeOnUpdate"/>
        /// and blocks the calling (HTTP worker) thread until the result is available.
        /// Includes a timeout to prevent deadlocks.
        /// </summary>
        private T InvokeOnMainThread<T>(Func<T> func, int timeoutMs = 10000)
        {
            T result = default;
            Exception caught = null;
            var done = new ManualResetEventSlim(false);

            Scripting.InvokeOnUpdate(() =>
            {
                try
                {
                    result = func();
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                finally
                {
                    done.Set();
                }
            });

            if (!done.Wait(timeoutMs))
                throw new TimeoutException("Main thread invocation timed out.");

            if (caught != null)
                throw caught;

            return result;
        }

        // ------------------------------------------------------------------
        // HTTP / JSON helpers
        // ------------------------------------------------------------------

        private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";

            var bytes = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadRequestBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Builds a flat JSON object from alternating key-value string pairs.
        /// All values are emitted as JSON strings.
        /// </summary>
        private static string BuildJsonObject(params string[] keyValuePairs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            for (int i = 0; i < keyValuePairs.Length; i += 2)
            {
                var key = keyValuePairs[i];
                var val = (i + 1 < keyValuePairs.Length) ? keyValuePairs[i + 1] : "";

                sb.Append($"  {JsonEscape(key)}: {JsonEscape(val)}");
                if (i + 2 < keyValuePairs.Length)
                    sb.Append(",");
                sb.AppendLine();
            }

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Escapes a string as a JSON string literal with surrounding quotes.
        /// </summary>
        private static string JsonEscape(string value)
        {
            if (value == null)
                return "null";

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // JSON request data classes
        // ------------------------------------------------------------------

        [Serializable]
        private class ImportRequest
        {
            public string[] Files;
            public string Target;
            public bool SkipDialog = true;
        }

        [Serializable]
        private class MaterialCreateRequest
        {
            public string Name;
            public string OutputPath;
        }

        [Serializable]
        private class MaterialInstanceCreateRequest
        {
            public string Name;
            public string BaseMaterial;
            public string OutputPath;
            public Dictionary<string, object> Parameters;
        }

        [Serializable]
        private class SetPropertyRequest
        {
            public string ActorName;
            public string ActorId;
            public string Property;
            public object Value;
        }

        private class LogEntry
        {
            public string Level;
            public string Message;
            public string Timestamp;
        }
    }
}

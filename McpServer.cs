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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlaxMCP
{
    /// <summary>
    /// MCP (Model Context Protocol) server for Flax Editor. Exposes engine
    /// functionality via the MCP Streamable HTTP transport on <c>/mcp</c>
    /// (JSON-RPC 2.0) and retains backward-compatible REST endpoints on the
    /// original paths.
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
        private const string ServerName = "FlaxMCP";
        private const string ServerVersion = "2.0.0";
        private const string ProtocolVersion = "2025-03-26";

        private HttpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;

        // Circular buffer for captured log entries
        private readonly List<LogEntry> _logBuffer = new List<LogEntry>();
        private readonly object _logLock = new object();

        // MCP tool registry
        private readonly Dictionary<string, McpTool> _tools = new Dictionary<string, McpTool>();

        // Build status tracking
        private volatile string _buildStatus = "idle";

        // ------------------------------------------------------------------
        // MCP tool definition
        // ------------------------------------------------------------------

        private class McpTool
        {
            public string Name;
            public string Description;
            public string InputSchemaJson;
            public Func<Dictionary<string, object>, string> Handler;
        }

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
                RegisterAllTools();

                _listener = new HttpListener();
                _listener.Prefixes.Add(Prefix);
                _listener.Start();
                _running = true;

                Debug.Logger.LogHandler.SendLog += OnLogMessage;
                Debug.Logger.LogHandler.SendExceptionLog += OnExceptionLog;

                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "McpServer"
                };
                _listenerThread.Start();

                Debug.Log($"[McpServer] Started on {Prefix} ({_tools.Count} tools registered)");
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
                // CORS headers for browser-based MCP clients
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

                // MCP Streamable HTTP transport endpoint
                if (path == "/mcp")
                {
                    HandleMcpRequest(context, response);
                    return;
                }

                // Legacy REST dispatch for backward compatibility
                HandleLegacyRestRequest(method, path, context, response, query);
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

        // ------------------------------------------------------------------
        // MCP Protocol handler (JSON-RPC 2.0 over HTTP)
        // ------------------------------------------------------------------

        private void HandleMcpRequest(HttpListenerContext context, HttpListenerResponse response)
        {
            if (context.Request.HttpMethod != "POST")
            {
                WriteJson(response, 405, BuildJsonRpcError(null, -32600, "MCP endpoint requires POST."));
                return;
            }

            var body = ReadRequestBody(context.Request);

            JObject request;
            try
            {
                request = JObject.Parse(body);
            }
            catch (Exception)
            {
                WriteJson(response, 400, BuildJsonRpcError(null, -32700, "Parse error: invalid JSON."));
                return;
            }

            var jsonrpc = request["jsonrpc"]?.ToString();
            if (jsonrpc != "2.0")
            {
                WriteJson(response, 400, BuildJsonRpcError(null, -32600, "Invalid request: jsonrpc must be \"2.0\"."));
                return;
            }

            var rpcMethod = request["method"]?.ToString();
            var id = request["id"];
            var rpcParams = request["params"] as JObject;

            // Notifications have no id and require no response
            if (id == null)
            {
                // Handle known notifications silently
                if (rpcMethod == "notifications/initialized" ||
                    rpcMethod == "notifications/cancelled")
                {
                    WriteJson(response, 204, "");
                    return;
                }

                // Unknown notification -- still no response
                WriteJson(response, 204, "");
                return;
            }

            // Route JSON-RPC methods
            string result;
            switch (rpcMethod)
            {
                case "initialize":
                    result = HandleMcpInitialize(id);
                    break;

                case "tools/list":
                    result = HandleMcpToolsList(id);
                    break;

                case "tools/call":
                    result = HandleMcpToolsCall(id, rpcParams);
                    break;

                case "ping":
                    result = BuildJsonRpcResult(id, new JObject());
                    break;

                default:
                    result = BuildJsonRpcError(id, -32601, $"Method not found: {rpcMethod}");
                    break;
            }

            WriteJson(response, 200, result);
        }

        private string HandleMcpInitialize(JToken id)
        {
            var resultObj = new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject
                    {
                        ["listChanged"] = false
                    }
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            };

            return BuildJsonRpcResult(id, resultObj);
        }

        private string HandleMcpToolsList(JToken id)
        {
            var toolsArray = new JArray();

            foreach (var kvp in _tools)
            {
                var tool = kvp.Value;
                var toolObj = new JObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["inputSchema"] = JObject.Parse(tool.InputSchemaJson)
                };
                toolsArray.Add(toolObj);
            }

            var resultObj = new JObject
            {
                ["tools"] = toolsArray
            };

            return BuildJsonRpcResult(id, resultObj);
        }

        private string HandleMcpToolsCall(JToken id, JObject rpcParams)
        {
            if (rpcParams == null)
                return BuildJsonRpcError(id, -32602, "Invalid params: missing params object.");

            var toolName = rpcParams["name"]?.ToString();
            if (string.IsNullOrEmpty(toolName))
                return BuildJsonRpcError(id, -32602, "Invalid params: missing 'name'.");

            if (!_tools.TryGetValue(toolName, out var tool))
                return BuildJsonRpcError(id, -32602, $"Unknown tool: {toolName}");

            // Convert JObject arguments to Dictionary<string, object>
            var args = new Dictionary<string, object>();
            var argsToken = rpcParams["arguments"] as JObject;
            if (argsToken != null)
            {
                foreach (var prop in argsToken.Properties())
                {
                    args[prop.Name] = ConvertJToken(prop.Value);
                }
            }

            try
            {
                var toolResult = tool.Handler(args);

                var contentArray = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = toolResult
                    }
                };

                var resultObj = new JObject
                {
                    ["content"] = contentArray
                };

                return BuildJsonRpcResult(id, resultObj);
            }
            catch (Exception ex)
            {
                return BuildJsonRpcError(id, -1, $"Tool '{toolName}' failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a <see cref="JToken"/> to a plain CLR object for use
        /// by tool handlers.
        /// </summary>
        private object ConvertJToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                        dict[prop.Name] = ConvertJToken(prop.Value);
                    return dict;

                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                        list.Add(ConvertJToken(item));
                    return list;

                case JTokenType.Integer:
                    return token.Value<long>();

                case JTokenType.Float:
                    return token.Value<double>();

                case JTokenType.Boolean:
                    return token.Value<bool>();

                case JTokenType.String:
                    return token.Value<string>();

                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;

                default:
                    return token.ToString();
            }
        }

        // ------------------------------------------------------------------
        // JSON-RPC response builders
        // ------------------------------------------------------------------

        private static string BuildJsonRpcResult(JToken id, JToken result)
        {
            var obj = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result
            };
            return obj.ToString(Formatting.None);
        }

        private static string BuildJsonRpcError(JToken id, int code, string message)
        {
            var obj = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JObject
                {
                    ["code"] = code,
                    ["message"] = message
                }
            };
            return obj.ToString(Formatting.None);
        }

        // ------------------------------------------------------------------
        // Tool registration
        // ------------------------------------------------------------------

        private void RegisterTool(string name, string description, string inputSchemaJson, Func<Dictionary<string, object>, string> handler)
        {
            _tools[name] = new McpTool
            {
                Name = name,
                Description = description,
                InputSchemaJson = inputSchemaJson,
                Handler = handler
            };
        }

        // ------------------------------------------------------------------
        // Legacy REST dispatch (backward compatibility)
        // ------------------------------------------------------------------

        private void HandleLegacyRestRequest(string method, string path, HttpListenerContext context, HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            switch (path)
            {
                case "/health":
                    HandleRestHealth(response);
                    break;

                case "/assets/list":
                    HandleRestAssetsList(response, query);
                    break;

                case "/assets/import":
                    RequirePost(method, response, () => HandleRestAssetsImport(context, response));
                    break;

                case "/materials/create":
                    RequirePost(method, response, () => HandleRestMaterialCreate(context, response));
                    break;

                case "/materials/create-instance":
                    RequirePost(method, response, () => HandleRestMaterialInstanceCreate(context, response));
                    break;

                case "/scene/hierarchy":
                    HandleRestSceneHierarchy(response);
                    break;

                case "/scene/actor":
                    HandleRestSceneActor(response, query);
                    break;

                case "/scene/actor/property":
                    RequirePost(method, response, () => HandleRestSetActorProperty(context, response));
                    break;

                case "/scene/find":
                    HandleRestFindActors(response, query);
                    break;

                case "/editor/logs":
                    HandleRestEditorLogs(response, query);
                    break;

                case "/editor/play":
                    RequirePost(method, response, () => HandleRestEditorPlay(response));
                    break;

                case "/editor/stop":
                    RequirePost(method, response, () => HandleRestEditorStop(response));
                    break;

                case "/content/find":
                    HandleRestContentFind(response, query);
                    break;

                case "/scripts/read":
                    HandleRestScriptRead(response, query);
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
        // REST: GET /health
        // ------------------------------------------------------------------

        private void HandleRestHealth(HttpListenerResponse response)
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
        // REST: GET /assets/list
        // ------------------------------------------------------------------

        private void HandleRestAssetsList(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["path"] != null) args["path"] = query["path"];
            if (query["type"] != null) args["type"] = query["type"];
            WriteJson(response, 200, ToolListAssets(args));
        }

        // ------------------------------------------------------------------
        // REST: POST /assets/import
        // ------------------------------------------------------------------

        private void HandleRestAssetsImport(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<ImportRequest>(body);

            if (data?.Files == null || data.Files.Length == 0)
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'files' array in request body."));
                return;
            }

            var args = new Dictionary<string, object>();
            args["files"] = new List<object>(data.Files);
            args["target"] = data.Target ?? "Content";
            args["skipDialog"] = data.SkipDialog;
            WriteJson(response, 200, ToolImportAssets(args));
        }

        // ------------------------------------------------------------------
        // REST: POST /materials/create
        // ------------------------------------------------------------------

        private void HandleRestMaterialCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<MaterialCreateRequest>(body);

            if (string.IsNullOrEmpty(data?.Name) || string.IsNullOrEmpty(data?.OutputPath))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'outputPath' in request body."));
                return;
            }

            var args = new Dictionary<string, object>
            {
                ["name"] = data.Name,
                ["outputPath"] = data.OutputPath
            };

            var result = ToolCreateMaterial(args);
            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // REST: POST /materials/create-instance
        // ------------------------------------------------------------------

        private void HandleRestMaterialInstanceCreate(HttpListenerContext context, HttpListenerResponse response)
        {
            var body = ReadRequestBody(context.Request);
            var data = FlaxEngine.Json.JsonSerializer.Deserialize<MaterialInstanceCreateRequest>(body);

            if (string.IsNullOrEmpty(data?.Name) || string.IsNullOrEmpty(data?.OutputPath))
            {
                WriteJson(response, 400, BuildJsonObject("error", "Missing 'name' or 'outputPath' in request body."));
                return;
            }

            var args = new Dictionary<string, object>
            {
                ["name"] = data.Name,
                ["outputPath"] = data.OutputPath
            };
            if (!string.IsNullOrEmpty(data.BaseMaterial))
                args["baseMaterial"] = data.BaseMaterial;
            if (data.Parameters != null)
                args["parameters"] = data.Parameters;

            var result = ToolCreateMaterialInstance(args);
            var statusCode = result.Contains("\"error\"") ? 500 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // REST: GET /scene/hierarchy
        // ------------------------------------------------------------------

        private void HandleRestSceneHierarchy(HttpListenerResponse response)
        {
            WriteJson(response, 200, ToolGetSceneHierarchy(new Dictionary<string, object>()));
        }

        // ------------------------------------------------------------------
        // REST: GET /scene/actor
        // ------------------------------------------------------------------

        private void HandleRestSceneActor(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["name"] != null) args["name"] = query["name"];
            if (query["id"] != null) args["id"] = query["id"];

            if (args.Count == 0)
            {
                WriteJson(response, 400, BuildJsonObject("error", "Provide 'name' or 'id' query parameter."));
                return;
            }

            var result = ToolGetActor(args);
            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // REST: POST /scene/actor/property
        // ------------------------------------------------------------------

        private void HandleRestSetActorProperty(HttpListenerContext context, HttpListenerResponse response)
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

            var args = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(data.ActorName)) args["actorName"] = data.ActorName;
            if (!string.IsNullOrEmpty(data.ActorId)) args["actorId"] = data.ActorId;
            args["property"] = data.Property;
            args["value"] = data.Value;

            var result = ToolSetActorProperty(args);
            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // REST: GET /scene/find
        // ------------------------------------------------------------------

        private void HandleRestFindActors(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["name"] != null) args["name"] = query["name"];
            if (query["type"] != null) args["type"] = query["type"];
            if (query["tag"] != null) args["tag"] = query["tag"];
            WriteJson(response, 200, ToolFindActors(args));
        }

        // ------------------------------------------------------------------
        // REST: GET /editor/logs
        // ------------------------------------------------------------------

        private void HandleRestEditorLogs(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["count"] != null) args["count"] = query["count"];
            WriteJson(response, 200, ToolGetEditorLogs(args));
        }

        // ------------------------------------------------------------------
        // REST: POST /editor/play & /editor/stop
        // ------------------------------------------------------------------

        private void HandleRestEditorPlay(HttpListenerResponse response)
        {
            WriteJson(response, 200, ToolEditorPlay(new Dictionary<string, object>()));
        }

        private void HandleRestEditorStop(HttpListenerResponse response)
        {
            WriteJson(response, 200, ToolEditorStop(new Dictionary<string, object>()));
        }

        // ------------------------------------------------------------------
        // REST: GET /content/find
        // ------------------------------------------------------------------

        private void HandleRestContentFind(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["path"] != null) args["path"] = query["path"];
            var result = ToolFindContent(args);
            var statusCode = result.Contains("\"error\"") ? 404 : 200;
            WriteJson(response, statusCode, result);
        }

        // ------------------------------------------------------------------
        // REST: GET /scripts/read
        // ------------------------------------------------------------------

        private void HandleRestScriptRead(HttpListenerResponse response, System.Collections.Specialized.NameValueCollection query)
        {
            var args = new Dictionary<string, object>();
            if (query["path"] != null) args["path"] = query["path"];
            var result = ToolReadScript(args);
            var statusCode = result.Contains("\"error\"") ? 400 : 200;
            WriteJson(response, statusCode, result);
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

        /// <summary>
        /// Resolves an actor from a dictionary containing optional "name",
        /// "id", "actorName", or "actorId" keys.
        /// </summary>
        private Actor ResolveActor(Dictionary<string, object> args)
        {
            var id = GetArgString(args, "id") ?? GetArgString(args, "actorId");
            var name = GetArgString(args, "name") ?? GetArgString(args, "actorName") ?? GetArgString(args, "actor");

            if (!string.IsNullOrEmpty(id))
            {
                if (Guid.TryParse(id, out var guid))
                    return FlaxEngine.Object.Find<Actor>(ref guid);
            }

            if (!string.IsNullOrEmpty(name))
                return FindActorByName(name);

            return null;
        }

        // ------------------------------------------------------------------
        // Main thread marshaling
        // ------------------------------------------------------------------

        /// <summary>
        /// Invokes a function on the main thread via <see cref="Scripting.InvokeOnUpdate"/>
        /// and blocks the calling (HTTP worker) thread until the result is available.
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

            if (statusCode == 204 || string.IsNullOrEmpty(json))
            {
                response.ContentLength64 = 0;
                return;
            }

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
        /// <summary>
        /// Normalizes Windows-style backslashes to forward slashes in path strings.
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// JSON-escapes a path string, normalizing backslashes to forward slashes first.
        /// </summary>
        private static string JsonEscapePath(string value)
        {
            return JsonEscape(NormalizePath(value));
        }

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
        // Argument extraction helpers
        // ------------------------------------------------------------------

        private static string GetArgString(Dictionary<string, object> args, string key, string defaultValue = null)
        {
            if (args.TryGetValue(key, out var val) && val != null)
                return val.ToString();
            return defaultValue;
        }

        private static float GetArgFloat(Dictionary<string, object> args, string key, float defaultValue = 0f)
        {
            if (args.TryGetValue(key, out var val) && val != null)
            {
                if (val is double d) return (float)d;
                if (val is long l) return l;
                if (val is float f) return f;
                if (float.TryParse(val.ToString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }
            return defaultValue;
        }

        private static int GetArgInt(Dictionary<string, object> args, string key, int defaultValue = 0)
        {
            if (args.TryGetValue(key, out var val) && val != null)
            {
                if (val is long l) return (int)l;
                if (val is double d) return (int)d;
                if (val is int i) return i;
                if (int.TryParse(val.ToString(), out var parsed))
                    return parsed;
            }
            return defaultValue;
        }

        private static bool GetArgBool(Dictionary<string, object> args, string key, bool defaultValue = false)
        {
            if (args.TryGetValue(key, out var val) && val != null)
            {
                if (val is bool b) return b;
                if (bool.TryParse(val.ToString(), out var parsed))
                    return parsed;
            }
            return defaultValue;
        }

        // ------------------------------------------------------------------
        // Property manipulation helpers
        // ------------------------------------------------------------------

        private void SetPropertyByPath(Actor actor, string propertyPath, object value)
        {
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

            var converted = ConvertPropertyValue(value, finalProp.PropertyType);
            finalProp.SetValue(current, converted);
        }

        private object ConvertPropertyValue(object value, Type targetType)
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
        // Scene tree helpers
        // ------------------------------------------------------------------

        private void BuildActorJson(StringBuilder sb, Actor actor, int indent)
        {
            var pad = new string(' ', indent * 2);
            var innerPad = new string(' ', (indent + 1) * 2);

            sb.AppendLine("{");
            sb.AppendLine($"{innerPad}\"name\": {JsonEscape(actor.Name)},");
            sb.AppendLine($"{innerPad}\"type\": {JsonEscape(actor.GetType().Name)},");
            sb.AppendLine($"{innerPad}\"id\": {JsonEscape(actor.ID.ToString())},");
            sb.AppendLine($"{innerPad}\"isActive\": {(actor.IsActive ? "true" : "false")},");

            var pos = actor.Position;
            sb.AppendLine($"{innerPad}\"position\": {{ \"X\": {pos.X}, \"Y\": {pos.Y}, \"Z\": {pos.Z} }},");

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

        private string BuildActorDetailJson(Actor actor)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"name\": {JsonEscape(actor.Name)},");
            sb.AppendLine($"  \"type\": {JsonEscape(actor.GetType().Name)},");
            sb.AppendLine($"  \"id\": {JsonEscape(actor.ID.ToString())},");
            sb.AppendLine($"  \"isActive\": {(actor.IsActive ? "true" : "false")},");

            var t = actor.Transform;
            sb.AppendLine("  \"transform\": {");
            sb.AppendLine($"    \"position\": {{ \"X\": {t.Translation.X}, \"Y\": {t.Translation.Y}, \"Z\": {t.Translation.Z} }},");
            sb.AppendLine($"    \"rotation\": {{ \"X\": {t.Orientation.X}, \"Y\": {t.Orientation.Y}, \"Z\": {t.Orientation.Z}, \"W\": {t.Orientation.W} }},");
            sb.AppendLine($"    \"scale\": {{ \"X\": {t.Scale.X}, \"Y\": {t.Scale.Y}, \"Z\": {t.Scale.Z} }}");
            sb.AppendLine("  },");

            sb.Append("  \"tags\": [");
            if (actor.Tags != null && actor.Tags.Length > 0)
            {
                for (int ti = 0; ti < actor.Tags.Length; ti++)
                {
                    sb.Append(JsonEscape(actor.Tags[ti].ToString()));
                    if (ti < actor.Tags.Length - 1)
                        sb.Append(", ");
                }
            }
            sb.AppendLine("],");

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
        // Collection helpers
        // ------------------------------------------------------------------

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

        private void CollectActorsOfType<T>(Actor root, List<T> results) where T : Actor
        {
            if (root is T typed)
                results.Add(typed);

            foreach (var child in root.Children)
                CollectActorsOfType(child, results);
        }

        private void CollectActorsOfTypeName(Actor root, string typeName, List<Actor> results)
        {
            if (string.Equals(root.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                results.Add(root);

            foreach (var child in root.Children)
                CollectActorsOfTypeName(child, typeName, results);
        }

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
                            "path", NormalizePath(child.Path),
                            "id", assetItem.ID.ToString()
                        ));
                    }
                }
            }
        }

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
                            "path", NormalizePath(child.Path),
                            "id", assetItem.ID.ToString()
                        ));
                    }
                }
            }
        }

        private void CollectContentTree(ContentFolder folder, StringBuilder sb, int indent, int maxDepth = -1, int currentDepth = 0)
        {
            if (folder == null)
                return;

            var pad = new string(' ', indent * 2);
            var innerPad = new string(' ', (indent + 1) * 2);

            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{innerPad}\"name\": {JsonEscape(folder.ShortName)},");
            sb.AppendLine($"{innerPad}\"path\": {JsonEscapePath(folder.Path)},");
            sb.AppendLine($"{innerPad}\"type\": \"Folder\",");

            // Count direct child assets
            var assetCount = folder.Children.Count(c => !(c is ContentFolder));
            sb.AppendLine($"{innerPad}\"assetCount\": {assetCount},");

            sb.AppendLine($"{innerPad}\"children\": [");

            if (maxDepth < 0 || currentDepth < maxDepth)
            {
                var childFolders = folder.Children.Where(c => c is ContentFolder).ToList();
                for (int i = 0; i < childFolders.Count; i++)
                {
                    CollectContentTree((ContentFolder)childFolders[i], sb, indent + 2, maxDepth, currentDepth + 1);
                    if (i < childFolders.Count - 1)
                        sb.Append(",");
                    sb.AppendLine();
                }
            }

            sb.AppendLine($"{innerPad}]");
            sb.Append($"{pad}}}");
        }

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

        // ------------------------------------------------------------------
        // JSON request data classes (used by legacy REST endpoints)
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

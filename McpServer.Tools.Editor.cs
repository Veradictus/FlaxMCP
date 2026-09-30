using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using FlaxEditor;
using FlaxEditor.Content.Settings;
using FlaxEngine;
using Newtonsoft.Json.Linq;

namespace FlaxMCP
{
    /// <summary>
    /// Editor control and utility MCP tool handlers. Covers play/stop/pause/resume,
    /// actor selection and focus, viewport get/set, undo/redo, editor windows,
    /// frame stats, script management (list, read, compile, errors), build
    /// (build game, status), project settings (get/set), batch execute, and
    /// health/status queries (get_health, get_project_status, get_editor_state,
    /// get_editor_logs).
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Health & Status
        // ==================================================================

        /// <summary>
        /// Returns server health including engine version and play mode state.
        /// </summary>
        private string ToolGetHealth(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                // Read project name from saved settings rather than runtime cache
                var projectName = Globals.ProductName;
                try
                {
                    var settings = GameSettings.Load();
                    if (!string.IsNullOrEmpty(settings.ProductName))
                        projectName = settings.ProductName;
                }
                catch { }

                return BuildJsonObject(
                    "status", "ok",
                    "engine", "FlaxEngine",
                    "version", Globals.EngineBuildNumber.ToString(),
                    "project", projectName,
                    "isPlaying", Editor.Instance.StateMachine.IsPlayMode.ToString().ToLowerInvariant()
                );
            });
        }

        /// <summary>
        /// Returns project compile status, scene count, asset count, and recent errors.
        /// </summary>
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
                // The log file also holds native errors (shaders, assets) the managed hook misses.
                List<LogEntry> errors = null;
                try
                {
                    errors = ReadEditorLogFile(out _)?.Where(e => LogLevelRank(e.Level) >= 2).TakeLast(10).ToList();
                }
                catch (IOException)
                {
                }
                if (errors == null)
                {
                    lock (_logLock)
                    {
                        errors = _logBuffer
                            .Where(e => e.Level == "Error" || e.Level == "Exception")
                            .TakeLast(10)
                            .ToList();
                    }
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

        /// <summary>
        /// Returns editor state including play mode, project name, loaded scenes, and selection.
        /// </summary>
        private string ToolGetEditorState(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");

                sb.AppendLine($"  \"isPlaying\": {(Editor.Instance.StateMachine.IsPlayMode ? "true" : "false")},");
                sb.AppendLine($"  \"isPaused\": {(Editor.Instance.StateMachine.PlayingState.IsPaused ? "true" : "false")},");
                sb.AppendLine($"  \"projectName\": {JsonEscape(Globals.ProductName)},");
                sb.AppendLine($"  \"projectFolder\": {JsonEscapePath(Globals.ProjectFolder)},");

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

        /// <summary>
        /// Returns recent editor log entries, up to the specified count.
        /// </summary>
        private string ToolGetEditorLogs(Dictionary<string, object> args)
        {
            int count = GetArgInt(args, "count", 50);
            count = Math.Clamp(count, 1, MaxLogEntries);
            var source = (GetArgString(args, "source", "file") ?? "file").ToLowerInvariant();
            var minLevel = GetArgString(args, "level");
            var contains = GetArgString(args, "contains");

            List<LogEntry> entries;
            string logFile = null;
            if (source == "file")
            {
                entries = ReadEditorLogFile(out logFile);
                if (entries == null)
                    return BuildJsonObject("error", "Could not find the editor log file. Use source 'managed' for messages logged from C#.");
            }
            else if (source == "managed")
            {
                lock (_logLock)
                    entries = new List<LogEntry>(_logBuffer);
            }
            else
            {
                return BuildJsonObject("error", $"Unknown source '{source}'. Use 'file' or 'managed'.");
            }

            var minRank = LogLevelRank(minLevel);
            entries = entries
                .Where(e => LogLevelRank(e.Level) >= minRank)
                .Where(e => string.IsNullOrEmpty(contains) || (e.Message != null && e.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            entries = entries.Skip(Math.Max(0, entries.Count - count)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"source\": {JsonEscape(source)},");
            if (logFile != null)
                sb.AppendLine($"  \"logFile\": {JsonEscapePath(logFile)},");
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

        private const long MaxLogReadBytes = 4 * 1024 * 1024;

        private static readonly System.Text.RegularExpressions.Regex LogLineRegex =
            new System.Text.RegularExpressions.Regex(@"^\[ (\d+:\d+:\d+\.\d+) \]: \[(\w+)\] ?(.*)$");

        private static int LogLevelRank(string level)
        {
            switch ((level ?? "").ToLowerInvariant())
            {
                case "warning": return 1;
                case "error": return 2;
                case "exception": return 2;
                case "fatal": return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// Reads the log file of the running editor. Unlike the managed log hook, this
        /// includes messages from native code such as shader and material compilation.
        /// Returns null when the file cannot be found.
        /// </summary>
        private List<LogEntry> ReadEditorLogFile(out string logFile)
        {
            var lines = ReadEditorLogLines(out logFile);
            return lines != null ? ParseLogEntries(lines) : null;
        }

        /// <summary>
        /// Groups raw log lines into entries; lines without a timestamp prefix continue the previous entry.
        /// </summary>
        private static List<LogEntry> ParseLogEntries(string[] lines)
        {
            var entries = new List<LogEntry>();
            LogEntry current = null;
            foreach (var rawLine in lines)
            {
                // Native messages can carry a trailing NUL from C strings.
                var line = rawLine.Replace("\0", "");
                var match = LogLineRegex.Match(line);
                if (match.Success)
                {
                    current = new LogEntry
                    {
                        Timestamp = match.Groups[1].Value,
                        Level = match.Groups[2].Value,
                        Message = match.Groups[3].Value
                    };
                    entries.Add(current);
                }
                else if (current != null && line.Length > 0 && !line.StartsWith("===="))
                {
                    // Multi-line messages (stack traces, compiler output) continue without a prefix.
                    current.Message += "\n" + line;
                }
            }
            return entries;
        }

        /// <summary>
        /// Reads the tail of the running editor's log file as raw lines, or null if it cannot be found.
        /// </summary>
        private string[] ReadEditorLogLines(out string logFile, long fromByte = -1)
        {
            logFile = FindEditorLogFile();
            if (logFile == null)
                return null;

            string text;
            using (var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var start = Math.Max(fromByte, stream.Length - MaxLogReadBytes);
                start = Math.Clamp(start, 0, stream.Length);
                start -= start % 2; // The file is UTF-16; keep character alignment.
                stream.Position = start;
                using (var reader = new StreamReader(stream, Encoding.Unicode, start == 0))
                    text = reader.ReadToEnd();
            }
            return text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }

        /// <summary>
        /// Current size of the editor log file in bytes; pass it to <see cref="ReadEditorLogLines"/>
        /// later to read only what was logged in between.
        /// </summary>
        private long GetEditorLogLength()
        {
            var logFile = FindEditorLogFile();
            return logFile != null ? new FileInfo(logFile).Length : 0;
        }

        private string FindEditorLogFile()
        {
            var logsFolder = Path.Combine(_projectFolder, "Logs");
            if (!Directory.Exists(logsFolder))
                return null;
            var processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var files = new DirectoryInfo(logsFolder).GetFiles("Log_*.txt")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
            // Prefer the file this editor process created; fall back to the newest one.
            var mine = files.FirstOrDefault(f => f.CreationTimeUtc >= processStart.AddSeconds(-5));
            return (mine ?? files.FirstOrDefault())?.FullName;
        }

        private static bool IsShaderMessage(LogEntry e)
        {
            var m = e.Message ?? "";
            return m.IndexOf("shader", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("Failed to compile", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf(".hlsl", StringComparison.OrdinalIgnoreCase) >= 0
                || m.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns shader and material compilation errors and warnings from the editor log file.
        /// </summary>
        private string ToolGetShaderErrors(Dictionary<string, object> args)
        {
            var count = Math.Clamp(GetArgInt(args, "count", 20), 1, MaxLogEntries);
            var entries = ReadEditorLogFile(out var logFile);
            if (entries == null)
                return BuildJsonObject("error", "Could not find the editor log file.");

            var matches = entries.Where(e => LogLevelRank(e.Level) >= 1 && IsShaderMessage(e)).ToList();
            matches = matches.Skip(Math.Max(0, matches.Count - count)).ToList();

            var result = new JObject
            {
                ["logFile"] = NormalizePath(logFile),
                ["count"] = matches.Count,
                ["errors"] = new JArray(matches.Select(e => new JObject
                {
                    ["level"] = e.Level,
                    ["time"] = e.Timestamp,
                    ["message"] = e.Message
                })),
            };
            if (matches.Count == 0)
                result["note"] = "No shader or material errors or warnings in this editor session's log.";
            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // ==================================================================
        // TOOL HANDLERS: Editor Control
        // ==================================================================

        /// <summary>
        /// Starts play mode in the editor.
        /// </summary>
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

        /// <summary>
        /// Stops play mode in the editor.
        /// </summary>
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

        /// <summary>
        /// Pauses play mode. Only effective when the editor is playing.
        /// </summary>
        private string ToolEditorPause(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                if (!Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("error", "Editor is not in play mode.");

                Editor.Instance.Simulation.RequestPausePlay();
                return BuildJsonObject("ok", "true", "status", "pause_requested");
            });
        }

        /// <summary>
        /// Resumes play mode after pausing.
        /// </summary>
        private string ToolEditorResume(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                if (!Editor.Instance.StateMachine.IsPlayMode)
                    return BuildJsonObject("error", "Editor is not in play mode.");

                Editor.Instance.Simulation.RequestResumePlay();
                return BuildJsonObject("ok", "true", "status", "resume_requested");
            });
        }

        /// <summary>
        /// Selects an actor in the editor scene graph.
        /// </summary>
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

        /// <summary>
        /// Focuses the editor viewport on an actor.
        /// </summary>
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

        /// <summary>
        /// Gets the editor viewport camera position, direction, and orientation.
        /// </summary>
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

        /// <summary>
        /// Sets the editor viewport camera position and orientation.
        /// </summary>
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
        // TOOL HANDLERS: Undo / Redo
        // ==================================================================

        /// <summary>
        /// Undoes the last editor action.
        /// </summary>
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

        /// <summary>
        /// Redoes the last undone editor action.
        /// </summary>
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
        // TOOL HANDLERS: Scripts
        // ==================================================================

        /// <summary>
        /// Lists all C# script files in the project Source folder.
        /// </summary>
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

        /// <summary>
        /// Reads the source code of a script file by project-relative path.
        /// </summary>
        private string ToolReadScript(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            var projectFolder = InvokeOnMainThread(() => Globals.ProjectFolder);
            string absPath;
            try
            {
                absPath = ResolveProjectPath(projectFolder, path);
            }
            catch (Exception ex)
            {
                return BuildJsonObject("error", ex.Message);
            }

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

        /// <summary>
        /// Triggers script compilation in the editor.
        /// </summary>
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

        private static readonly System.Text.RegularExpressions.Regex CompilerDiagnosticRegex =
            new System.Text.RegularExpressions.Regex(@"(?<file>[A-Za-z]:[^:()]*?\.cs|[^\s()\]]+\.cs)\((?<pos>[\d,]+)\): (?<level>error|warning) (?<code>[A-Z]+\d+): (?<msg>.*)$");

        /// <summary>
        /// Gets C# compiler diagnostics from the most recent script compilation in the editor log.
        /// </summary>
        private string ToolGetScriptErrors(Dictionary<string, object> args)
        {
            var includeWarnings = GetArgBool(args, "includeWarnings", false);
            var state = InvokeOnMainThread(() => new[] { ScriptsBuilder.LastCompilationFailed, ScriptsBuilder.IsCompiling });

            var result = new JObject
            {
                ["hasCompilationErrors"] = state[0],
                ["isCompiling"] = state[1],
            };

            var diagnostics = new JArray();
            int errorCount = 0, warningCount = 0;
            var lines = ReadEditorLogLines(out var logFile);
            if (lines != null)
            {
                // Only look at output of the latest compilation; older errors are stale.
                var start = Math.Max(0, Array.FindLastIndex(lines, l => l.Contains("Starting scripts compilation")));
                var seen = new HashSet<string>();
                for (var i = start; i < lines.Length; i++)
                {
                    var match = CompilerDiagnosticRegex.Match(lines[i]);
                    if (!match.Success)
                        continue;
                    var key = match.Groups["file"].Value + match.Groups["pos"].Value + match.Groups["code"].Value;
                    if (!seen.Add(key))
                        continue; // Flax.Build echoes each diagnostic more than once.
                    var level = match.Groups["level"].Value;
                    if (level == "warning")
                    {
                        warningCount++;
                        if (!includeWarnings)
                            continue;
                    }
                    else
                    {
                        errorCount++;
                    }
                    diagnostics.Add(new JObject
                    {
                        ["level"] = level,
                        ["code"] = match.Groups["code"].Value,
                        ["file"] = NormalizePath(match.Groups["file"].Value),
                        ["position"] = match.Groups["pos"].Value,
                        ["message"] = match.Groups["msg"].Value.Trim(),
                    });
                }
                result["logFile"] = NormalizePath(logFile);
            }
            else
            {
                List<LogEntry> entries;
                lock (_logLock)
                    entries = _logBuffer.Where(e => e.Level == "Error" && e.Message != null && e.Message.Contains("error CS")).ToList();
                foreach (var e in entries)
                    diagnostics.Add(new JObject { ["level"] = "error", ["message"] = e.Message });
                errorCount = entries.Count;
                result["note"] = "Editor log file not found; showing errors captured from managed logging only.";
            }

            result["errorCount"] = errorCount;
            result["warningCount"] = warningCount;
            result["errors"] = diagnostics;
            if (!includeWarnings && warningCount > 0)
                result["warningsHidden"] = "Pass includeWarnings: true to list warnings.";
            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        // ==================================================================
        // TOOL HANDLERS: Component / Script Management
        // ==================================================================

        /// <summary>
        /// Adds a script component to an actor by fully qualified type name.
        /// </summary>
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

        /// <summary>
        /// Removes a script from an actor by zero-based index.
        /// </summary>
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

        /// <summary>
        /// Lists all scripts on an actor with their types, enabled state, and public properties.
        /// </summary>
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
        // TOOL HANDLERS: Build
        // ==================================================================

        /// <summary>
        /// Requests a game build for a target platform and configuration.
        /// </summary>
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

        /// <summary>
        /// Returns the current build status string.
        /// </summary>
        private string ToolGetBuildStatus(Dictionary<string, object> args)
        {
            return BuildJsonObject("status", _buildStatus);
        }

        // ==================================================================
        // TOOL HANDLERS: Project Settings
        // ==================================================================

        /// <summary>
        /// Reads GameSettings.json and returns project configuration.
        /// </summary>
        private string ToolGetProjectSettings(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                try
                {
                    var settings = GameSettings.Load();

                    var sb = new StringBuilder();
                    sb.AppendLine("{");
                    sb.AppendLine($"  \"productName\": {JsonEscape(settings.ProductName)},");
                    sb.AppendLine($"  \"companyName\": {JsonEscape(settings.CompanyName)},");
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

                    sb.AppendLine($"  \"settingsPath\": {JsonEscapePath(GameSettings.GameSettingsAssetPath)}");
                    sb.Append("}");
                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    return BuildJsonObject("error", $"Failed to read project settings: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Updates GameSettings fields such as productName and companyName.
        /// </summary>
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
                        settings.ProductName = productName;
                        changed = true;
                    }

                    var companyName = GetArgString(args, "companyName");
                    if (companyName != null)
                    {
                        settings.CompanyName = companyName;
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
        // TOOL HANDLERS: Batch Execute
        // ==================================================================

        /// <summary>
        /// Executes multiple tool calls in sequence, returning results for each.
        /// </summary>
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
        // TOOL HANDLERS: Profiling
        // ==================================================================

        /// <summary>
        /// Gets current frame timing, CPU/GPU timings, draw statistics and memory usage.
        /// </summary>
        private string ToolGetFrameStats(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var stats = ProfilingTools.Stats;
                var profilerEnabled = ProfilingTools.Enabled;
                const double mb = 1024.0 * 1024.0;

                var result = new JObject
                {
                    ["fps"] = Engine.FramesPerSecond,
                    ["deltaTime"] = Time.DeltaTime,
                    ["gameTime"] = Time.GameTime,
                    ["unscaledDeltaTime"] = Time.UnscaledDeltaTime,
                    ["timeScale"] = Time.TimeScale,
                    ["updateCpuMs"] = Math.Round(stats.UpdateTimeMs, 3),
                    ["physicsCpuMs"] = Math.Round(stats.PhysicsTimeMs, 3),
                    ["drawCpuMs"] = Math.Round(stats.DrawCPUTimeMs, 3),
                    // GPU timings come from timer queries that only run while the profiler is on.
                    ["drawGpuMs"] = profilerEnabled ? (JToken)Math.Round(stats.DrawGPUTimeMs, 3) : JValue.CreateNull(),
                    ["drawCalls"] = stats.DrawStats.DrawCalls,
                    ["triangles"] = stats.DrawStats.Triangles,
                    ["vertices"] = stats.DrawStats.Vertices,
                    ["gpuMemoryUsedMB"] = Math.Round(stats.MemoryGPU.Used / mb, 1),
                    ["gpuMemoryTotalMB"] = Math.Round(stats.MemoryGPU.Total / mb, 1),
                    ["processMemoryMB"] = Math.Round(stats.ProcessMemory.UsedPhysicalMemory / mb, 1),
                    ["profilerEnabled"] = profilerEnabled,
                };
                if (!profilerEnabled)
                    result["note"] = "drawGpuMs is null while the profiler is off. Use get_gpu_profile for GPU timings.";
                return result.ToString(Newtonsoft.Json.Formatting.Indented);
            });
        }

        private struct GpuEventSample
        {
            public string Name;
            public float TimeMs;
            public int Depth;
        }

        private class GpuPassStats
        {
            public string Name;
            public string Path;
            public int Depth;
            public int Order;
            public double SumMs;
            public float MaxMs;
            public int Count;
        }

        /// <summary>
        /// Turns the engine profiler on for a number of frames and returns averaged
        /// GPU timings per rendering pass.
        /// </summary>
        private string ToolGetGpuProfile(Dictionary<string, object> args)
        {
            var frames = Math.Clamp(GetArgInt(args, "frames", 30), 1, 300);
            var maxDepth = Math.Clamp(GetArgInt(args, "maxDepth", 3), 0, 32);
            var minMs = Math.Max(0f, GetArgFloat(args, "minMs", 0.01f));

            var wasEnabled = InvokeOnMainThread(() =>
            {
                var enabled = ProfilingTools.Enabled;
                ProfilingTools.Enabled = true;
                return enabled;
            });

            var passes = new Dictionary<string, GpuPassStats>();
            var frameGpuMs = new List<float>();
            long drawCalls = 0, triangles = 0;
            var sampled = 0;
            try
            {
                // GPU timer queries resolve a few frames late; skip those frames.
                ulong lastFrame = InvokeOnMainThread(() => Engine.FrameCount) + 5;
                var deadline = DateTime.UtcNow.AddSeconds(10 + frames * 0.25);
                var stack = new string[64];
                while (sampled < frames && DateTime.UtcNow < deadline)
                {
                    var snapshot = InvokeOnMainThread(() => CaptureGpuEvents(ref lastFrame));
                    if (snapshot == null)
                    {
                        Thread.Sleep(5);
                        continue;
                    }

                    sampled++;
                    frameGpuMs.Add(snapshot.Value.FrameMs);
                    drawCalls = snapshot.Value.DrawCalls;
                    triangles = snapshot.Value.Triangles;
                    foreach (var e in snapshot.Value.Events)
                    {
                        if (e.Depth < 0 || e.Depth >= stack.Length)
                            continue;
                        stack[e.Depth] = e.Name;
                        var path = string.Join(" > ", stack, 0, e.Depth + 1);
                        if (!passes.TryGetValue(path, out var pass))
                        {
                            pass = new GpuPassStats { Name = e.Name, Path = path, Depth = e.Depth, Order = passes.Count };
                            passes[path] = pass;
                        }
                        pass.SumMs += e.TimeMs;
                        pass.MaxMs = Math.Max(pass.MaxMs, e.TimeMs);
                        pass.Count++;
                    }
                }
            }
            finally
            {
                if (!wasEnabled)
                    InvokeOnMainThread(() => { ProfilingTools.Enabled = false; return true; });
            }

            if (sampled == 0)
                return BuildJsonObject("error", "No GPU profiler data arrived. The editor may be minimized or not rendering.");

            var avgFrame = frameGpuMs.Average();
            double Avg(GpuPassStats p) => p.SumMs / sampled;

            var passList = new JArray();
            foreach (var p in passes.Values.OrderBy(p => p.Order))
            {
                if (p.Depth > maxDepth || Avg(p) < minMs)
                    continue;
                passList.Add(new JObject
                {
                    ["name"] = new string(' ', p.Depth * 2) + p.Name,
                    ["depth"] = p.Depth,
                    ["avgMs"] = Math.Round(Avg(p), 3),
                    ["maxMs"] = Math.Round(p.MaxMs, 3),
                    ["percentOfFrame"] = avgFrame > 0 ? Math.Round(Avg(p) / avgFrame * 100.0, 1) : 0,
                });
            }

            var slowest = new JArray();
            foreach (var p in passes.Values.Where(p => p.Depth > 0).OrderByDescending(Avg).Take(10))
                slowest.Add(new JObject { ["path"] = p.Path, ["avgMs"] = Math.Round(Avg(p), 3) });

            var result = new JObject
            {
                ["framesSampled"] = sampled,
                ["gpuFrameMs"] = new JObject
                {
                    ["avg"] = Math.Round(avgFrame, 3),
                    ["min"] = Math.Round(frameGpuMs.Min(), 3),
                    ["max"] = Math.Round(frameGpuMs.Max(), 3),
                },
                ["drawCalls"] = drawCalls,
                ["triangles"] = triangles,
                ["slowestPasses"] = slowest,
                ["passes"] = passList,
                ["note"] = "Times cover every view the editor renders (editor viewport, game view, UI). Close or hide views you do not want measured.",
            };
            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private struct GpuSnapshot
        {
            public float FrameMs;
            public long DrawCalls;
            public long Triangles;
            public List<GpuEventSample> Events;
        }

        /// <summary>
        /// Copies the last resolved GPU profiler frame. Returns null when no new frame is available.
        /// Must run on the main thread.
        /// </summary>
        private static unsafe GpuSnapshot? CaptureGpuEvents(ref ulong lastFrame)
        {
            var frame = Engine.FrameCount;
            if (frame <= lastFrame)
                return null;
            var events = ProfilingTools.EventsGPU;
            if (events == null || events.Length == 0)
                return null;
            lastFrame = frame;

            var list = new List<GpuEventSample>(events.Length);
            foreach (var e in events)
            {
                list.Add(new GpuEventSample
                {
                    Name = e.Name != null ? new string(e.Name) : "(unnamed)",
                    TimeMs = e.Time,
                    Depth = e.Depth,
                });
            }
            var stats = ProfilingTools.Stats;
            return new GpuSnapshot
            {
                FrameMs = stats.DrawGPUTimeMs,
                DrawCalls = stats.DrawStats.DrawCalls,
                Triangles = stats.DrawStats.Triangles,
                Events = list,
            };
        }

        // ==================================================================
        // TOOL HANDLERS: Editor Windows
        // ==================================================================

        /// <summary>
        /// Lists open editor windows with their types, titles, and visibility.
        /// </summary>
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
        // TOOL HANDLERS: Asset Pipeline
        // ==================================================================

        /// <summary>
        /// Triggers the asset import pipeline via a project-provided AssetConverterPlugin.
        /// The plugin is resolved by type name via reflection so FlaxMCP compiles in any
        /// project; the tool reports an error when no such plugin is loaded.
        /// Supports running individual steps or the full chained pipeline.
        /// </summary>
        private string ToolRunAssetPipeline(Dictionary<string, object> args)
        {
            var step = GetArgString(args, "step", "all");

            return InvokeOnMainThread(() =>
            {
                var plugin = PluginManager.EditorPlugins.Cast<Plugin>()
                    .Concat(PluginManager.GamePlugins.Cast<Plugin>())
                    .FirstOrDefault(p => p.GetType().Name == "AssetConverterPlugin");
                if (plugin == null)
                    return BuildJsonObject("error", "AssetConverterPlugin not found. Is it loaded?");

                string methodName, status;
                switch (step.ToLowerInvariant())
                {
                    case "textures":
                        methodName = "OnImportAllTextures";
                        status = "import_queued";
                        break;

                    case "models":
                        methodName = "OnImportAllModels";
                        status = "import_queued";
                        break;

                    case "materials":
                        methodName = "OnCreateAllMaterials";
                        status = "creation_started";
                        break;

                    case "all":
                        methodName = "OnRunFullPipeline";
                        status = "pipeline_started";
                        break;

                    default:
                        return BuildJsonObject("error", $"Unknown step: {step}. Use: textures, models, materials, all");
                }

                var method = plugin.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                if (method == null)
                    return BuildJsonObject("error", $"AssetConverterPlugin has no public method {methodName}.");
                method.Invoke(plugin, null);
                return BuildJsonObject("ok", "true", "step", step.ToLowerInvariant(), "status", status);
            });
        }
    }
}

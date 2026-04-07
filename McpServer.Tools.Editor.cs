using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FlaxEditor;
using FlaxEditor.Content.Settings;
using FlaxEngine;

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

        /// <summary>
        /// Gets script compilation errors and warnings from the log buffer.
        /// </summary>
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
        /// Gets current frame timing and rendering statistics.
        /// </summary>
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
        /// Triggers the asset import pipeline via the AssetConverterPlugin.
        /// Supports running individual steps or the full chained pipeline.
        /// </summary>
        private string ToolRunAssetPipeline(Dictionary<string, object> args)
        {
            var step = GetArgString(args, "step", "all");

            return InvokeOnMainThread(() =>
            {
                var plugin = PluginManager.GetPlugin<Embergrim.Editor.AssetConverterPlugin>();
                if (plugin == null)
                    return BuildJsonObject("error", "AssetConverterPlugin not found. Is it loaded?");

                switch (step.ToLowerInvariant())
                {
                    case "textures":
                        plugin.OnImportAllTextures();
                        return BuildJsonObject("ok", "true", "step", "textures", "status", "import_queued");

                    case "models":
                        plugin.OnImportAllModels();
                        return BuildJsonObject("ok", "true", "step", "models", "status", "import_queued");

                    case "materials":
                        plugin.OnCreateAllMaterials();
                        return BuildJsonObject("ok", "true", "step", "materials", "status", "creation_started");

                    case "all":
                        plugin.OnRunFullPipeline();
                        return BuildJsonObject("ok", "true", "step", "all", "status", "pipeline_started");

                    default:
                        return BuildJsonObject("error", $"Unknown step: {step}. Use: textures, models, materials, all");
                }
            });
        }
    }
}

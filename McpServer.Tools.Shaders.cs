using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEngine;
using Newtonsoft.Json.Linq;

namespace FlaxMCP
{
    /// <summary>
    /// Shader source tools. Covers compiling project shader sources
    /// (<c>Source/Shaders/*.shader</c>) and reporting the compiler output.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Shaders
        // ==================================================================

        /// <summary>
        /// Imports a shader source into <c>Content/Shaders</c> when needed, loads it (which compiles it)
        /// and returns the compiler errors and warnings logged for it.
        /// </summary>
        /// <remarks>
        /// The engine only watches <c>Source/Shaders</c> if the folder existed when the editor started,
        /// so shaders added later are never compiled automatically. This tool covers both cases.
        /// </remarks>
        private string ToolCompileShader(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");
            var timeoutMs = Math.Max(1000, GetArgInt(args, "timeoutMs", 60000));
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

            string sourcePath;
            try
            {
                sourcePath = ResolveProjectPath(_projectFolder, path);
            }
            catch (Exception ex)
            {
                return BuildJsonObject("error", ex.Message);
            }

            var shadersSource = Path.GetFullPath(Path.Combine(_projectFolder, "Source", "Shaders")) + Path.DirectorySeparatorChar;
            if (!sourcePath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                !sourcePath.StartsWith(shadersSource, StringComparison.OrdinalIgnoreCase))
                return BuildJsonObject("error", "Expected a .shader file inside Source/Shaders.", "path", path);
            if (!File.Exists(sourcePath))
                return BuildJsonObject("error", $"Shader source not found: {path}");

            var relative = sourcePath.Substring(shadersSource.Length);
            var assetPath = Path.Combine(_projectFolder, "Content", "Shaders", Path.ChangeExtension(relative, ".flax"));
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var logMark = GetEditorLogLength();
            var sourceTime = File.GetLastWriteTimeUtc(sourcePath);
            var needsImport = !File.Exists(assetPath) || File.GetLastWriteTimeUtc(assetPath) < sourceTime;

            if (needsImport)
            {
                var importError = InvokeOnMainThread(() => StartShaderImport(sourcePath, assetPath));
                if (importError != null)
                    return BuildJsonObject("error", importError);

                while (true)
                {
                    Thread.Sleep(100);
                    var importing = InvokeOnMainThread(() => Editor.Instance.ContentImporting.IsImporting);
                    if (!importing && File.Exists(assetPath) && File.GetLastWriteTimeUtc(assetPath) >= sourceTime)
                        break;
                    if (DateTime.UtcNow > deadline)
                        return BuildJsonObject("error", "Timed out waiting for the shader import to finish.", "assetPath", NormalizePath(assetPath));
                }
            }

            // Loading the asset compiles it. When nothing was re-imported, force a reload so
            // the caller always gets a fresh compile result.
            var shader = InvokeOnMainThread(() =>
            {
                var asset = FlaxEngine.Content.LoadAsync<Shader>(assetPath);
                if (asset != null && !needsImport && asset.IsLoaded)
                    asset.Reload();
                return asset;
            });
            if (shader == null)
                return BuildJsonObject("error", $"Could not load shader asset: {assetPath}");

            // The compiler's own log line is the verdict. The asset's load state can still be
            // stale from a previous attempt while the reload is in flight, so it is only used
            // when no compilation happens (e.g. the compiled cache is still valid).
            var succeededMarker = $"Shader compilation '{name}' succeed";
            var failedMarkers = new[] { $"Shader compilation '{name}' failed", $"Failed to compile '{name}'" };
            var settleAfter = DateTime.UtcNow.AddSeconds(3);
            string[] newLines;
            bool? verdict;
            while (true)
            {
                Thread.Sleep(150);
                newLines = ReadEditorLogLines(out _, logMark) ?? Array.Empty<string>();
                verdict = null;
                if (newLines.Any(l => failedMarkers.Any(m => l.Contains(m))))
                    verdict = false;
                else if (newLines.Any(l => l.Contains(succeededMarker)))
                    verdict = true;
                if (verdict.HasValue)
                    break;

                if (DateTime.UtcNow > settleAfter)
                {
                    var state = InvokeOnMainThread(() =>
                        shader.IsLoaded ? true : shader.LastLoadFailed ? false : (bool?)null);
                    if (state.HasValue)
                    {
                        verdict = state;
                        break;
                    }
                }
                if (DateTime.UtcNow > deadline)
                    return BuildJsonObject("error", "Timed out waiting for the shader to compile.", "assetPath", NormalizePath(assetPath));
            }

            // Give the remaining error lines of a failed compile a moment to be written.
            if (verdict == false)
            {
                Thread.Sleep(300);
                newLines = ReadEditorLogLines(out _, logMark) ?? newLines;
            }

            var entries = ParseLogEntries(newLines);
            var problems = entries
                .Where(e => LogLevelRank(e.Level) >= 1 && (MentionsShader(e.Message, name) || e.Message.Contains("Failed to compile")))
                .ToList();
            var compileLog = entries.Where(e => MentionsShader(e.Message, name)).Select(e => $"[{e.Level}] {e.Message}").ToList();
            var compiled = verdict == true && problems.All(e => LogLevelRank(e.Level) < 2);

            var result = new JObject
            {
                ["compiled"] = compiled,
                ["shader"] = name,
                ["source"] = NormalizePath(sourcePath),
                ["assetPath"] = NormalizePath(assetPath),
                ["imported"] = needsImport,
                ["errors"] = new JArray(problems.Where(e => LogLevelRank(e.Level) >= 2).Select(e => e.Message)),
                ["warnings"] = new JArray(problems.Where(e => LogLevelRank(e.Level) == 1).Select(e => e.Message)),
                ["compileLog"] = new JArray(compileLog.TakeLast(20)),
            };
            if (!compiled)
            {
                result["error"] = problems.Count > 0
                    ? "Shader failed to compile. See 'errors'."
                    : "Shader is in a failed state but produced no new compiler output. Change the source and compile again, or see get_shader_errors for the previous failure.";
            }
            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Queues the import or reimport of a shader source. Returns an error message or null.
        /// Must run on the main thread.
        /// </summary>
        private static string StartShaderImport(string sourcePath, string assetPath)
        {
            var database = Editor.Instance.ContentDatabase;
            var folderPath = Path.GetDirectoryName(assetPath);
            Directory.CreateDirectory(folderPath);
            if (!(database.Find(folderPath) is ContentFolder folder))
            {
                database.RefreshFolder(database.Game.Content.Folder, true);
                folder = database.Find(folderPath) as ContentFolder;
            }
            if (folder == null)
                return $"Content folder not found: {folderPath}";

            // Importing again overwrites the existing asset. ContentImporting.Reimport is not
            // used because shader assets store no import path, so it silently does nothing.
            Editor.Instance.ContentImporting.Import(sourcePath, folder, true);
            return null;
        }

        private static bool MentionsShader(string text, string name)
        {
            return text != null && (text.Contains($"'{name}'") || text.Contains($"/{name}.flax") || text.Contains($"\\{name}.flax") || text.Contains($"{name}.shader"));
        }
    }
}

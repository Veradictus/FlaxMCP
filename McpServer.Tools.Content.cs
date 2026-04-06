using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEditor;
using FlaxEditor.Content;
using FlaxEngine;

namespace FlaxMCP
{
    /// <summary>
    /// Content and asset management MCP tool handlers. Covers listing, importing,
    /// searching, inspecting, reimporting, and managing content items (files and
    /// folders) within the Flax project content database. Also includes the
    /// <see cref="ResolvePath"/> helper used to convert relative paths to absolute.
    /// </summary>
    public partial class McpServer
    {
        // ==================================================================
        // TOOL HANDLERS: Assets & Content
        // ==================================================================

        /// <summary>
        /// Resolves a relative content path to an absolute project path.
        /// </summary>
        private string ResolvePath(string path)
        {
            if (!Path.IsPathRooted(path))
                return Path.Combine(Globals.ProjectFolder, path);
            return path;
        }

        /// <summary>
        /// Lists assets in a content folder, optionally filtered by file extension.
        /// </summary>
        private string ToolListAssets(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path", "Content");
            var typeFilter = GetArgString(args, "type");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(ResolvePath(path));
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
                            "path", NormalizePath(childFolder.Path)
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
                            "path", NormalizePath(child.Path),
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

        /// <summary>
        /// Imports external files into the project content folder.
        /// </summary>
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
                var folder = Editor.Instance.ContentDatabase.Find(ResolvePath(target)) as ContentFolder;
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

        /// <summary>
        /// Finds a content item by its path in the content database.
        /// </summary>
        private string ToolFindContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(ResolvePath(path));
                if (item == null)
                    return BuildJsonObject("error", $"Content item not found: {path}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": {JsonEscape(item.ShortName)},");
                sb.AppendLine($"  \"type\": {JsonEscape(item.GetType().Name)},");
                sb.AppendLine($"  \"path\": {JsonEscapePath(item.Path)},");
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

        /// <summary>
        /// Searches the content database by name and/or asset type.
        /// </summary>
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

        /// <summary>
        /// Gets detailed information about a content item including file size and modification time.
        /// </summary>
        private string ToolGetContentInfo(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(ResolvePath(path));
                if (item == null)
                    return BuildJsonObject("error", $"Content item not found: {path}");

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"name\": {JsonEscape(item.ShortName)},");
                sb.AppendLine($"  \"type\": {JsonEscape(item.GetType().Name)},");
                sb.AppendLine($"  \"path\": {JsonEscapePath(item.Path)},");
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

        /// <summary>
        /// Reimports an asset from its original source file.
        /// </summary>
        private string ToolReimportContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                var item = Editor.Instance.ContentDatabase.Find(ResolvePath(path));
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

        /// <summary>
        /// Gets the content directory tree structure starting from a folder.
        /// </summary>
        private string ToolGetContentTree(Dictionary<string, object> args)
        {
            var maxDepth = GetArgInt(args, "maxDepth", 2);
            var startPath = GetArgString(args, "path");

            return InvokeOnMainThread(() =>
            {
                ContentFolder startFolder;

                if (!string.IsNullOrEmpty(startPath))
                {
                    var item = Editor.Instance.ContentDatabase.Find(ResolvePath(startPath));
                    startFolder = item as ContentFolder;
                    if (startFolder == null)
                        return BuildJsonObject("error", $"Folder not found: {startPath}");
                }
                else
                {
                    startFolder = Editor.Instance.ContentDatabase.Game.Content.Folder;
                    if (startFolder == null)
                        return BuildJsonObject("error", "Content database not available.");
                }

                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.Append("  \"tree\": ");
                CollectContentTree(startFolder, sb, 1, maxDepth);
                sb.AppendLine();
                sb.Append("}");
                return sb.ToString();
            });
        }

        // ==================================================================
        // TOOL HANDLERS: Content Operations (Extended)
        // ==================================================================

        /// <summary>
        /// Creates a content folder at the specified path.
        /// </summary>
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
                        ? Editor.Instance.ContentDatabase.Find(ResolvePath(parentPath))
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

        /// <summary>
        /// Deletes a content item (file or folder). Use with caution.
        /// </summary>
        private string ToolDeleteContent(Dictionary<string, object> args)
        {
            var path = GetArgString(args, "path");
            if (string.IsNullOrEmpty(path))
                return BuildJsonObject("error", "Missing 'path' argument.");

            return InvokeOnMainThread(() =>
            {
                try
                {
                    var item = Editor.Instance.ContentDatabase.Find(ResolvePath(path));
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

        /// <summary>
        /// Moves or renames a content item from one path to another.
        /// </summary>
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
        // TOOL HANDLERS: Import Status
        // ==================================================================

        /// <summary>
        /// Returns the current state of the content import queue.
        /// </summary>
        private string ToolGetImportStatus(Dictionary<string, object> args)
        {
            return InvokeOnMainThread(() =>
            {
                var importing = Editor.Instance.ContentImporting;
                return BuildJsonObject(
                    "isImporting", importing.IsImporting.ToString().ToLowerInvariant(),
                    "batchSize", importing.ImportBatchSize.ToString(),
                    "progress", importing.ImportingProgress.ToString("F2")
                );
            });
        }
    }
}

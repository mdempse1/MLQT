using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Editing tools that change the source of loaded classes: replace one class's body in place
/// (update_class_source) or rename a class and every reference to it (rename_class). Both write the
/// affected .mo file(s), reload them, and (when analysis has run) refresh the dependency graph.
/// rename_class is precise — it rewrites only the exact identifier tokens that resolve to the class
/// (via the shared reference locator), not textual matches.
/// </summary>
[McpServerToolType]
public sealed class EditTools
{
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public EditTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "update_class_source")]
    [Description("Replace the Modelica source of a single loaded class with new source, then write the " +
                "file to disk and refresh the graph (so check_class / spell_check / get_class_source / the " +
                "dependency tools see the change). new_source must be ONE complete, syntactically valid " +
                "class definition (e.g. 'model X ... end X;'); it is written verbatim — NOT reformatted, so " +
                "run format_class afterwards if you want. The class name must stay the same — to rename or " +
                "move a class (rewriting references too) use rename_class / move_class. Set preview=true to " +
                "get the resulting file text without writing.")]
    public async Task<object> UpdateClassSource(
        [Description("Fully-qualified class id to replace, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("The new Modelica source: exactly one complete class definition, with the same class name.")]
        string newSource,
        [Description("Return the resulting file text without writing to disk or updating the graph. Default false.")]
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(newSource))
            return new ToolError("new_source must be a non-empty, complete Modelica class definition.");

        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse; its source range is unknown and cannot be updated.");

        var (models, errors) = ModelicaParserHelper.ExtractModelsWithErrors(newSource);
        if (errors.Count > 0)
            return new ToolError(
                $"new_source has {errors.Count} syntax error(s): {DescribeErrors(errors)}. Provide one " +
                "complete, valid class definition (e.g. 'model X ... end X;').");

        var topLevel = models.Where(m => !m.IsNested).ToList();
        if (topLevel.Count != 1)
            return new ToolError(
                $"new_source must define exactly ONE top-level class; found {topLevel.Count}. Update one class at a time.");
        if (!string.Equals(topLevel[0].Name, node.Name, StringComparison.Ordinal))
            return new ToolError(
                $"new_source renames the class from '{node.Name}' to '{topLevel[0].Name}'. update_class_source " +
                "only replaces a class's body in place — to rename it (updating references too) use " +
                "rename_class. Keep the class name the same here.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        var owner = ctx.FileOwner;
        var ownerCode = owner.Definition.ModelicaCode ?? string.Empty;

        string newOwnerCode;
        if (node.Id == owner.Id)
        {
            newOwnerCode = newSource;
        }
        else
        {
            var oldClassCode = node.Definition.ModelicaCode ?? string.Empty;
            if (CountOccurrences(ownerCode, oldClassCode) != 1)
                return new ToolError(
                    "Could not uniquely locate the class within its file (its cached source may be stale). " +
                    "Reload the library and retry.");
            newOwnerCode = ReplaceFirst(ownerCode, oldClassCode, newSource);
        }

        var fileContent = PrependWithinClause(newOwnerCode, owner.ParentModelName);

        if (preview)
            return new UpdateClassSourceResult(classId, ctx.FilePath, PreviewOnly: true, Changed: false, 0, fileContent);

        if (FileWritability.RequireWritable(ctx.FilePath, "update this class") is { } readOnly)
            return readOnly;

        await File.WriteAllTextAsync(ctx.FilePath, fileContent);
        var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new UpdateClassSourceResult(classId, ctx.FilePath, PreviewOnly: false, Changed: true, affected.Count, null);
    }

    [McpServerTool(Name = "create_class")]
    [Description("Create a new class inside a loaded parent class/package from complete Modelica source, " +
                "place it on disk, and load it. Provide just the class definition (no 'within' clause). " +
                "Storage: for a directory package parent, a standalone class is written as its own .mo file " +
                "in the package directory (and added to package.order); otherwise it is nested into the " +
                "parent's package.mo. Pass standalone=true/false to force, or omit to choose automatically " +
                "(standalone when the parent is a directory package and the class has no replaceable/" +
                "redeclare/inner/outer prefix). Fails if the class already exists or the source has syntax " +
                "errors; writes are refused on read-only files. Set preview=true to see the file that would " +
                "be written.")]
    public async Task<object> CreateClass(
        [Description("Fully-qualified id of the parent package/class to create the class inside.")]
        string parentId,
        [Description("The new class's complete Modelica source (one class definition, no 'within' clause).")]
        string source,
        [Description("Force standalone (true) or nested (false) storage. Omit to choose automatically.")]
        bool? standalone = null,
        [Description("Return the file text that would be written without creating anything. Default false.")]
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new ToolError("source must be a complete Modelica class definition.");
        if (source.TrimStart().StartsWith("within", StringComparison.Ordinal))
            return new ToolError("Provide just the class definition, without a 'within' clause (the parent is given by parent_id).");

        var (models, errors) = ModelicaParserHelper.ExtractModelsWithErrors(source);
        if (errors.Count > 0)
            return new ToolError($"source has {errors.Count} syntax error(s): {DescribeErrors(errors)}. Provide one complete, valid class.");
        var topLevel = models.Where(m => !m.IsNested).ToList();
        if (topLevel.Count != 1)
            return new ToolError($"source must define exactly ONE top-level class; found {topLevel.Count}.");

        var className = topLevel[0].Name;
        if (string.IsNullOrEmpty(className) || !IdentifierRegex.IsMatch(className))
            return new ToolError($"The class name '{className}' is not a valid Modelica identifier.");

        var parent = _libraries.GetModelById(parentId);
        if (parent is null)
            return ToolDiagnostics.ClassNotFound(_libraries, parentId);

        var newId = $"{parentId}.{className}";
        if (_libraries.GetModelById(newId) is not null)
            return new ToolError($"A class '{newId}' already exists. Use update_class_source to change it, or choose another name.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, parentId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for parent '{parentId}'.");

        var parentIsDirectoryPackage =
            ctx.FileOwner.Id == parentId &&
            string.Equals(Path.GetFileName(ctx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase);
        var standaloneAble = !StartsWithElementPrefix(source);

        bool useStandalone;
        if (standalone == true)
        {
            if (!parentIsDirectoryPackage)
                return new ToolError($"Parent '{parentId}' is not a directory package, so a standalone file cannot be created under it. Use standalone=false to nest it, or omit standalone.");
            if (!standaloneAble)
                return new ToolError("A class with a replaceable/redeclare/inner/outer prefix cannot be stored standalone; use standalone=false.");
            useStandalone = true;
        }
        else if (standalone == false)
        {
            useStandalone = false;
        }
        else
        {
            useStandalone = parentIsDirectoryPackage && standaloneAble;
        }

        return useStandalone
            ? await CreateStandaloneAsync(newId, className, source, ctx.FilePath, preview)
            : await CreateNestedAsync(newId, className, source, parent, ctx, parentIsDirectoryPackage, preview);
    }

    private async Task<object> CreateStandaloneAsync(
        string newId, string className, string source, string packageMoPath, bool preview)
    {
        var dir = Path.GetDirectoryName(packageMoPath)!;
        var parentId = newId[..newId.LastIndexOf('.')];
        var newFilePath = Path.Combine(dir, className + ".mo");
        if (File.Exists(newFilePath))
            return new ToolError($"A file already exists at '{newFilePath}'.");

        var content = $"within {parentId};\n{source.TrimEnd()}\n";
        if (preview)
            return new CreateClassResult(newId, newFilePath, "standalone", PreviewOnly: true, Created: false, content);

        if (FileWritability.PreflightWritable(new[] { newFilePath }, $"create class '{newId}'") is { } readOnly)
            return readOnly;

        await File.WriteAllTextAsync(newFilePath, content);
        AppendToPackageOrder(dir, className);
        var affected = await _libraries.ReloadFileAsync(newFilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);
        return new CreateClassResult(newId, newFilePath, "standalone", PreviewOnly: false, Created: true, null);
    }

    private async Task<object> CreateNestedAsync(
        string newId, string className, string source, ModelNode parent,
        ModelFilePersistence.FileOwnerContext ctx, bool parentIsDirectoryPackage, bool preview)
    {
        var parentCode = parent.Definition.ModelicaCode ?? string.Empty;
        var inserted = InsertNestedClass(parentCode, parent.Name, source);
        if (inserted is null)
            return new ToolError($"Could not find the end of parent class '{parent.Id}' to insert into.");

        string newOwnerCode;
        if (parent.Id == ctx.FileOwner.Id)
        {
            newOwnerCode = inserted;
        }
        else
        {
            var ownerCode = ctx.FileOwner.Definition.ModelicaCode ?? string.Empty;
            if (CountOccurrences(ownerCode, parentCode) != 1)
                return new ToolError("Could not uniquely locate the parent within its file (cached source may be stale). Reload the library and retry.");
            newOwnerCode = ReplaceFirst(ownerCode, parentCode, inserted);
        }

        var fileContent = PrependWithinClause(newOwnerCode, ctx.FileOwner.ParentModelName);

        var (_, errs) = ModelicaParserHelper.ParseWithErrors(fileContent);
        if (errs.Count > 0)
            return new ToolError($"Inserting the class would make '{ctx.FilePath}' unparseable ({DescribeErrors(errs)}). Nothing was written.");

        if (preview)
            return new CreateClassResult(newId, ctx.FilePath, "nested", PreviewOnly: true, Created: false, fileContent);

        if (FileWritability.RequireWritable(ctx.FilePath, $"create class '{newId}'") is { } readOnly)
            return readOnly;

        await File.WriteAllTextAsync(ctx.FilePath, fileContent);
        if (parentIsDirectoryPackage)
            AppendToPackageOrder(Path.GetDirectoryName(ctx.FilePath)!, className);
        var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);
        return new CreateClassResult(newId, ctx.FilePath, "nested", PreviewOnly: false, Created: true, null);
    }

    [McpServerTool(Name = "delete_class")]
    [Description("Delete a loaded class and its .mo storage: a standalone class's file is removed (and its " +
                "package.order entry), a nested class is cut out of its containing package.mo, and a " +
                "directory package's whole folder is deleted recursively. If analyze_dependencies has run, " +
                "classes that still reference the deleted class (or, for a package, any of its members) are " +
                "reported as dangling references (they are NOT auto-updated — fix or remove them). Writes " +
                "are refused on read-only files. Set preview=true to see what would be deleted and what " +
                "would dangle — recommended for a package.")]
    public async Task<object> DeleteClass(
        [Description("Fully-qualified id of the class to delete.")] string classId,
        [Description("Report what would be deleted (and what would dangle) without deleting. Default false.")]
        bool preview = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        var isFileOwner = ctx.FileOwner.Id == classId;
        if (isFileOwner && string.Equals(Path.GetFileName(ctx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
            return await DeleteDirectoryPackageAsync(classId, node, ctx, preview);

        var graph = _libraries.CombinedGraph;
        var depsChecked = _session.DependenciesAnalyzed;
        var dangling = depsChecked
            ? graph.GetModelUsedBy(classId).Select(m => m.Id).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : new List<string>();
        var note = depsChecked
            ? (dangling.Count > 0
                ? $"{dangling.Count} class(es) still reference '{classId}' and will not resolve after deletion — update or remove them."
                : null)
            : "Dependencies were not analyzed, so references to this class were not checked. Run analyze_dependencies first to see what would break.";

        string storage;
        string? newOwnerContent = null;
        if (isFileOwner)
        {
            storage = "standalone-file";
        }
        else
        {
            storage = "nested";
            var ownerCode = ctx.FileOwner.Definition.ModelicaCode ?? string.Empty;
            var classCode = node.Definition.ModelicaCode ?? string.Empty;
            if (string.IsNullOrEmpty(classCode) || CountOccurrences(ownerCode, classCode) != 1)
                return new ToolError("Could not uniquely locate the class within its file (cached source may be stale). Reload the library and retry.");
            var content = PrependWithinClause(CollapseBlankLines(ReplaceFirst(ownerCode, classCode, "")), ctx.FileOwner.ParentModelName);
            var (_, errs) = ModelicaParserHelper.ParseWithErrors(content);
            if (errs.Count > 0)
                return new ToolError($"Removing the class would make '{ctx.FilePath}' unparseable ({DescribeErrors(errs)}). Nothing was deleted.");
            newOwnerContent = content;
        }

        if (preview)
            return new DeleteClassResult(classId, ctx.FilePath, storage, PreviewOnly: true, Deleted: false, depsChecked, dangling, note);

        if (FileWritability.RequireWritable(ctx.FilePath, $"delete class '{classId}'") is { } readOnly)
            return readOnly;

        List<string> affected;
        if (isFileOwner)
        {
            File.Delete(ctx.FilePath);
            RemoveFromPackageOrder(Path.GetDirectoryName(ctx.FilePath)!, node.Name);
            affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        }
        else
        {
            await File.WriteAllTextAsync(ctx.FilePath, newOwnerContent!);
            if (string.Equals(Path.GetFileName(ctx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
                RemoveFromPackageOrder(Path.GetDirectoryName(ctx.FilePath)!, node.Name);
            affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        }
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new DeleteClassResult(classId, ctx.FilePath, storage, PreviewOnly: false, Deleted: true, depsChecked, dangling, note);
    }

    [McpServerTool(Name = "move_class")]
    [Description("Move a class to a different parent package (keeping its simple name), relocating its .mo " +
                "storage and re-qualifying references to it (and its nested classes) across the loaded " +
                "files. Requires analyze_dependencies. The class is placed under the target the same way " +
                "create_class chooses (standalone file in a directory package, else nested). References that " +
                "resolve to the class are rewritten to the new fully-qualified name. A whole directory " +
                "package can also be moved (into another directory package): its folder is relocated and " +
                "its subtree re-qualified. LIMITATION (single class): the moved class's OWN references to its " +
                "former siblings are not re-qualified — any that no longer resolve are reported in " +
                "brokenReferencesInMovedClass. Writes are refused on read-only files. Prefer preview=true first.")]
    public async Task<object> MoveClass(
        [Description("Fully-qualified id of the class to move.")] string classId,
        [Description("Fully-qualified id of the destination parent package.")] string newParentId,
        [Description("Report the plan without changing anything. Default false.")] bool preview = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be moved.");
        if (_libraries.GetModelById(newParentId) is null)
            return ToolDiagnostics.ClassNotFound(_libraries, newParentId);

        var oldLeaf = node.Name;
        if (string.Equals(newParentId, node.ParentModelName, StringComparison.Ordinal))
            return new ToolError($"'{classId}' is already a child of '{newParentId}'.");
        if (string.Equals(newParentId, classId, StringComparison.Ordinal) ||
            newParentId.StartsWith(classId + ".", StringComparison.Ordinal))
            return new ToolError("A class cannot be moved into itself or one of its own descendants.");

        var newId = $"{newParentId}.{oldLeaf}";
        if (_libraries.GetModelById(newId) is not null)
            return new ToolError($"A class '{newId}' already exists at the destination. Choose a different parent or rename first.");

        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "moving a class (references are re-qualified via the dependency graph)");

        var graph = _libraries.CombinedGraph;
        var srcCtx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        var tgtCtx = ModelFilePersistence.ResolveFileOwner(_libraries, newParentId);
        if (srcCtx is null || tgtCtx is null)
            return new ToolError("Could not locate the source or destination file.");
        if (srcCtx.FileOwner.Id == classId &&
            string.Equals(Path.GetFileName(srcCtx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
            return await MoveDirectoryPackageAsync(classId, node, newParentId, newId, srcCtx, preview);

        // Old -> new id map for the class and all its descendants (their ids all change with the move).
        var descendants = _libraries.GetAllModels().Select(m => m.Id)
            .Where(id => id == classId || id.StartsWith(classId + ".", StringComparison.Ordinal))
            .ToList();
        var targetSet = new HashSet<string>(descendants, StringComparer.Ordinal);
        string MapId(string oldId) => newId + oldId[classId.Length..];

        // Files whose references to the class (or its descendants) must be re-qualified.
        var fileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in descendants)
            foreach (var dep in graph.GetModelUsedBy(d))
                if (dep.ContainingFileId is not null)
                    fileIds.Add(dep.ContainingFileId);
        if (node.ContainingFileId is not null) fileIds.Add(node.ContainingFileId);
        if (tgtCtx.FileOwner.ContainingFileId is not null) fileIds.Add(tgtCtx.FileOwner.ContainingFileId);

        var refPaths = fileIds
            .Select(id => graph.GetNode<FileNode>(id)?.FilePath)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        // Plan the re-qualification (replace each reference's whole dotted name with the mapped new id).
        var requalified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requalCount = 0;
        foreach (var path in refPaths)
        {
            var text = await File.ReadAllTextAsync(path);
            var (tree, _) = ModelicaParserHelper.ParseWithErrors(text);
            var locator = new ReferenceLocator(graph, targetSet);
            locator.Visit(tree);
            if (locator.Sites.Count == 0)
                continue;

            var edits = locator.Sites.Select(s => (s.StartIndex, s.StopIndex, MapId(s.TargetId))).ToList();
            requalified[path] = ApplyReplacements(text, edits);
            requalCount += edits.Count;
        }

        var allWritePaths = new HashSet<string>(requalified.Keys, StringComparer.OrdinalIgnoreCase)
        {
            srcCtx.FilePath, tgtCtx.FilePath
        };

        if (preview)
        {
            var pv = $"Would move '{classId}' -> '{newId}', re-qualifying {requalCount} reference(s) across " +
                     $"{requalified.Count} file(s). References inside the moved class to former siblings are " +
                     "not re-qualified and will be reported after the move.";
            return new MoveClassResult(classId, newId, srcCtx.FileOwner.Id == classId ? "standalone" : "nested",
                PreviewOnly: true, Moved: false, requalCount, requalified.Count, Array.Empty<string>(), pv);
        }

        if (FileWritability.PreflightWritable(allWritePaths, $"move '{classId}'") is { } readOnly)
            return readOnly;

        // 1) Write + reload the re-qualified referencing files (the class itself still exists as oldId).
        var touched = new List<string>();
        foreach (var (path, content) in requalified)
        {
            await File.WriteAllTextAsync(path, content);
            touched.AddRange(await _libraries.ReloadFileAsync(path));
        }

        // 2) Relocate storage: take the (now re-qualified) class code, remove it from the source, and
        //    place it under the new parent.
        var moved = _libraries.GetModelById(classId) ?? node;
        var classCode = moved.Definition.ModelicaCode ?? string.Empty;
        var srcCtx2 = ModelFilePersistence.ResolveFileOwner(_libraries, classId) ?? srcCtx;

        var removeResult = await RemoveClassStorageAsync(moved, srcCtx2);
        if (removeResult is ToolError removeErr)
            return removeErr;
        touched.AddRange((List<string>)removeResult);

        var tgtCtx2 = ModelFilePersistence.ResolveFileOwner(_libraries, newParentId) ?? tgtCtx;
        var addResult = await AddClassStorageAsync(newParentId, oldLeaf, classCode, tgtCtx2);
        if (addResult is ToolError addErr)
            return addErr;
        touched.AddRange((List<string>)addResult);

        await GraphRefresh.RefreshAfterEditAsync(touched, _libraries, _resources, _session);

        var brokenAfter = ProbeUnresolvedReferences(_libraries.GetModelById(newId));
        var storageKind = srcCtx.FileOwner.Id == classId ? "standalone" : "nested";
        var note = brokenAfter.Count > 0
            ? $"Moved. {brokenAfter.Count} reference(s) inside the moved class no longer resolve (they pointed " +
              "to former siblings) — re-qualify them manually. Verify with a model checker."
            : "Moved and references re-qualified. Verify with a model checker.";
        return new MoveClassResult(classId, newId, storageKind, PreviewOnly: false, Moved: true,
            requalCount, allWritePaths.Count, brokenAfter, note);
    }

    // Move a whole directory package under a new parent (a directory package): re-qualify references to
    // its subtree, rewrite the moved files' within clauses, relocate the folder, and update both
    // package.orders. The leaf name is unchanged, so no declaration rename.
    private async Task<object> MoveDirectoryPackageAsync(
        string classId, ModelNode node, string newParentId, string newId,
        ModelFilePersistence.FileOwnerContext ctx, bool preview)
    {
        var oldLeaf = node.Name;
        var oldParent = node.ParentModelName;
        if (string.IsNullOrEmpty(oldParent))
            return new ToolError("Moving a top-level package is not supported. Rename it or restructure manually.");

        var tgtCtx = ModelFilePersistence.ResolveFileOwner(_libraries, newParentId);
        if (tgtCtx is null || tgtCtx.FileOwner.Id != newParentId ||
            !string.Equals(Path.GetFileName(tgtCtx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
            return new ToolError($"Destination '{newParentId}' must be a directory package to hold a moved package directory.");

        var graph = _libraries.CombinedGraph;
        var dir = Path.GetDirectoryName(ctx.FilePath)!;
        var oldParentDir = Path.GetDirectoryName(dir)!;
        var newParentDir = Path.GetDirectoryName(tgtCtx.FilePath)!;
        var newDir = Path.Combine(newParentDir, oldLeaf);
        if (Directory.Exists(newDir))
            return new ToolError($"A directory '{newDir}' already exists at the destination.");

        var descendants = Descendants(classId);
        var targetSet = new HashSet<string>(descendants, StringComparer.Ordinal);
        string MapId(string oldId) => newId + oldId[classId.Length..];
        var subtreeFiles = new HashSet<string>(SubtreeFilePaths(descendants), StringComparer.OrdinalIgnoreCase);

        var refPaths = new HashSet<string>(subtreeFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var d in descendants)
            foreach (var dep in graph.GetModelUsedBy(d))
            {
                var p = dep.ContainingFileId is null ? null : graph.GetNode<FileNode>(dep.ContainingFileId)?.FilePath;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    refPaths.Add(p);
            }

        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requalCount = 0;
        foreach (var path in refPaths)
        {
            var text = await File.ReadAllTextAsync(path);
            var (tree, _) = ModelicaParserHelper.ParseWithErrors(text);
            var locator = new ReferenceLocator(graph, targetSet);
            locator.Visit(tree);
            if (locator.Sites.Count > 0)
            {
                var edits = locator.Sites.Select(s => (s.StartIndex, s.StopIndex, MapId(s.TargetId))).ToList();
                text = ApplyReplacements(text, edits);
                requalCount += edits.Count;
            }
            // Rewriting the oldParent prefix fixes both the package.mo's own 'within oldParent;' and every
            // descendant's 'within oldParent.leaf...;' (which becomes newParent.leaf... = newId...).
            if (subtreeFiles.Contains(path))
                text = RewriteWithinPrefix(text, oldParent, newParentId);
            changed[path] = text;
        }

        foreach (var (path, content) in changed)
        {
            var (_, errs) = ModelicaParserHelper.ParseWithErrors(content);
            if (errs.Count > 0)
                return new ToolError($"Moving would leave '{path}' unparseable ({DescribeErrors(errs)}). Nothing was changed.");
        }

        var note = $"Moved package directory '{oldLeaf}' from '{oldParent}' to '{newParentId}', re-qualifying " +
                   $"{requalCount} reference(s). Verify with a model checker.";
        if (preview)
            return new MoveClassResult(classId, newId, "directory-package", PreviewOnly: true, Moved: false,
                requalCount, changed.Count, Array.Empty<string>(), note);

        if (FileWritability.PreflightWritable(changed.Keys, $"move package '{classId}'") is { } readOnly)
            return readOnly;

        Directory.Move(dir, newDir);

        var oldPaths = new List<string>();
        var newPaths = new List<string>();
        foreach (var (path, content) in changed)
        {
            var target = subtreeFiles.Contains(path) ? newDir + path[dir.Length..] : path;
            await File.WriteAllTextAsync(target, content);
            if (subtreeFiles.Contains(path)) { oldPaths.Add(path); newPaths.Add(target); }
            else newPaths.Add(path);
        }

        RemoveFromPackageOrder(oldParentDir, oldLeaf);
        AppendToPackageOrder(newParentDir, oldLeaf);

        var affected = new List<string>();
        foreach (var p in oldPaths) affected.AddRange(await _libraries.ReloadFileAsync(p));
        foreach (var p in newPaths) affected.AddRange(await _libraries.ReloadFileAsync(p));
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new MoveClassResult(classId, newId, "directory-package", PreviewOnly: false, Moved: true,
            requalCount, changed.Count, Array.Empty<string>(), note);
    }

    // Rename a whole directory package: re-qualify references to it and its subtree, rewrite the descendant
    // files' within clauses and the package's declaration, rename the folder, and update package.order.
    private async Task<object> RenameDirectoryPackageAsync(
        string classId, string newId, string oldLeaf, string newLeaf,
        ModelFilePersistence.FileOwnerContext ctx, bool preview)
    {
        var graph = _libraries.CombinedGraph;
        var dir = Path.GetDirectoryName(ctx.FilePath)!;
        var parentDir = Path.GetDirectoryName(dir)!;
        var newDir = Path.Combine(parentDir, newLeaf);
        if (Directory.Exists(newDir))
            return new ToolError($"A directory '{newDir}' already exists. Choose a different name.");

        var descendants = Descendants(classId);
        var targetSet = new HashSet<string>(descendants, StringComparer.Ordinal);
        string MapId(string oldId) => newId + oldId[classId.Length..];
        var subtreeFiles = new HashSet<string>(SubtreeFilePaths(descendants), StringComparer.OrdinalIgnoreCase);

        // Every file that references the subtree (external + internal), by file id -> path.
        var refPaths = new HashSet<string>(subtreeFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var d in descendants)
            foreach (var dep in graph.GetModelUsedBy(d))
            {
                var p = dep.ContainingFileId is null ? null : graph.GetNode<FileNode>(dep.ContainingFileId)?.FilePath;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    refPaths.Add(p);
            }

        // Build the new content of every changed file: re-qualify references, then (for subtree files)
        // rewrite the within clause, and for the package's own package.mo rename the declaration.
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requalCount = 0;
        foreach (var path in refPaths)
        {
            var text = await File.ReadAllTextAsync(path);
            var (tree, _) = ModelicaParserHelper.ParseWithErrors(text);
            var locator = new ReferenceLocator(graph, targetSet);
            locator.Visit(tree);
            if (locator.Sites.Count > 0)
            {
                var edits = locator.Sites.Select(s => (s.StartIndex, s.StopIndex, MapId(s.TargetId))).ToList();
                text = ApplyReplacements(text, edits);
                requalCount += edits.Count;
            }

            if (subtreeFiles.Contains(path))
            {
                text = RewriteWithinPrefix(text, classId, newId);
                if (string.Equals(path, ctx.FilePath, StringComparison.OrdinalIgnoreCase))
                    text = RenameDefinitionTokens(text, classId, newLeaf, graph);
            }

            changed[path] = text;
        }

        // Parse-check every result before touching disk.
        foreach (var (path, content) in changed)
        {
            var (_, errs) = ModelicaParserHelper.ParseWithErrors(content);
            if (errs.Count > 0)
                return new ToolError($"Renaming would leave '{path}' unparseable ({DescribeErrors(errs)}). Nothing was changed.");
        }

        var note = $"Renamed the package directory '{Path.GetFileName(dir)}' -> '{newLeaf}' and re-qualified " +
                   $"{requalCount} reference(s). Verify with a model checker.";
        if (preview)
        {
            var previews = changed.Select(kv => new RenameFileChange(kv.Key, 0, kv.Value)).ToList();
            return new RenameClassResult(classId, newId, PreviewOnly: true, Changed: false, changed.Count, requalCount, previews, note);
        }

        if (FileWritability.PreflightWritable(changed.Keys, $"rename package '{classId}'") is { } readOnly)
            return readOnly;

        Directory.Move(dir, newDir);

        // Write each file's new content (subtree files now live under newDir).
        var oldPaths = new List<string>();
        var newPaths = new List<string>();
        foreach (var (path, content) in changed)
        {
            var target = subtreeFiles.Contains(path) ? newDir + path[dir.Length..] : path;
            await File.WriteAllTextAsync(target, content);
            if (subtreeFiles.Contains(path)) { oldPaths.Add(path); newPaths.Add(target); }
            else newPaths.Add(path);
        }

        RenameInPackageOrder(parentDir, oldLeaf, newLeaf);

        var affected = new List<string>();
        foreach (var p in oldPaths) affected.AddRange(await _libraries.ReloadFileAsync(p)); // gone -> remove old ids
        foreach (var p in newPaths) affected.AddRange(await _libraries.ReloadFileAsync(p)); // add/update
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        var changes = changed.Keys.Select(p => new RenameFileChange(p, 0, null)).ToList();
        return new RenameClassResult(classId, newId, PreviewOnly: false, Changed: true, changed.Count, requalCount, changes, note);
    }

    // Rewrite the leading 'within <name>;' clause: if <name> is oldPrefix or under it, swap that prefix.
    private static string RewriteWithinPrefix(string text, string oldPrefix, string newPrefix)
    {
        var m = Regex.Match(text, @"within\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;");
        if (!m.Success)
            return text;
        var name = m.Groups[1].Value;
        if (name != oldPrefix && !name.StartsWith(oldPrefix + ".", StringComparison.Ordinal))
            return text;
        var replacement = newPrefix + name[oldPrefix.Length..];
        var g = m.Groups[1];
        return text[..g.Index] + replacement + text[(g.Index + name.Length)..];
    }

    // Rename a class's own declaration name tokens (package X ... end X) to newLeaf.
    private static string RenameDefinitionTokens(string text, string classId, string newLeaf, DirectedGraph graph)
    {
        var (tree, _) = ModelicaParserHelper.ParseWithErrors(text);
        var locator = new ReferenceLocator(graph, new[] { classId });
        locator.Visit(tree);
        var def = locator.Definitions.FirstOrDefault(d => d.Id == classId);
        if (def is null)
            return text;
        var spans = def.NameTokens.Select(t => (t.StartIndex, t.StopIndex)).ToList();
        return ApplySpans(text, spans, newLeaf);
    }

    private static void RenameInPackageOrder(string directory, string oldName, string newName)
    {
        var path = Path.Combine(directory, "package.order");
        if (!File.Exists(path))
            return;
        var lines = File.ReadAllLines(path, Encoding.Latin1)
            .Select(l => string.Equals(l.Trim(), oldName, StringComparison.Ordinal) ? newName : l)
            .ToList();
        File.WriteAllLines(path, lines, Encoding.Latin1);
    }

    // Delete a whole directory package: its directory (recursively), its parent's package.order entry, and
    // all its models from the graph. Reports references from OUTSIDE the subtree as dangling.
    private async Task<object> DeleteDirectoryPackageAsync(
        string classId, ModelNode node, ModelFilePersistence.FileOwnerContext ctx, bool preview)
    {
        var dir = Path.GetDirectoryName(ctx.FilePath)!;
        var parentDir = Path.GetDirectoryName(dir)!;
        var descendants = Descendants(classId);
        var subtreeFiles = SubtreeFilePaths(descendants);

        var depsChecked = _session.DependenciesAnalyzed;
        var dangling = depsChecked ? ExternalDangling(classId, descendants) : new List<string>();
        var note = depsChecked
            ? (dangling.Count > 0
                ? $"{dangling.Count} class(es) outside '{classId}' still reference it (or its members) and will " +
                  "not resolve after deletion — update or remove them."
                : null)
            : "Dependencies were not analyzed, so external references were not checked. Run analyze_dependencies first.";

        if (preview)
            return new DeleteClassResult(classId, dir, "directory-package", PreviewOnly: true, Deleted: false, depsChecked, dangling, note);

        if (FileWritability.PreflightWritable(subtreeFiles, $"delete package '{classId}'") is { } readOnly)
            return readOnly;

        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not delete the package directory '{dir}': {ex.Message}. Nothing was changed.");
        }

        RemoveFromPackageOrder(parentDir, node.Name);

        var affected = new List<string>();
        foreach (var path in subtreeFiles)
            affected.AddRange(await _libraries.ReloadFileAsync(path)); // file is gone -> its models are removed
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new DeleteClassResult(classId, dir, "directory-package", PreviewOnly: false, Deleted: true, depsChecked, dangling, note);
    }

    // The class and every descendant (its whole subtree).
    private List<string> Descendants(string classId) => _libraries.GetAllModels()
        .Select(m => m.Id)
        .Where(id => id == classId || id.StartsWith(classId + ".", StringComparison.Ordinal))
        .ToList();

    // Distinct source files that hold the subtree's models.
    private List<string> SubtreeFilePaths(IEnumerable<string> ids)
    {
        var graph = _libraries.CombinedGraph;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var fileId = _libraries.GetModelById(id)?.ContainingFileId;
            var path = fileId is null ? null : graph.GetNode<FileNode>(fileId)?.FilePath;
            if (!string.IsNullOrEmpty(path))
                paths.Add(path);
        }
        return paths.ToList();
    }

    // References to any subtree member from OUTSIDE the subtree.
    private List<string> ExternalDangling(string classId, IReadOnlyCollection<string> descendants)
    {
        var graph = _libraries.CombinedGraph;
        var inside = new HashSet<string>(descendants, StringComparer.Ordinal);
        var external = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in descendants)
            foreach (var user in graph.GetModelUsedBy(id))
                if (!inside.Contains(user.Id))
                    external.Add(user.Id);
        return external.ToList();
    }

    // Removes a class from disk (delete its standalone file, or cut it from its containing package.mo).
    // Returns the affected model ids, or a ToolError.
    private async Task<object> RemoveClassStorageAsync(ModelNode node, ModelFilePersistence.FileOwnerContext ctx)
    {
        if (ctx.FileOwner.Id == node.Id)
        {
            File.Delete(ctx.FilePath);
            RemoveFromPackageOrder(Path.GetDirectoryName(ctx.FilePath)!, node.Name);
            return await _libraries.ReloadFileAsync(ctx.FilePath);
        }

        var ownerCode = ctx.FileOwner.Definition.ModelicaCode ?? string.Empty;
        var classCode = node.Definition.ModelicaCode ?? string.Empty;
        if (string.IsNullOrEmpty(classCode) || CountOccurrences(ownerCode, classCode) != 1)
            return new ToolError("Could not uniquely locate the class within its source file to move it.");
        var content = PrependWithinClause(CollapseBlankLines(ReplaceFirst(ownerCode, classCode, "")), ctx.FileOwner.ParentModelName);
        await File.WriteAllTextAsync(ctx.FilePath, content);
        if (string.Equals(Path.GetFileName(ctx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
            RemoveFromPackageOrder(Path.GetDirectoryName(ctx.FilePath)!, node.Name);
        return await _libraries.ReloadFileAsync(ctx.FilePath);
    }

    // Places 'classCode' under newParentId (standalone file if the parent is a directory package and the
    // class allows it, else nested in the parent's package.mo). Returns affected model ids or a ToolError.
    private async Task<object> AddClassStorageAsync(
        string newParentId, string leaf, string classCode, ModelFilePersistence.FileOwnerContext tgtCtx)
    {
        var parentIsDirectoryPackage = tgtCtx.FileOwner.Id == newParentId &&
            string.Equals(Path.GetFileName(tgtCtx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase);
        var useStandalone = parentIsDirectoryPackage && !StartsWithElementPrefix(classCode);

        if (useStandalone)
        {
            var dir = Path.GetDirectoryName(tgtCtx.FilePath)!;
            var newFilePath = Path.Combine(dir, leaf + ".mo");
            await File.WriteAllTextAsync(newFilePath, $"within {newParentId};\n{classCode.TrimEnd()}\n");
            AppendToPackageOrder(dir, leaf);
            return await _libraries.ReloadFileAsync(newFilePath);
        }

        var parentNode = _libraries.GetModelById(newParentId)!;
        var parentCode = parentNode.Definition.ModelicaCode ?? string.Empty;
        var inserted = InsertNestedClass(parentCode, parentNode.Name, classCode);
        if (inserted is null)
            return new ToolError($"Could not find the end of destination '{newParentId}' to insert into.");

        string newOwnerCode;
        if (parentNode.Id == tgtCtx.FileOwner.Id)
        {
            newOwnerCode = inserted;
        }
        else
        {
            var ownerCode = tgtCtx.FileOwner.Definition.ModelicaCode ?? string.Empty;
            if (CountOccurrences(ownerCode, parentCode) != 1)
                return new ToolError("Could not uniquely locate the destination within its file.");
            newOwnerCode = ReplaceFirst(ownerCode, parentCode, inserted);
        }
        await File.WriteAllTextAsync(tgtCtx.FilePath, PrependWithinClause(newOwnerCode, tgtCtx.FileOwner.ParentModelName));
        if (parentIsDirectoryPackage)
            AppendToPackageOrder(Path.GetDirectoryName(tgtCtx.FilePath)!, leaf);
        return await _libraries.ReloadFileAsync(tgtCtx.FilePath);
    }

    // Best-effort: the component/extends types declared directly in a class that do not resolve in the
    // class's current scope. After a move this surfaces references to former siblings that are no longer
    // visible. Uses the same resolution as validate_class_references.
    private List<string> ProbeUnresolvedReferences(ModelNode? classNode)
    {
        var tree = classNode?.Definition.EnsureParsed();
        if (classNode is null || tree is null)
            return new List<string>();

        var iface = ClassInterfaceExtractor.Extract(tree);
        var imports = iface.Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();

        var broken = new List<string>();
        foreach (var e in iface.Elements)
        {
            if (e.Kind is not (ClassElementKind.Component or ClassElementKind.Extends))
                continue;
            var type = e.Type;
            if (string.IsNullOrWhiteSpace(type) || TypeResolver.IsPredefined(type))
                continue;
            var clean = type!.TrimStart('.').Trim();
            if (TypeResolver.Resolve(_libraries, classNode.Id, type, imports) is null && !broken.Contains(clean))
                broken.Add(clean);
        }
        return broken;
    }

    [McpServerTool(Name = "rename_class")]
    [Description("Rename a loaded class AND rewrite every reference to it across the loaded files, then " +
                "write the changed files, reload them, and refresh dependencies. Requires " +
                "analyze_dependencies (the referencing files are found via the dependency graph). This is a " +
                "PRECISE rename: it resolves each reference the same way dependency analysis does and " +
                "rewrites only the exact identifier tokens that refer to this class (the declaration plus " +
                "qualified/relative/imported uses) — NOT textual name matches, so a same-named unrelated " +
                "class is never touched. A whole directory package can be renamed too: its folder is renamed, " +
                "its subtree's ids re-qualified, and package.order updated. Each changed file is re-parsed; " +
                "if any would no longer parse, nothing is written. Set preview=true to see the planned " +
                "per-file changes first. Note: deep member accesses like Pkg.OldName.someConstant are not " +
                "rewritten (consistent with dependency analysis) — review those.")]
    public async Task<object> RenameClass(
        [Description("Fully-qualified id of the class to rename, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("The new simple (leaf) class name, e.g. 'MyIntegrator'. Must be a valid Modelica identifier.")]
        string newName,
        [Description("Return the planned per-file changes (with new content) without writing. Default false.")]
        bool preview = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be renamed.");
        if (string.IsNullOrWhiteSpace(newName) || !IdentifierRegex.IsMatch(newName))
            return new ToolError($"newName '{newName}' is not a valid Modelica identifier (letters/digits/_, not starting with a digit).");

        var oldLeaf = node.Name;
        if (string.Equals(newName, oldLeaf, StringComparison.Ordinal))
            return new ToolError("newName is the same as the current name.");

        var parent = node.ParentModelName;
        var newId = string.IsNullOrEmpty(parent) ? newName : $"{parent}.{newName}";
        if (_libraries.GetModelById(newId) is not null)
            return new ToolError($"A class named '{newId}' already exists. Choose a different name.");

        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries,
                "renaming a class (referencing files are located via the dependency graph)");

        var dirCtx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (dirCtx is not null && dirCtx.FileOwner.Id == classId &&
            string.Equals(Path.GetFileName(dirCtx.FilePath), "package.mo", StringComparison.OrdinalIgnoreCase))
            return await RenameDirectoryPackageAsync(classId, newId, oldLeaf, newName, dirCtx, preview);

        var graph = _libraries.CombinedGraph;

        // Files to edit: the class's own file (its declaration) + the file of every class that uses it.
        var fileIds = new HashSet<string>(StringComparer.Ordinal);
        if (node.ContainingFileId is not null)
            fileIds.Add(node.ContainingFileId);
        foreach (var dependent in graph.GetModelUsedBy(classId))
            if (dependent.ContainingFileId is not null)
                fileIds.Add(dependent.ContainingFileId);

        var paths = fileIds
            .Select(id => graph.GetNode<FileNode>(id)?.FilePath)
            .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        if (paths.Count == 0)
            return new ToolError($"Could not locate any source file for '{classId}'.");

        // Plan the precise edits per file: the declaration name tokens + resolved usage leaf tokens.
        var planned = new List<(string path, string newContent, int count)>();
        foreach (var path in paths)
        {
            var text = await File.ReadAllTextAsync(path);
            var (tree, _) = ModelicaParserHelper.ParseWithErrors(text);

            var locator = new ReferenceLocator(graph, new[] { classId });
            locator.Visit(tree);

            var spans = new List<(int start, int stop)>();
            foreach (var def in locator.Definitions)
                if (def.Id == classId)
                    foreach (var token in def.NameTokens)
                        spans.Add((token.StartIndex, token.StopIndex));
            foreach (var site in locator.Sites)
                if (site.TargetId == classId && string.Equals(site.Leaf.Text, oldLeaf, StringComparison.Ordinal))
                    spans.Add((site.Leaf.StartIndex, site.Leaf.StopIndex));

            if (spans.Count == 0)
                continue;

            var newContent = ApplySpans(text, spans, newName);

            var (_, resultErrors) = ModelicaParserHelper.ParseWithErrors(newContent);
            if (resultErrors.Count > 0)
                return new ToolError(
                    $"Renaming would leave '{path}' unparseable ({DescribeErrors(resultErrors)}). No files " +
                    "were changed.");

            planned.Add((path, newContent, spans.Count));
        }

        if (planned.Count == 0)
            return new ToolError(
                $"No references to '{classId}' were found to rename. (Has analyze_dependencies run since the class was loaded?)");

        var total = planned.Sum(p => p.count);
        var note = $"Precise rename of the declaration and resolved references. Deep member accesses " +
                   $"(e.g. Pkg.{oldLeaf}.someMember) are not rewritten — consistent with dependency " +
                   "analysis; review those and verify with a model checker.";

        if (preview)
        {
            var previewChanges = planned.Select(p => new RenameFileChange(p.path, p.count, p.newContent)).ToList();
            return new RenameClassResult(classId, newId, PreviewOnly: true, Changed: false,
                planned.Count, total, previewChanges, note);
        }

        if (FileWritability.PreflightWritable(planned.Select(p => p.path), $"rename '{classId}'") is { } readOnly)
            return readOnly;

        var affected = new List<string>();
        foreach (var (path, newContent, _) in planned)
        {
            await File.WriteAllTextAsync(path, newContent);
            affected.AddRange(await _libraries.ReloadFileAsync(path));
        }
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        var changes = planned.Select(p => new RenameFileChange(p.path, p.count, null)).ToList();
        return new RenameClassResult(classId, newId, PreviewOnly: false, Changed: true,
            planned.Count, total, changes, note);
    }

    // Replace each [start, stop] span (inclusive) with 'replacement', applying right-to-left so earlier
    // offsets stay valid. Spans must not overlap (declaration tokens and usage leaves never do).
    private static string ApplySpans(string text, List<(int start, int stop)> spans, string replacement)
    {
        var builder = new StringBuilder(text);
        foreach (var (start, stop) in spans.OrderByDescending(s => s.start))
            builder.Remove(start, stop - start + 1).Insert(start, replacement);
        return builder.ToString();
    }

    // Apply per-span text replacements (each with its own replacement string), right-to-left.
    private static string ApplyReplacements(string text, List<(int start, int stop, string replacement)> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var (start, stop, replacement) in edits.OrderByDescending(e => e.start))
            builder.Remove(start, stop - start + 1).Insert(start, replacement);
        return builder.ToString();
    }

    private static string CollapseBlankLines(string s)
        => Regex.Replace(s.Replace("\r\n", "\n"), "\n{3,}", "\n\n");

    private static void RemoveFromPackageOrder(string directory, string className)
    {
        var path = Path.Combine(directory, "package.order");
        if (!File.Exists(path))
            return;
        var lines = File.ReadAllLines(path, Encoding.Latin1)
            .Where(l => !string.Equals(l.Trim(), className, StringComparison.Ordinal))
            .ToList();
        File.WriteAllLines(path, lines, Encoding.Latin1);
    }

    private static string DescribeErrors(IReadOnlyList<ParserError> errors)
    {
        var shown = errors.Take(5).Select(e => $"line {e.Line}:{e.CharPosition} {e.Message}");
        return string.Join("; ", shown) + (errors.Count > 5 ? $" (+{errors.Count - 5} more)" : "");
    }

    private static readonly string[] ElementPrefixes = { "replaceable", "redeclare", "inner", "outer", "final" };

    private static bool StartsWithElementPrefix(string source)
    {
        var firstToken = source.TrimStart().Split(new[] { ' ', '\t', '\r', '\n' }, 2)[0];
        return ElementPrefixes.Contains(firstToken);
    }

    // Insert 'source' as a nested class into 'parentCode', just before the parent's closing
    // 'end {parentLeaf};'. Returns null if the closing cannot be located.
    private static string? InsertNestedClass(string parentCode, string parentLeaf, string source)
    {
        var matches = Regex.Matches(parentCode, $@"\bend\s+{Regex.Escape(parentLeaf)}\b");
        if (matches.Count == 0)
            return null;
        var at = matches[^1].Index; // the parent's own closing 'end'
        var indented = IndentBlock(source.Trim(), "  ");
        return parentCode[..at] + indented + "\n" + parentCode[at..];
    }

    private static string IndentBlock(string code, string indent)
        => string.Join("\n", code.Replace("\r\n", "\n").Split('\n').Select(l => l.Length == 0 ? l : indent + l));

    // Keep a directory package's package.order in sync when a child is added. No-op if there is no
    // package.order (the loader includes every .mo file anyway).
    private static void AppendToPackageOrder(string directory, string className)
    {
        var path = Path.Combine(directory, "package.order");
        if (!File.Exists(path))
            return;
        var lines = File.ReadAllLines(path, Encoding.Latin1).ToList();
        if (lines.Any(l => string.Equals(l.Trim(), className, StringComparison.Ordinal)))
            return;
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);
        lines.Add(className);
        File.WriteAllLines(path, lines, Encoding.Latin1);
    }

    private static string PrependWithinClause(string ownerCode, string? parentModelName)
    {
        if (ownerCode.StartsWith("within", StringComparison.Ordinal))
            return ownerCode;
        return string.IsNullOrEmpty(parentModelName)
            ? "within;\n" + ownerCode
            : $"within {parentModelName};\n{ownerCode}";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return 0;
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string source, string oldValue, string newValue)
    {
        var idx = source.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? source : source[..idx] + newValue + source[(idx + oldValue.Length)..];
    }
}

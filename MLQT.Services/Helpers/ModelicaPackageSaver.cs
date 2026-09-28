using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using static MLQT.Services.LoggingService;

namespace MLQT.Services.Helpers;

/// <summary>
/// Service for saving Modelica packages to disk or zip files.
/// </summary>
public class ModelicaPackageSaver
{
    private static readonly Regex DymolaChecksumRegex = new(@"Dymola\(checkSum=""\d+:\d+""\),", RegexOptions.Compiled);

    /// <summary>
    /// Saves a specific library (subset of models) to a directory structure and returns information about written files.
    /// Only saves models whose IDs are in the provided set.
    /// Uses parallel processing for improved performance on large libraries.
    /// </summary>
    /// <param name="graph">The graph containing all models</param>
    /// <param name="modelIds">Set of model IDs belonging to the library to save</param>
    /// <param name="rootDirectory">The root directory to save to (parent of library directory)</param>
    /// <param name="showAnnotations">Whether to include annotations in the output</param>
    /// <param name="settings">
    /// The repository's settings, which decide which classes are written back exactly as they are:
    /// every class when <see cref="StyleCheckingSettings.ApplyFormattingRules"/> is off, and otherwise
    /// each one <see cref="FormattingExclusion.Excludes"/> names — the name list and
    /// <c>__MLQT(format=false)</c> alike. Null asks only the source, for a caller with no repository.
    /// </param>
    /// <param name="newFileStyle">
    /// The encoding and line ending for a file the save creates. A file that already exists keeps its
    /// own either way; null writes a new one as UTF-8 with line feeds. Split into files passes the
    /// style of the file the package came from, so its new files match it (B308).
    /// </param>
    /// <param name="untouchedModelIds">
    /// The classes stored in files the save must leave exactly as they are — a file with syntax
    /// errors, for Format All (B414). Such a class is neither parsed, rendered nor written, and does
    /// not move: a package's inline children stay in its file rather than being split out beside
    /// it, and a single-file library is not expanded. Classes below it that are stored in files of
    /// their own are still written, into the directory the package already has. The caller keeps
    /// those files (and a skipped <c>package.mo</c>'s <c>package.order</c>), which this does not
    /// write and so does not report.
    /// </param>
    /// <returns>SaveResult containing information about all written files and model-to-file mappings</returns>
    public static SaveResult SaveLibraryToDirectoryWithResult(DirectedGraph graph, HashSet<string> modelIds, string rootDirectory, bool showAnnotations, FormattingOptions formatting, StyleCheckingSettings? settings = null, ModelicaFileEncoding.FileStyle? newFileStyle = null, IReadOnlySet<string>? untouchedModelIds = null)
    {
        var result = new SaveResult();
        var untouched = untouchedModelIds ?? new HashSet<string>(StringComparer.Ordinal);

        // Get only the models belonging to this library
        var allModels = graph.ModelNodes.Where(m => modelIds.Contains(m.Id)).ToList();

        // The ones the save renders: every class but those in a file it must leave alone.
        var modelsToRender = untouched.Count == 0
            ? allModels
            : allModels.Where(m => !untouched.Contains(m.Id)).ToList();

        // Refuse outright rather than filtering them out. A stub stands for a class in an encrypted
        // third-party library, and its "source" is a reconstruction from documentation — writing it
        // anywhere would replace a vendor's library with our own summary of it. Silently skipping
        // would hide the fact that a caller assembled the wrong model set; a caller that has stubs
        // in hand has a bug, and it should surface here rather than on a user's installation.
        var stub = allModels.FirstOrDefault(m => m.IsExternalStub);
        if (stub is not null)
        {
            throw new InvalidOperationException(
                $"Refusing to save '{stub.Id}': it belongs to an encrypted library and exists only as a " +
                "reconstruction from vendor documentation. Reference libraries are read-only.");
        }

        // A directory whose package.mo defines a model rather than a package cannot be written back
        // as it is laid out: the class is written as a file, and the classes stored in the directory
        // beside it have nowhere to go. Refused before anything is written, rather than leaving a
        // Lib.mo beside the untouched Lib/ that defines Lib a second time (B443).
        result.NonPackageDirectoryIds.AddRange(NonPackageDirectories(graph, modelsToRender));
        if (result.NonPackageDirectoryIds.Count > 0)
        {
            Warn(nameof(ModelicaPackageSaver),
                $"Not saving: {string.Join(", ", result.NonPackageDirectoryIds)} is stored as a directory " +
                "but is not a package, and only a package is written as one");
            return result;
        }

        // PHASE 1: Pre-parse all models in parallel (batched to limit peak memory)
        PreParseModelsParallel(modelsToRender, modelIds);

        // PHASE 2: Pre-compute tree structure (parent-child relationships and standalone status)
        var modelIndex = allModels.ToDictionary(m => m.Id);
        var childrenByParent = BuildChildrenIndex(allModels, modelIds);

        // Pre-compute data that requires parse trees while they are still available
        // (parse trees will be released during rendering in Phase 3). This runs before
        // ComputeStandaloneChildren because that asks what each child would be written AS, and a
        // short class definition is a package that is still written as a file.
        var shortClassIds = new HashSet<string>();
        var preComputedElementNames = new Dictionary<string, List<string>>();
        var formatPreserved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in allModels)
        {
            if (PackageFileLayout.IsShortClassDefinition(model))
                shortClassIds.Add(model.Id);

            // Keeps the model's original text (no reformatting/reordering). Asked through the shared
            // FormattingExclusion so every writing path gets the same answer — the incremental format
            // used to read the name list only and reordered the classes the annotation was written
            // on, and Split into files passed no list at all and reformatted the ones named in it
            // (B65, B305). A repository with formatting off has every class moved as it was written.
            if (!untouched.Contains(model.Id)
                && (settings is { ApplyFormattingRules: false } || FormattingExclusion.Excludes(model, settings)))
                formatPreserved.Add(model.Id);

            // Pre-compute element names for packages without a stored package.order
            if (model.PackageOrder == null && model.Definition.ParsedCode != null
                && model.ClassType == "package")
            {
                var elementNames = ExtractAllElementNamesFromPackage(model.Definition.ParsedCode);
                if (elementNames.Count > 0)
                    preComputedElementNames[model.Id] = elementNames;
            }
        }

        AddChildrenSavedElsewhere(graph, allModels, modelIds, preComputedElementNames);

        var standaloneChildren = ComputeStandaloneChildren(childrenByParent, shortClassIds);

        // PHASE 3: Pre-render all models in parallel
        // Parse trees are released immediately after each model is rendered to avoid
        // having all parse trees and all rendered strings coexist in memory.
        var excludedOrNull = formatPreserved.Count > 0 ? formatPreserved : null;
        var verbatimText = ExciseStandaloneChildrenFromVerbatimPackages(
            allModels, formatPreserved, standaloneChildren, shortClassIds);
        // Built once for the whole save, and only when the layout actually asks for the finer
        // declaration order — resolving a type walks imports and the extends chain, and a save that
        // is not ordering declarations must not pay for it.
        var isSimpleType = formatting.DeclarationOrder ? StyleChecking.CreateSimpleTypeLookup(graph) : null;

        var renderedCode = PreRenderModelsParallel(modelsToRender, childrenByParent, standaloneChildren,
            formatting, excludedOrNull, isSimpleType, verbatimText);

        // PHASE 4: Write files (sequential tree traversal using pre-rendered code)
        // Rendered code entries are removed from the dictionary after writing to free memory.
        var savedModels = new HashSet<string>();
        var topLevelModels = allModels.Where(m =>
        {
            return string.IsNullOrEmpty(m.ParentModelName) || !modelIds.Contains(m.ParentModelName);
        }).ToList();

        foreach (var model in topLevelModels)
        {
            WriteModelFiles(model, rootDirectory, allModels, savedModels, childrenByParent,
                standaloneChildren, shortClassIds, preComputedElementNames, renderedCode, result, newFileStyle,
                untouched, formatPreserved);
        }

        // The last line (B441): every class the save was given is in a file it wrote, or the caller
        // is told which are not, so that it deletes nothing that may be the only copy of one.
        foreach (var model in modelsToRender)
        {
            if (!result.ModelIdToFilePath.ContainsKey(model.Id))
                result.UnplacedModelIds.Add(model.Id);
        }

        return result;
    }

    /// <summary>
    /// Renders the single file that <paramref name="fileOwner"/> heads to the formatted Modelica
    /// source that should be written to disk, using the same renderer configuration as a full
    /// library save. Used by single-file edits (e.g. spelling corrections) that persist one .mo
    /// file without rewriting the whole library.
    /// <para>
    /// <paramref name="fileOwner"/> must be the topmost model stored in its file — its
    /// <c>ModelicaCode</c> is the complete file slice. Standalone children stored in their own
    /// files are excluded automatically because that slice does not contain them, so no
    /// <c>classNamesToExclude</c> set is needed. The file-owner's <c>ParsedCode</c> is (re)parsed
    /// from its current <c>ModelicaCode</c> so a caller can mutate the source first.
    /// </para>
    /// </summary>
    /// <param name="isSimpleType">The lookup that tells a variable from a component, for
    /// <see cref="FormattingOptions.DeclarationOrder"/> — the checker's, keyed from
    /// <paramref name="fileOwner"/>'s id, so the two cannot order a class differently.</param>
    public static string RenderFileOwnerModel(ModelNode fileOwner, FormattingOptions formatting,
        Func<string, string, bool>? isSimpleType = null)
    {
        // The stored ModelicaCode is the extracted class body without a 'within' clause.
        // The file written to disk must carry the within clause so that, when the library is
        // reloaded, the standalone file re-parses with the correct package context and the model
        // regenerates its original hierarchical ID (e.g. "VeSyMA.EnergyStorage.Summary.Null"
        // rather than a detached "Null"). Mirror the full-save path (PreParseModelsParallel) and
        // prepend the within clause for rendering, without mutating the stored within-less body.
        var sourceCode = WithinClause.Ensure(fileOwner.Definition.ModelicaCode ?? "", fileOwner.ParentModelName);

        var (parseTree, _) = ModelicaParserHelper.ParseWithErrors(sourceCode);
        fileOwner.Definition.ParsedCode = parseTree;

        var rendered = RenderStoredDefinition(parseTree, formatting,
            rootClassId: isSimpleType is null ? null : fileOwner.Id, isSimpleType);

        // The file's header and trailing comments, which the stored source never carries (B445), as
        // the renderer writes them. Not when the source is already a whole file (format_class hands
        // over the file as it is on disk): its own header was rendered with it.
        return fileOwner.FileText is { } fileText && !WithinClause.Has(fileOwner.Definition.ModelicaCode ?? "")
            ? fileText.Formatted().ApplyTo(rendered)
            : rendered;
    }

    /// <summary>
    /// Renders a whole .mo file's source to the formatted text that should replace it, using the same
    /// renderer configuration as a full library save.
    /// <para>
    /// Prefer this over <see cref="RenderFileOwnerModel"/> whenever the file's text is available on
    /// disk. A model node's stored <c>ModelicaCode</c> is not always the whole file:
    /// <c>PackageCodeTrimmer</c> trims a package's inline standalone children out of it (each child
    /// has its own node), so rendering a package from its stored source would write the file back
    /// without those classes. The file's own text is the one representation that always holds every
    /// class stored in it, nested children included.
    /// </para>
    /// </summary>
    /// <param name="fileSource">The complete current text of the file, as read from disk.</param>
    /// <param name="withinParent">
    /// Fully-qualified name of the package the file's classes live in, used only if
    /// <paramref name="fileSource"/> carries no within clause of its own. Null or empty for a
    /// top-level library.
    /// </param>
    public static string RenderFileSource(string fileSource, string? withinParent, FormattingOptions formatting)
        => RenderFileSource(fileSource, withinParent, formatting, out _);

    /// <inheritdoc cref="RenderFileSource(string, string?, bool, bool, bool)"/>
    /// <param name="parserErrors">
    /// Syntax errors found in <paramref name="fileSource"/>. A caller about to overwrite the file
    /// must check this and leave the file alone when it is non-empty: the renderer will still
    /// produce output for malformed input, but that output is not a faithful copy of the file.
    /// </param>
    public static string RenderFileSource(string fileSource, string? withinParent, FormattingOptions formatting,
        out IReadOnlyList<ParserError> parserErrors, string? rootClassId = null,
        Func<string, string, bool>? isSimpleType = null)
    {
        var (parseTree, errors) = ParseFileSource(fileSource, withinParent);
        parserErrors = errors;
        return RenderStoredDefinition(parseTree, formatting, rootClassId, isSimpleType);
    }

    /// <summary>
    /// The syntax errors in a file's own text, parsed exactly as <see cref="RenderFileSource(string, string?, FormattingOptions, out IReadOnlyList{ParserError}, string?, Func{string, string, bool}?)"/>
    /// parses it before rendering.
    ///
    /// <para><b>The one rule both formatters apply</b>: a file with any is left exactly as the user
    /// left it, because the renderer still produces output for malformed input and that output is
    /// not a faithful copy of the file. The incremental formatter asks it through
    /// <c>RenderFileSource</c>; <b>Format All Files</b> asks it here, of every file of a library
    /// before the save, and leaves every class stored in such a file where it is (B414).</para>
    /// </summary>
    /// <param name="fileSource">The complete current text of the file, as read from disk.</param>
    /// <param name="withinParent">The package the file's classes live in, used only if the file
    /// carries no within clause of its own.</param>
    public static IReadOnlyList<ParserError> SyntaxErrorsInFile(string fileSource, string? withinParent)
        => ParseFileSource(fileSource, withinParent).Errors;

    /// <summary>How a file refused for its syntax errors is described in the log.</summary>
    public static string DescribeSyntaxErrors(IReadOnlyList<ParserError> errors)
        => $"{errors.Count} syntax error(s), first at line {errors[0].Line}: {errors[0].Message}";

    private static (modelicaParser.Stored_definitionContext Tree, IReadOnlyList<ParserError> Errors) ParseFileSource(
        string fileSource, string? withinParent)
    {
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(WithinClause.Ensure(fileSource, withinParent));
        return (parseTree, errors);
    }

    /// <summary>Renders a parsed stored_definition with the standard save-time renderer settings.</summary>
    /// <param name="rootClassId">The id of the outermost class, for the type lookup below.</param>
    /// <param name="isSimpleType">Tells the renderer a variable from a component, which is what
    /// <see cref="FormattingOptions.DeclarationOrder"/> needs and the grammar cannot answer. The
    /// checker is given the same lookup, so the two cannot order a class differently.</param>
    private static string RenderStoredDefinition(modelicaParser.Stored_definitionContext parseTree,
        FormattingOptions formatting, string? rootClassId = null,
        Func<string, string, bool>? isSimpleType = null)
    {
        var visitor = new ModelicaRenderer(
            renderForCodeEditor: false,
            showAnnotations: true,
            excludeClassDefinitions: false,
            tokenStream: null,
            classNamesToExclude: null,
            formatting: formatting,
            rootClassId: rootClassId,
            isSimpleType: isSimpleType);
        visitor.VisitStored_definition(parseTree);

        var code = string.Join("\n", visitor.Code);
        return DymolaChecksumRegex.Replace(code, "");
    }

    /// <summary>
    /// Pre-parses all models in parallel to prepare ParsedCode.
    /// Processes in batches with GC hints to limit peak memory from parse trees.
    /// </summary>
    private static void PreParseModelsParallel(List<ModelNode> allModels, HashSet<string> modelIds)
    {
        const int batchSize = 500;

        foreach (var batch in Batch(allModels, batchSize))
        {
            Parallel.ForEach(batch, model =>
            {
                try
                {
                    // Parse from a within-prepended copy so the rendered file carries the clause and
                    // re-parses with the right package context on reload. The clause stays in this
                    // local: writing it back into ModelicaCode would leave every model in the graph
                    // carrying one after a full save, shifting each finding's line number by one and
                    // making it depend on whether a save had run — which is what let a later
                    // formatter add a second clause. PreRenderModelsParallel releases ParsedCode
                    // once it has rendered, so the with-clause tree does not outlive this save.
                    var sourceToParse = WithinClause.Ensure(model.Definition.ModelicaCode, model.ParentModelName);

                    var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(sourceToParse);
                    model.Definition.ParsedCode = parseTree;
                    foreach (var error in errors)
                    {
                        Error("ModelicaPackageSaver", $"Parse error in {model.Id} at line {error.Line}: {error.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Error("ModelicaPackageSaver", $"Failed to parse model {model.Id}", ex);
                }
            });

            // Hint GC between batches to reclaim intermediate allocations
            // (e.g., old ModelicaCode strings replaced by within-prepended versions)
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }
    }

    /// <summary>
    /// Adds to a package's <c>package.order</c> names the children the save was not given (B375).
    ///
    /// <para>A package with no stored <c>package.order</c> gets one built from what the save knows:
    /// the children in its set and the names in the package's own source. A save of part of a
    /// library — Split into files, which is given only the classes in the package's own file (B305)
    /// — does not have the children that already live in files of their own, and the order it wrote
    /// left them out. A <c>package.order</c> that omits a class is one a tool may read as the whole
    /// list. They go last, by name, because nothing on disk says where else they belong.</para>
    ///
    /// <para>A package with a stored order is left to it: that order is the user's, and it already
    /// names whatever it names.</para>
    /// </summary>
    private static void AddChildrenSavedElsewhere(
        DirectedGraph graph, List<ModelNode> allModels, HashSet<string> modelIds,
        Dictionary<string, List<string>> orderNames)
    {
        var unordered = allModels
            .Where(m => m.PackageOrder == null && m.ClassType == "package")
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (unordered.Count == 0)
            return;

        foreach (var group in graph.ModelNodes
                     .Where(m => !modelIds.Contains(m.Id)
                                 && m.ParentModelName is { } parent && unordered.Contains(parent))
                     .GroupBy(m => m.ParentModelName!))
        {
            if (!orderNames.TryGetValue(group.Key, out var names))
                orderNames[group.Key] = names = [];

            foreach (var name in group.Select(m => m.Definition.Name).OrderBy(n => n, StringComparer.Ordinal))
                if (!names.Contains(name))
                    names.Add(name);
        }
    }

    /// <summary>
    /// Builds an index of children by parent model ID.
    /// </summary>
    private static Dictionary<string, List<ModelNode>> BuildChildrenIndex(List<ModelNode> allModels, HashSet<string> modelIds)
    {
        var result = new Dictionary<string, List<ModelNode>>();

        foreach (var model in allModels)
        {
            var parentName = model.ParentModelName;

            if (!string.IsNullOrEmpty(parentName) && modelIds.Contains(parentName))
            {
                if (!result.ContainsKey(parentName))
                    result[parentName] = new List<ModelNode>();
                result[parentName].Add(model);
            }
        }

        return result;
    }

    /// <summary>
    /// Computes which children can be stored standalone for each parent.
    /// Returns a dictionary mapping parent ID to set of standalone child names.
    ///
    /// <para>The answer itself is <see cref="PackageFileLayout"/>'s, because
    /// <c>SingleFilePackageAnalyzer</c> reports on exactly the classes this decides to write, and
    /// the two had a copy each of a rule that was the same wrong answer twice (B245). The
    /// short-class question is handed in rather than asked, because every parse tree is still in
    /// hand at this point.</para>
    /// </summary>
    private static Dictionary<string, HashSet<string>> ComputeStandaloneChildren(
        Dictionary<string, List<ModelNode>> childrenByParent,
        HashSet<string> shortClassIds)
        => childrenByParent.ToDictionary(
            kvp => kvp.Key,
            kvp => PackageFileLayout.StandaloneChildNames(kvp.Value, m => shortClassIds.Contains(m.Id)));

    /// <summary>
    /// The text each package excluded from formatting is written as: its own source, verbatim, less
    /// the children written as files of their own (B309).
    ///
    /// <para>A formatted package has those children removed by the renderer
    /// (<c>classNamesToExclude</c>); a verbatim one bypasses the renderer, so any such child still in
    /// its source — one the trimmer never cut, or a package that was never trimmed — was written
    /// twice, inline and beside it: a duplicate definition, and a load error. A child that cannot be
    /// cut out without taking a neighbour's text with it stays inline instead, and is taken out of
    /// <paramref name="standaloneChildren"/> so it is not also written separately.</para>
    ///
    /// <para>Sequential, before the parallel render, because it edits
    /// <paramref name="standaloneChildren"/>, which that render reads for every package.</para>
    /// </summary>
    private static Dictionary<string, string> ExciseStandaloneChildrenFromVerbatimPackages(
        List<ModelNode> allModels,
        HashSet<string> excludedModelIds,
        Dictionary<string, HashSet<string>> standaloneChildren,
        HashSet<string> shortClassIds)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var model in allModels)
        {
            if (!excludedModelIds.Contains(model.Id)
                || model.Definition.ParsedCode is null
                || !PackageFileLayout.WrittenAsDirectory(model, m => shortClassIds.Contains(m.Id))
                || !standaloneChildren.TryGetValue(model.Id, out var names)
                || names.Count == 0)
                continue;

            // The same text PreParseModelsParallel parsed, so the tree's lines are this text's lines.
            var source = WithinClause.Ensure(model.Definition.ModelicaCode, model.ParentModelName);
            result[model.Id] = PackageCodeTrimmer.ExciseInlineClasses(
                source, model.Definition.ParsedCode, names, out var keptInline);
            names.ExceptWith(keptInline);
        }

        return result;
    }

    /// <summary>
    /// Pre-renders all models in parallel and returns a dictionary of rendered code.
    /// Parse trees are released immediately after each model is rendered to minimize
    /// peak memory (avoids all parse trees and all rendered strings coexisting).
    /// Processes in batches with GC hints between batches.
    /// </summary>
    private static ConcurrentDictionary<string, string> PreRenderModelsParallel(
        List<ModelNode> allModels,
        Dictionary<string, List<ModelNode>> childrenByParent,
        Dictionary<string, HashSet<string>> standaloneChildren,
        FormattingOptions formatting,
        HashSet<string>? excludedModelIds = null,
        Func<string, string, bool>? isSimpleType = null,
        IReadOnlyDictionary<string, string>? verbatimText = null)
    {
        const int batchSize = 500;
        var renderedCode = new ConcurrentDictionary<string, string>();

        foreach (var batch in Batch(allModels, batchSize))
        {
            Parallel.ForEach(batch, model =>
            {
                try
                {
                    if (model.Definition.ParsedCode == null)
                        return;

                    // Skip formatting for excluded models — use original code. The within clause still
                    // has to go on: what this dictionary holds becomes the text of the file, and a
                    // file with no within clause reloads as a detached top-level class instead of a
                    // member of its package. Every other entry here comes from the renderer, which
                    // emits the clause from the parse tree; this is the one path that bypasses it.
                    if (excludedModelIds != null && excludedModelIds.Contains(model.Id))
                    {
                        renderedCode[model.Id] = verbatimText != null && verbatimText.TryGetValue(model.Id, out var excised)
                            ? excised
                            : WithinClause.Ensure(model.Definition.ModelicaCode, model.ParentModelName);
                        model.Definition.ParsedCode = null;
                        return;
                    }

                    // Determine which children to exclude (for packages)
                    HashSet<string>? classNamesToExclude = null;
                    if (PackageFileLayout.WrittenAsDirectory(model))
                    {
                        standaloneChildren.TryGetValue(model.Id, out classNamesToExclude);
                    }

                    // Render the model
                    var visitor = new ModelicaRenderer(
                        renderForCodeEditor: false,
                        showAnnotations: true,
                        excludeClassDefinitions: false,
                        tokenStream: null,
                        classNamesToExclude: classNamesToExclude,
                        formatting: formatting,
                        rootClassId: model.Id,
                        isSimpleType: isSimpleType);
                    visitor.VisitStored_definition(model.Definition.ParsedCode);
                    var code = string.Join("\n", visitor.Code);

                    // Remove Dymola checksum annotations
                    code = DymolaChecksumRegex.Replace(code, "");

                    renderedCode[model.Id] = code;

                    // Release parse tree immediately — rendering is complete and the tree
                    // is no longer needed. This prevents parse trees from accumulating
                    // alongside rendered strings.
                    model.Definition.ParsedCode = null;
                }
                catch (Exception ex)
                {
                    Error("ModelicaPackageSaver", $"Failed to render model {model.Id}", ex);
                }
            });

            // Hint GC between batches to reclaim released parse trees
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        }

        return renderedCode;
    }

    /// <summary>
    /// Writes model files to disk using pre-rendered code.
    /// Removes rendered code entries after writing to free memory progressively.
    /// Updates ModelicaCode with the rendered version so the old source string can be collected.
    /// </summary>
    private static void WriteModelFiles(
        ModelNode model,
        string parentDirectory,
        List<ModelNode> allModels,
        HashSet<string> savedModels,
        Dictionary<string, List<ModelNode>> childrenByParent,
        Dictionary<string, HashSet<string>> standaloneChildren,
        HashSet<string> shortClassIds,
        Dictionary<string, List<string>> preComputedElementNames,
        ConcurrentDictionary<string, string> renderedCode,
        SaveResult result,
        ModelicaFileEncoding.FileStyle? newFileStyle,
        IReadOnlySet<string> untouched,
        IReadOnlySet<string> verbatim)
    {
        if (savedModels.Contains(model.Id))
            return;

        savedModels.Add(model.Id);

        // A class in a file the save must leave alone (B414) is not written and does not move. The
        // classes nested in its file are untouched with it; any below it stored in files of their
        // own are written where they already are, in the directory the package has.
        if (untouched.Contains(model.Id))
        {
            if (childrenByParent.TryGetValue(model.Id, out var below))
            {
                var packageDir = Path.Combine(parentDirectory, model.Definition.Name);
                foreach (var child in below.Where(c => !untouched.Contains(c.Id)))
                {
                    WriteModelFiles(child, packageDir, allModels, savedModels, childrenByParent,
                        standaloneChildren, shortClassIds, preComputedElementNames, renderedCode, result,
                        newFileStyle, untouched, verbatim);
                }
            }

            return;
        }

        if (!renderedCode.TryRemove(model.Id, out var code))
            return;

        // The same question ComputeStandaloneChildren asked when it decided this class could have
        // its own entry, so the two cannot disagree about what that entry is.
        if (PackageFileLayout.WrittenAsDirectory(model, m => shortClassIds.Contains(m.Id)))
        {
            // Create package directory
            var packageDir = Path.Combine(parentDirectory, model.Definition.Name);
            try
            {
                Directory.CreateDirectory(packageDir);
                result.CreatedDirectories.Add(packageDir);
            }
            catch (Exception e)
            {
                Error("ModelicaPackageSaver", $"Failed to create directory: {packageDir}", e);
            }

            // Write package.mo
            var packageFile = Path.Combine(packageDir, "package.mo");
            try
            {
                ModelicaFileEncoding.WriteAllTextLike(packageFile, WithFileText(model, code, verbatim), newFileStyle);
                result.WrittenFiles.Add(packageFile);
                result.ModelIdToFilePath[model.Id] = packageFile;
            }
            catch (Exception e)
            {
                Error("ModelicaPackageSaver", $"Failed to write package file: {packageFile}", e);
                result.FailedFiles.Add(packageFile);
            }

            StoreWrittenCode(model, code, result.WrittenFiles.Contains(packageFile));

            // Get children for this package
            childrenByParent.TryGetValue(model.Id, out var children);
            children ??= new List<ModelNode>();

            // Write package.order
            if (children.Any() || model.PackageOrder != null || model.NestedChildrenOrder != null)
            {
                var packageOrderFile = Path.Combine(packageDir, "package.order");
                var packageOrderList = BuildPackageOrderList(model, children, preComputedElementNames);

                if (packageOrderList.Count > 0)
                {
                    try
                    {
                        ModelicaFileEncoding.WriteAllLinesLike(packageOrderFile, packageOrderList, newFileStyle);
                        result.WrittenFiles.Add(packageOrderFile);
                    }
                    catch (Exception e)
                    {
                        Error("ModelicaPackageSaver", $"Failed to write package.order: {packageOrderFile}", e);
                        result.FailedFiles.Add(packageOrderFile);
                    }
                }
            }

            // Get standalone children for this package
            standaloneChildren.TryGetValue(model.Id, out var standaloneNames);
            standaloneNames ??= new HashSet<string>();

            // Process children
            foreach (var child in children)
            {
                // A child already stored in a file of its own is written back there even when the
                // layout would not give it one - its name colliding with a sibling's entry or a
                // reserved one. It is not in this package's source, so it cannot go into package.mo,
                // and mapping it there left its own file, the only copy of the class, to be deleted
                // as an orphan (B441).
                if (standaloneNames.Contains(child.Definition.Name) || StoredInAFileOfItsOwn(child, model.ContainingFileId))
                {
                    // Recursively write standalone child
                    WriteModelFiles(child, packageDir, allModels, savedModels, childrenByParent,
                        standaloneChildren, shortClassIds, preComputedElementNames, renderedCode, result,
                        newFileStyle, untouched, verbatim);
                }
                else
                {
                    // Non-standalone children are in package.mo — update their ModelicaCode
                    // with the rendered version so the displayed code matches what was saved
                    UpdateNestedChildren(child, packageFile, model.ContainingFileId, savedModels, childrenByParent,
                        renderedCode, result);
                }
            }
        }
        else
        {
            // Write as standalone .mo file
            var fileName = $"{model.Definition.Name}.mo";
            var filePath = Path.Combine(parentDirectory, fileName);
            try
            {
                ModelicaFileEncoding.WriteAllTextLike(filePath, WithFileText(model, code, verbatim), newFileStyle);
                result.WrittenFiles.Add(filePath);
                result.ModelIdToFilePath[model.Id] = filePath;
            }
            catch (Exception e)
            {
                Error("ModelicaPackageSaver", $"Failed to write model file: {filePath}", e);
                result.FailedFiles.Add(filePath);
            }

            StoreWrittenCode(model, code, result.WrittenFiles.Contains(filePath));

            // Update non-standalone children embedded in this model (e.g., nested classes
            // inside a model/block/connector) so their displayed code matches what was saved
            if (childrenByParent.TryGetValue(model.Id, out var nestedChildren))
            {
                foreach (var child in nestedChildren)
                {
                    UpdateNestedChildren(child, filePath, model.ContainingFileId, savedModels, childrenByParent,
                        renderedCode, result);
                }
            }
        }
    }

    /// <summary>
    /// The text of the file <paramref name="model"/> heads: <paramref name="code"/>, with the file's
    /// own text outside the class put back around it (B445). A class written as it was keeps that text
    /// as it was; a formatted one gets it as the renderer writes it, which is what the incremental
    /// formatter writes for the same file. A class that headed no file has none, so a split's new
    /// per-class files get no header and the package that headed the single file keeps it.
    /// </summary>
    private static string WithFileText(ModelNode model, string code, IReadOnlySet<string> verbatim)
        => model.FileText is not { } fileText ? code
            : (verbatim.Contains(model.Id) ? fileText : fileText.Formatted()).ApplyTo(code);

    /// <summary>
    /// Stores the rendered text on the class, which frees the old source string - but only when the
    /// file that holds it was written. A class whose file failed is still on disk as it was, and
    /// storing the rendered text left the graph showing and checking code that was nowhere on disk
    /// after a partial Format All (B374). The within clause is stripped back off: it belongs to the
    /// file, and every other path stores class source without one.
    /// </summary>
    private static void StoreWrittenCode(ModelNode model, string code, bool written)
    {
        if (!written)
            return;

        model.Definition.ModelicaCode = WithinClause.Strip(code);
        model.SourceMatchesFile = false;   // renderer's lines now, not the file's — see ModelNode
    }

    /// <summary>
    /// Recursively updates ModelicaCode for a non-standalone model and all its descendants.
    /// These models are embedded in their parent's file and don't get written separately,
    /// but their in-memory ModelicaCode must reflect the formatted version.
    /// </summary>
    /// <param name="ownerFileId">The file the class that heads <paramref name="containingFilePath"/>
    /// was loaded from. Only a class loaded from that same file is in the text written there.</param>
    private static void UpdateNestedChildren(
        ModelNode model,
        string containingFilePath,
        string? ownerFileId,
        HashSet<string> savedModels,
        Dictionary<string, List<ModelNode>> childrenByParent,
        ConcurrentDictionary<string, string> renderedCode,
        SaveResult result)
    {
        savedModels.Add(model.Id);

        // Only a file that was written holds the class. Mapping it to one whose write failed told a
        // caller the class was safely on disk when it was nowhere but the file it came from (B303).
        // Nor does a file hold a class that came from another file: the text written is the owner's
        // own source, rendered, and a class stored elsewhere was never in it (B441). Left unmapped,
        // it is reported in UnplacedModelIds and its library loses no file.
        var written = result.WrittenFiles.Contains(containingFilePath)
            && !StoredInAFileOfItsOwn(model, ownerFileId);
        if (written)
            result.ModelIdToFilePath[model.Id] = containingFilePath;
        if (renderedCode.TryRemove(model.Id, out var childCode))
            StoreWrittenCode(model, childCode, written);

        // Recurse into this model's own nested children
        if (childrenByParent.TryGetValue(model.Id, out var grandchildren))
        {
            foreach (var grandchild in grandchildren)
            {
                UpdateNestedChildren(grandchild, containingFilePath, ownerFileId, savedModels,
                    childrenByParent, renderedCode, result);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="model"/> was loaded from a different file than
    /// <paramref name="enclosingFileId"/> — its own, rather than inline in the class around it. The
    /// question <see cref="PackageCodeTrimmer"/> asks of the same nodes. Unknown on either side (a
    /// graph built without file nodes) reads as inline, which is what every class was taken to be
    /// before B441.
    /// </summary>
    private static bool StoredInAFileOfItsOwn(ModelNode model, string? enclosingFileId)
        => model.ContainingFileId is not null
            && enclosingFileId is not null
            && !string.Equals(model.ContainingFileId, enclosingFileId, StringComparison.Ordinal);

    /// <summary>
    /// The classes among <paramref name="models"/> that a directory's <c>package.mo</c> defines and
    /// that the save would not write as a directory — a <c>model</c>, <c>block</c> or short class
    /// definition rather than a package (B443). MLS 3.6 §13.4.1 asks only that the node define "a
    /// class A", so such a layout loads, but <see cref="PackageFileLayout.WrittenAsDirectory"/> gives a
    /// directory to a package alone, and saving one would move the class out of its directory.
    ///
    /// <para>A class heads a <c>package.mo</c> when that is its file, its name is the directory's, and
    /// it is not nested inline in a class of the same file. The name is what tells the node from a
    /// class called <c>Package</c> in its own <c>Package.mo</c>, which a case-sensitive filesystem can
    /// hold beside it.</para>
    /// </summary>
    public static IReadOnlyList<string> NonPackageDirectories(DirectedGraph graph, IEnumerable<ModelNode> models)
    {
        var found = new List<string>();
        foreach (var model in models)
        {
            if (model.ContainingFileId is null
                || graph.GetNode<FileNode>(model.ContainingFileId)?.FilePath is not { } path
                || !string.Equals(Path.GetFileName(path), "package.mo", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), model.Definition.Name, StringComparison.Ordinal))
                continue;

            var parent = model.ParentModelName is null ? null : graph.GetNode<ModelNode>(model.ParentModelName);
            if (parent is not null && string.Equals(parent.ContainingFileId, model.ContainingFileId, StringComparison.Ordinal))
                continue;

            if (!PackageFileLayout.WrittenAsDirectory(model))
                found.Add(model.Id);
        }
        return found;
    }

    /// <summary>
    /// Builds the package.order list for a package model.
    /// </summary>
    private static List<string> BuildPackageOrderList(ModelNode model, List<ModelNode> children, Dictionary<string, List<string>>? preComputedElementNames = null)
    {
        var packageOrderList = new List<string>();

        // Get stored package.order
        if (model.PackageOrder is string[] storedOrder)
        {
            packageOrderList.AddRange(storedOrder);
        }

        // Get nested children order
        if (model.NestedChildrenOrder is string[] nestedOrder)
        {
            foreach (var childName in nestedOrder)
            {
                if (!packageOrderList.Contains(childName))
                    packageOrderList.Add(childName);
            }
        }

        // Add child models
        foreach (var child in children)
        {
            if (!packageOrderList.Contains(child.Definition.Name))
                packageOrderList.Add(child.Definition.Name);
        }

        // Use pre-computed element names if available (parse trees may have been released),
        // otherwise extract from parsed code
        if (model.PackageOrder == null)
        {
            List<string>? allElementNames = null;
            if (preComputedElementNames != null)
                preComputedElementNames.TryGetValue(model.Id, out allElementNames);
            else if (model.Definition.ParsedCode != null)
                allElementNames = ExtractAllElementNamesFromPackage(model.Definition.ParsedCode);

            if (allElementNames != null)
            {
                foreach (var elementName in allElementNames)
                {
                    if (!packageOrderList.Contains(elementName))
                        packageOrderList.Add(elementName);
                }
            }
        }

        return packageOrderList;
    }

    /// <summary>
    /// Checks if a model uses a short class definition (e.g., package A = B "description";).
    /// Short class definitions should be saved as .mo files, not as directories.
    /// </summary>

    /// <summary>
    /// Extracts all element names from a package's parsed code.
    /// This includes class definitions, constants, types, parameters, and other components.
    /// </summary>
    private static List<string> ExtractAllElementNamesFromPackage(modelicaParser.Stored_definitionContext storedDefinition)
    {
        var elementNames = new List<string>();

        // Visit each class definition in the stored_definition
        foreach (var classDefContext in storedDefinition.class_definition())
        {
            // Get the class name
            var className = classDefContext.class_specifier()?.long_class_specifier()?.IDENT(0)?.GetText()
                ?? classDefContext.class_specifier()?.short_class_specifier()?.IDENT()?.GetText();

            if (!string.IsNullOrEmpty(className))
            {
                // Get the composition from long_class_specifier
                var composition = classDefContext.class_specifier()?.long_class_specifier()?.composition();
                if (composition != null)
                {
                    // Extract elements from all element_list sections in the composition
                    // (public, protected, and initial public section)
                    foreach (var elementList in composition.element_list())
                    {
                        if (elementList != null)
                        {
                            ExtractElementNamesFromElementList(elementList, elementNames);
                        }
                    }
                }
            }
        }

        return elementNames;
    }

    /// <summary>
    /// Extracts element names from an element_list context.
    /// </summary>
    private static void ExtractElementNamesFromElementList(modelicaParser.Element_listContext elementList, List<string> elementNames)
    {
        foreach (var element in elementList.element())
        {
            // Check for class definitions
            var classDefinition = element.class_definition();
            if (classDefinition != null)
            {
                var className = classDefinition.class_specifier()?.long_class_specifier()?.IDENT(0)?.GetText()
                    ?? classDefinition.class_specifier()?.short_class_specifier()?.IDENT()?.GetText();

                if (!string.IsNullOrEmpty(className))
                {
                    elementNames.Add(className);
                }
                continue;
            }

            // Check for component clauses (constants, parameters, variables)
            var componentClause = element.component_clause();
            if (componentClause != null)
            {
                var componentList = componentClause.component_list();
                if (componentList != null)
                {
                    foreach (var componentDecl in componentList.component_declaration())
                    {
                        var declaration = componentDecl.declaration();
                        if (declaration != null)
                        {
                            var componentName = declaration.IDENT()?.GetText();
                            if (!string.IsNullOrEmpty(componentName))
                            {
                                elementNames.Add(componentName);
                            }
                        }
                    }
                }
            }

            // Note: We skip import_clause and extends_clause as they typically don't appear in package.order
        }
    }

    /// <summary>
    /// Splits a list into batches of a given size.
    /// </summary>
    private static IEnumerable<List<T>> Batch<T>(List<T> source, int batchSize)
    {
        for (int i = 0; i < source.Count; i += batchSize)
            yield return source.GetRange(i, Math.Min(batchSize, source.Count - i));
    }

    /// <summary>
    /// Where a library's files are written, given where it was loaded from.
    /// </summary>
    /// <remarks>
    /// <para>Three shapes, and the difference between them is one directory level:</para>
    /// <list type="bullet">
    /// <item>A single <c>.mo</c> file: written back beside itself.</item>
    /// <item>A package directory — one holding a <c>package.mo</c> — is written to its
    /// <em>parent</em>, because <see cref="SaveLibraryToDirectoryWithResult"/> creates the library
    /// folder itself. Handing it the package directory would nest a second copy inside the first.</item>
    /// <item>A directory of loose classes with no <c>package.mo</c>: written to that directory,
    /// since there is no library folder to create.</item>
    /// </list>
    /// <para>Null when the source is gone or resolves to nothing, which the caller reports rather
    /// than guessing at a location to write a user's library to.</para>
    /// </remarks>
    public static string? ResolveSaveDirectory(
        string? sourcePath, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return null;

        string? saveDirectory = null;

        if (fileExists(sourcePath))
        {
            saveDirectory = Path.GetDirectoryName(sourcePath);
        }
        else if (directoryExists(sourcePath))
        {
            saveDirectory = fileExists(Path.Combine(sourcePath, "package.mo"))
                ? Path.GetDirectoryName(sourcePath)
                : sourcePath;
        }

        return string.IsNullOrEmpty(saveDirectory) || !directoryExists(saveDirectory) ? null : saveDirectory;
    }
}

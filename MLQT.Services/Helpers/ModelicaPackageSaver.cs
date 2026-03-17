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
    /// <returns>SaveResult containing information about all written files and model-to-file mappings</returns>
    public static SaveResult SaveLibraryToDirectoryWithResult(DirectedGraph graph, HashSet<string> modelIds, string rootDirectory, bool showAnnotations, bool oneOfEachSection, bool importsFirst, bool componentsBeforeClasses, IReadOnlyList<string>? excludedModelIds = null)
    {
        var result = new SaveResult();

        // Get only the models belonging to this library
        var allModels = graph.ModelNodes.Where(m => modelIds.Contains(m.Id)).ToList();

        // PHASE 1: Pre-parse all models in parallel (batched to limit peak memory)
        PreParseModelsParallel(allModels, modelIds);

        // PHASE 2: Pre-compute tree structure (parent-child relationships and standalone status)
        var modelIndex = allModels.ToDictionary(m => m.Id);
        var childrenByParent = BuildChildrenIndex(allModels, modelIds);
        var standaloneChildren = ComputeStandaloneChildren(allModels, childrenByParent);

        // Pre-compute data that requires parse trees while they are still available
        // (parse trees will be released during rendering in Phase 3)
        var shortClassIds = new HashSet<string>();
        var preComputedElementNames = new Dictionary<string, List<string>>();
        foreach (var model in allModels)
        {
            if (IsShortClassDefinition(model))
                shortClassIds.Add(model.Id);

            // Pre-compute element names for packages without a stored package.order
            if (model.PackageOrder == null && model.Definition.ParsedCode != null
                && model.ClassType == "package")
            {
                var elementNames = ExtractAllElementNamesFromPackage(model.Definition.ParsedCode);
                if (elementNames.Count > 0)
                    preComputedElementNames[model.Id] = elementNames;
            }
        }

        // PHASE 3: Pre-render all models in parallel
        // Parse trees are released immediately after each model is rendered to avoid
        // having all parse trees and all rendered strings coexist in memory.
        var excludedSet = excludedModelIds != null && excludedModelIds.Count > 0
            ? new HashSet<string>(excludedModelIds, StringComparer.Ordinal)
            : null;
        var renderedCode = PreRenderModelsParallel(allModels, childrenByParent, standaloneChildren,
            oneOfEachSection, importsFirst, componentsBeforeClasses, excludedSet);

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
                standaloneChildren, shortClassIds, preComputedElementNames, renderedCode, result);
        }

        return result;
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
                    // Add within clause if needed
                    if (!model.Definition.ModelicaCode.StartsWith("within"))
                    {
                        var parent = model.ParentModelName;
                        if (!string.IsNullOrEmpty(parent))
                            model.Definition.ModelicaCode = string.Concat("within ", parent, ";\n", model.Definition.ModelicaCode);
                        else
                            model.Definition.ModelicaCode = "within;\n" + model.Definition.ModelicaCode;
                        model.Definition.ParsedCode = null;
                    }

                    // Parse if needed
                    if (model.Definition.ParsedCode == null)
                    {
                        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(model.Definition.ModelicaCode);
                        model.Definition.ParsedCode = parseTree;
                        foreach (var error in errors)
                        {
                            Error("ModelicaPackageSaver", $"Parse error in {model.Id} at line {error.Line}: {error.Message}");
                        }
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
    /// </summary>
    private static Dictionary<string, HashSet<string>> ComputeStandaloneChildren(
        List<ModelNode> allModels,
        Dictionary<string, List<ModelNode>> childrenByParent)
    {
        var result = new Dictionary<string, HashSet<string>>();

        foreach (var kvp in childrenByParent)
        {
            var parentId = kvp.Key;
            var children = kvp.Value;
            var standaloneNames = new HashSet<string>();

            // Detect case-insensitive duplicate names
            var nameCounts = children
                .GroupBy(m => m.Definition.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var child in children)
            {
                var canStore = child.CanBeStoredStandalone;

                var lowerName = child.Definition.Name.ToLowerInvariant();
                var hasCaseInsensitiveDuplicate = nameCounts[lowerName] > 1;
                var conflictsWithPackageMo = lowerName == "package";

                if (canStore && !hasCaseInsensitiveDuplicate && !conflictsWithPackageMo)
                {
                    standaloneNames.Add(child.Definition.Name);
                }
            }

            result[parentId] = standaloneNames;
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
        bool oneOfEachSection,
        bool importsFirst,
        bool componentsBeforeClasses,
        HashSet<string>? excludedModelIds = null)
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

                    // Skip formatting for excluded models — use original code
                    if (excludedModelIds != null && excludedModelIds.Contains(model.Id))
                    {
                        renderedCode[model.Id] = model.Definition.ModelicaCode;
                        model.Definition.ParsedCode = null;
                        return;
                    }

                    var classType = model.ClassType;

                    var isShortClass = IsShortClassDefinition(model);

                    // Determine which children to exclude (for packages)
                    HashSet<string>? classNamesToExclude = null;
                    if (classType == "package" && !isShortClass)
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
                        oneOfEachSection: oneOfEachSection,
                        importsFirst: importsFirst,
                        componentsBeforeClasses: componentsBeforeClasses);
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
        SaveResult result)
    {
        if (savedModels.Contains(model.Id))
            return;

        savedModels.Add(model.Id);

        if (!renderedCode.TryRemove(model.Id, out var code))
            return;

        var classType = model.ClassType;

        // Use pre-computed short class status since parse trees have been released
        var isShortClass = shortClassIds.Contains(model.Id);

        if (classType == "package" && !isShortClass)
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
                File.WriteAllText(packageFile, code);
                result.WrittenFiles.Add(packageFile);
                result.ModelIdToFilePath[model.Id] = packageFile;
            }
            catch (Exception e)
            {
                Error("ModelicaPackageSaver", $"Failed to write package file: {packageFile}", e);
            }

            // Update ModelicaCode with the rendered version to free the old source string
            model.Definition.ModelicaCode = code;

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
                        File.WriteAllLines(packageOrderFile, packageOrderList);
                        result.WrittenFiles.Add(packageOrderFile);
                    }
                    catch (Exception e)
                    {
                        Error("ModelicaPackageSaver", $"Failed to write package.order: {packageOrderFile}", e);
                    }
                }
            }

            // Get standalone children for this package
            standaloneChildren.TryGetValue(model.Id, out var standaloneNames);
            standaloneNames ??= new HashSet<string>();

            // Process children
            foreach (var child in children)
            {
                if (standaloneNames.Contains(child.Definition.Name))
                {
                    // Recursively write standalone child
                    WriteModelFiles(child, packageDir, allModels, savedModels, childrenByParent,
                        standaloneChildren, shortClassIds, preComputedElementNames, renderedCode, result);
                }
                else
                {
                    // Non-standalone children are in package.mo — update their ModelicaCode
                    // with the rendered version so the displayed code matches what was saved
                    UpdateNestedChildren(child, packageFile, savedModels, childrenByParent,
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
                File.WriteAllText(filePath, code);
                result.WrittenFiles.Add(filePath);
                result.ModelIdToFilePath[model.Id] = filePath;
            }
            catch (Exception e)
            {
                Error("ModelicaPackageSaver", $"Failed to write model file: {filePath}", e);
            }

            // Update ModelicaCode with the rendered version to free the old source string
            model.Definition.ModelicaCode = code;

            // Update non-standalone children embedded in this model (e.g., nested classes
            // inside a model/block/connector) so their displayed code matches what was saved
            if (childrenByParent.TryGetValue(model.Id, out var nestedChildren))
            {
                foreach (var child in nestedChildren)
                {
                    UpdateNestedChildren(child, filePath, savedModels, childrenByParent,
                        renderedCode, result);
                }
            }
        }
    }

    /// <summary>
    /// Recursively updates ModelicaCode for a non-standalone model and all its descendants.
    /// These models are embedded in their parent's file and don't get written separately,
    /// but their in-memory ModelicaCode must reflect the formatted version.
    /// </summary>
    private static void UpdateNestedChildren(
        ModelNode model,
        string containingFilePath,
        HashSet<string> savedModels,
        Dictionary<string, List<ModelNode>> childrenByParent,
        ConcurrentDictionary<string, string> renderedCode,
        SaveResult result)
    {
        savedModels.Add(model.Id);
        result.ModelIdToFilePath[model.Id] = containingFilePath;
        if (renderedCode.TryRemove(model.Id, out var childCode))
            model.Definition.ModelicaCode = childCode;

        // Recurse into this model's own nested children
        if (childrenByParent.TryGetValue(model.Id, out var grandchildren))
        {
            foreach (var grandchild in grandchildren)
            {
                UpdateNestedChildren(grandchild, containingFilePath, savedModels,
                    childrenByParent, renderedCode, result);
            }
        }
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
    private static bool IsShortClassDefinition(ModelNode model)
    {
        if (model.Definition.ParsedCode == null)
            return false;

        // Look for short_class_specifier in the parsed code
        foreach (var classDefContext in model.Definition.ParsedCode.class_definition())
        {
            var classSpecifier = classDefContext.class_specifier();
            if (classSpecifier?.short_class_specifier() != null)
            {
                return true;
            }
        }

        return false;
    }

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
}

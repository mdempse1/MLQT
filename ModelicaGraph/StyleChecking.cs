using System.Collections.Concurrent;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Helper class for ModelicaSyntaxVisitor containing formatting and analysis functions.
/// </summary>
public static class StyleChecking
{
    private static readonly IReadOnlySet<string> NoInheritedNames = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Applies the style checks to a model and returns the results as legacy
    /// <see cref="LogMessage"/>s. Thin projection over <see cref="RunStyleCheckingFindings"/> kept
    /// for existing consumers (GUI, MCP) — new code should prefer the structured findings.
    /// </summary>
    /// <param name="_currentModel">The model to check</param>
    /// <param name="settings">Style checking settings</param>
    /// <param name="fullModelId">The fully qualified model ID (e.g., "MyPackage.MySubPackage.MyModel")</param>
    public static List<LogMessage> RunStyleChecking(
        ModelDefinition _currentModel,
        StyleCheckingSettings settings,
        string fullModelId = "",
        IReadOnlySet<string>? knownModelIds = null,
        SpellChecker? spellChecker = null,
        IReadOnlySet<string>? knownModelNames = null,
        bool isExcludedFromFormatting = false,
        Func<string, string, bool>? baseClassHasIcon = null,
        NamingConventionConfig? namingConfig = null,
        Func<string, IReadOnlySet<string>>? inheritedElementNames = null,
        Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? unitLookup = null)
        => RunStyleCheckingFindings(_currentModel, settings, fullModelId, knownModelIds, spellChecker,
                knownModelNames, isExcludedFromFormatting, baseClassHasIcon, namingConfig: namingConfig,
                inheritedElementNames: inheritedElementNames, unitLookup: unitLookup)
            .Select(f => f.ToLogMessage())
            .ToList();

    /// <summary>
    /// Applies the style checks to a model and returns structured <see cref="Finding"/>s carrying
    /// rule id, severity, element identity, and a reformat-stable fingerprint. Foundation entry
    /// point for the CI pipeline, baseline/ratchet, and dashboard.
    /// </summary>
    /// <param name="_currentModel">The model to check</param>
    /// <param name="settings">Style checking settings</param>
    /// <param name="fullModelId">The fully qualified model ID (e.g., "MyPackage.MySubPackage.MyModel")</param>
    public static List<Finding> RunStyleCheckingFindings(
        ModelDefinition _currentModel,
        StyleCheckingSettings settings,
        string fullModelId = "",
        IReadOnlySet<string>? knownModelIds = null,
        SpellChecker? spellChecker = null,
        IReadOnlySet<string>? knownModelNames = null,
        bool isExcludedFromFormatting = false,
        Func<string, string, bool>? baseClassHasIcon = null,
        bool honorSuppressions = true,
        NamingConventionConfig? namingConfig = null,
        Func<string, IReadOnlySet<string>>? inheritedElementNames = null,
        Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? unitLookup = null)
    {
        List<Finding> findings = new();
        _currentModel.StyleRulesChecked = true;

        // Skip parsing entirely if no style rules are enabled
        if (!settings.HasAnyStyleRuleEnabled)
            return findings;

        // A library the user excluded (typically a test-case or example library sharing the repository)
        // is loaded and still counts as a user of what it references, but nothing in it is reported.
        // The check lives here, in the per-class primitive, so every surface inherits it: the GUI's
        // worker and its combined deferred pass, the CLI, and MCP all funnel through this method.
        if (settings.IsLibraryExcluded(fullModelId))
            return findings;

        var parsedCode = _currentModel.EnsureParsed();
        if (parsedCode == null)
            return findings;

        // The package the class sits in, for when its own source carries no within clause.
        var basePackage = ModelicaName.EnclosingPackageOf(fullModelId);

        if (settings.ParameterHasDescription || settings.ConstantHasDescription)
        {
            var visitor = new PublicParametersAndConstantsHaveDescription(settings.ParameterHasDescription, settings.ConstantHasDescription, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        // Skip formatting-related style rules for models excluded from formatting
        if (!isExcludedFromFormatting)
        {
            if (settings.ImportStatementsFirst)
            {
                var visitor = new ImportStatementsFirst(settings.ImportStatementsFirst, basePackage);
                visitor.VisitStored_definition(parsedCode);
                findings.AddRange(visitor.Findings);

                var visitor2 = new ExtendsClausesAtTop(false, basePackage);
                visitor2.VisitStored_definition(parsedCode);
                findings.AddRange(visitor2.Findings);
            }
            if (settings.InitialEQAlgoFirst || settings.InitialEQAlgoLast)
            {
                var visitor = new InitialEquationFirst(settings.InitialEQAlgoFirst, settings.InitialEQAlgoLast, basePackage);
                visitor.VisitStored_definition(parsedCode);
                findings.AddRange(visitor.Findings);
            }
            if (settings.OneOfEachSection || settings.DontMixEquationAndAlgorithm)
            {
                var visitor = new OneOfEachSection(settings.OneOfEachSection, settings.OneOfEachSection, settings.OneOfEachSection, settings.OneOfEachSection, !settings.DontMixEquationAndAlgorithm, basePackage);
                visitor.VisitStored_definition(parsedCode);
                findings.AddRange(visitor.Findings);
            }
            if (settings.DontMixConnections)
            {
                var visitor = new MixConnectionsAndEquations(basePackage);
                visitor.VisitStored_definition(parsedCode);
                findings.AddRange(visitor.Findings);
            }
        }
        if (settings.ClassHasDescription)
        {
            var visitor = new CheckClassDescriptionStrings(basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.ClassHasDocumentationInfo || settings.ClassHasDocumentationRevisions || settings.ClassHasIcon)
        {
            var visitor = new CheckClassAnnotations(
                settings.ClassHasDocumentationInfo, settings.ClassHasDocumentationRevisions,
                settings.ClassHasIcon, basePackage, baseClassHasIcon);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.ValidateModelReferences && knownModelIds != null)
        {
            var visitor = new CheckModelReferences(knownModelIds, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.SpellCheckDescription && spellChecker != null)
        {
            var visitor = new SpellCheckDescriptions(spellChecker, knownModelNames, basePackage, inheritedElementNames);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.SpellCheckDocumentation && spellChecker != null)
        {
            var visitor = new SpellCheckDocumentation(spellChecker, knownModelNames, basePackage, inheritedElementNames);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.FollowNamingConvention)
        {
            // Derived from the settings, so it is the same for every class in a run. Callers that
            // check more than one class build it once and pass it in — see StyleCheckContext. The
            // fallback keeps the single-class callers (a snippet, a test) working unchanged.
            var config = namingConfig ?? settings.NamingConvention.ToConfig();
            var visitor = new FollowNamingConvention(config, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.CheckDuplicateDeclarations || settings.CheckDuplicateImports)
        {
            var visitor = new DuplicateDeclarations(settings.CheckDuplicateDeclarations, settings.CheckDuplicateImports, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.CheckMissingUnits)
        {
            var visitor = new MissingUnits(basePackage, unitLookup);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        // MLQT.Unused.Import is not here: an import is visible to every class lexically nested below
        // it, which in a library means other files entirely, so it is decided by UnusedImportAnalyzer
        // over the graph rather than by looking at the declaring class on its own.

        // Stamp the configured severity on each finding (visitors emit at the default level).
        // A finding only exists because its rule ran, so a resolved severity of Off would be a
        // contradiction; the fallback to the rule's default keeps such a finding reportable instead
        // of emitting one at a severity that means "disabled". It used to be load-bearing, because
        // MLQT.Style.ExtendsAtTop had no setting to resolve and so always came back Off; that rule
        // now resolves through its governor (RuleDefinition.GovernedBy), and this is a net.
        for (int i = 0; i < findings.Count; i++)
        {
            var sev = settings.SeverityFor(findings[i].RuleId);
            if (sev == RuleSeverity.Off)
                sev = RuleCatalog.DefaultSeverityFor(findings[i].RuleId);
            findings[i] = findings[i] with { Severity = sev };
        }

        // Drop findings the author has intentionally waived via __MLQT annotations.
        if (honorSuppressions && findings.Count > 0)
        {
            var extractor = new MlqtSuppressionExtractor(basePackage);
            extractor.VisitStored_definition(parsedCode);
            var suppressions = extractor.Build();
            if (!suppressions.IsEmpty)
                findings = findings.Where(f => !suppressions.IsSuppressed(f)).ToList();
        }

        // Collapse exact-duplicate findings. Some visitors emit the same finding more than once — e.g. a
        // word misspelled repeatedly in one documentation block, or a section flagged per occurrence — so
        // the same (rule, element, discriminator, line) can appear twice. Each distinct issue counts once.
        if (findings.Count > 1)
        {
            var seen = new HashSet<(string, string, string?, string?, int)>();
            findings = findings
                .Where(f => seen.Add((f.RuleId, f.ModelId, f.ElementPath, f.Discriminator, f.LineNumber)))
                .ToList();
        }

        return findings;
    }

    /// <summary>
    /// Creates the type lookup the missing-unit rule needs: for a type as written in a class, whether
    /// it is a Real-derived quantity and whether its type chain fixes a unit. Returns null if the
    /// graph is null, leaving the rule to judge plain <c>Real</c> only.
    ///
    /// <para>The same <see cref="UnitResolver"/> the Unit coverage dimension uses, with the same
    /// per-class import scope, so the rule and the dashboard cannot disagree about what counts as a
    /// quantity or as united. A library's SI types resolve once and are then cached: the alias chains
    /// are shallow but they are asked about for every declaration in the library.</para>
    /// </summary>
    public static Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? CreateUnitLookup(
        DirectedGraph? graph)
    {
        if (graph == null) return null;

        var unitCache = new ConcurrentDictionary<string, (bool, bool)>(StringComparer.Ordinal);
        var importsByModel = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        return (modelId, typeName) =>
        {
            var imports = importsByModel.GetOrAdd(modelId, id => ImportsOf(graph, id));
            return UnitResolver.Resolve(graph, modelId, typeName, imports, unitCache);
        };
    }

    /// <summary>The import clauses of a class, which decide what a short type name means in it.</summary>
    private static IReadOnlyList<string> ImportsOf(DirectedGraph graph, string modelId)
    {
        var node = graph.GetNode<ModelNode>(modelId);
        var tree = node?.Definition.EnsureParsed();
        if (tree is null)
            return [];

        return ClassInterfaceExtractor.Extract(tree).Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();
    }

    /// <summary>
    /// Creates a lookup giving the element names a class inherits — the components and nested classes
    /// declared anywhere up its <c>extends</c> chain — for use as spell-check context words.
    /// Returns null if the graph is null, in which case only a class's own declarations are known.
    ///
    /// <para>Descriptions and documentation routinely name inherited members ("Temperature at
    /// port_a"), and port_a is declared in a base class two packages away. Without the chain those
    /// names are unknown words, so a library built on shared base classes reports a spelling finding
    /// for every one of them.</para>
    ///
    /// <para>Answered once per class and cached: the class is checked by both spell-check visitors,
    /// and resolving the chain parses each base class it reaches.</para>
    /// </summary>
    public static Func<string, IReadOnlySet<string>>? CreateInheritedElementNamesCallback(DirectedGraph? graph)
    {
        if (graph == null) return null;

        // Shared across the parallel per-class checks, hence concurrent.
        var inheritedNames = new ConcurrentDictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        return modelId => inheritedNames.GetOrAdd(modelId, id => CollectInheritedElementNames(graph, id));
    }

    private static IReadOnlySet<string> CollectInheritedElementNames(DirectedGraph graph, string modelId)
    {
        var node = graph.GetNode<ModelNode>(modelId);
        if (node == null)
            return NoInheritedNames;

        // Protected members are visible to a derived class, so they are names its prose can use.
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in ClassElementResolver.Collect(
                     graph, node, includeProtected: true, includeInherited: true))
        {
            // Only what came from a base class: the class's own declarations are collected by the
            // visitor, which also has them for classes the graph does not know.
            if (element.InheritedFrom == null)
                continue;

            if (element.Element.Kind is ClassElementKind.Component or ClassElementKind.Class)
                names.Add(element.Element.Name);
        }

        return names.Count == 0 ? NoInheritedNames : names;
    }

    /// <summary>
    /// Creates a callback that checks whether a base class (or any of its ancestors)
    /// has an Icon annotation, using the graph to resolve model names.
    /// Returns null if the graph is null (no inheritance checking possible).
    /// </summary>
    public static Func<string, string, bool>? CreateBaseClassHasIconCallback(DirectedGraph? graph)
    {
        if (graph == null) return null;

        // "Does this class, or anything it inherits from, have an icon" is a property of the class
        // alone, so it is answered once per class rather than once per class that extends it.
        // Without this the answer is recomputed — reparsing the class and re-running the extractor —
        // for every derived class, and the classes at the top of a hierarchy are exactly the ones
        // with the most derivations: in a library built on Modelica.Icons.* or a vendor's icon
        // package, the same handful of base classes are walked thousands of times.
        //
        // Shared across the parallel per-class checks, hence concurrent.
        var iconInChain = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        var resolved = new ConcurrentDictionary<(string Name, string Context), string?>();

        return (baseClassName, currentModelFullId) =>
            HasIconInInheritanceChain(
                graph, baseClassName, currentModelFullId, new HashSet<string>(StringComparer.Ordinal),
                iconInChain, resolved);
    }

    /// <summary>
    /// Recursively checks whether a base class or any of its ancestors has an Icon annotation.
    /// </summary>
    private static bool HasIconInInheritanceChain(
        DirectedGraph graph, string baseClassName, string currentModelFullId, HashSet<string> visited,
        ConcurrentDictionary<string, bool> iconInChain,
        ConcurrentDictionary<(string, string), string?> resolvedNames)
        => Walk(graph, baseClassName, currentModelFullId, visited, iconInChain, resolvedNames).HasIcon;

    /// <summary>
    /// One step of the walk.
    ///
    /// <para><paramref name="Complete"/> reports whether the answer describes the class on its own
    /// merits, or whether the walk was cut short by meeting a name already on the current path — a
    /// cycle, which is invalid Modelica but must not be allowed to poison the cache with a
    /// truncated "no". Only complete answers are remembered.</para>
    /// </summary>
    private readonly record struct IconWalkResult(bool HasIcon, bool Complete)
    {
        public static IconWalkResult Found { get; } = new(true, true);
        public static IconWalkResult NotFound { get; } = new(false, true);
        public static IconWalkResult Truncated { get; } = new(false, false);
    }

    private static IconWalkResult Walk(
        DirectedGraph graph, string baseClassName, string currentModelFullId, HashSet<string> visited,
        ConcurrentDictionary<string, bool> iconInChain,
        ConcurrentDictionary<(string, string), string?> resolvedNames)
    {
        // Name resolution walks up the package hierarchy probing the graph, and the same
        // (name, context) pair recurs constantly across a library — cache it alongside the answer.
        var resolvedId = resolvedNames.GetOrAdd(
            (baseClassName, currentModelFullId),
            key => ResolveModelName(graph, key.Item1, key.Item2));

        // A name that resolves to nothing is a settled answer, not a truncated one: there is no
        // class here to have an icon.
        if (resolvedId == null)
            return IconWalkResult.NotFound;

        if (iconInChain.TryGetValue(resolvedId, out var cached))
            return cached ? IconWalkResult.Found : IconWalkResult.NotFound;

        if (!visited.Add(resolvedId))
            return IconWalkResult.Truncated;

        var result = Resolve();
        if (result.Complete)
            iconInChain[resolvedId] = result.HasIcon;

        return result;

        IconWalkResult Resolve()
        {
            var node = graph.GetNode<ModelNode>(resolvedId);
            if (node == null)
                return IconWalkResult.NotFound;

            // Parse the model and extract icon + extends information
            var parsedCode = node.Definition.EnsureParsed();
            if (parsedCode == null)
                return IconWalkResult.NotFound;

            var extracted = IconExtractor.ExtractIconWithInheritance(parsedCode);
            if (extracted == null)
                return IconWalkResult.NotFound;

            // This model directly has an Icon annotation
            if (extracted.Icon != null)
                return IconWalkResult.Found;

            // Recursively check this model's base classes
            var complete = true;
            foreach (var ancestorName in extracted.ExtendsClasses)
            {
                var ancestor = Walk(
                    graph, ancestorName, resolvedId, visited, iconInChain, resolvedNames);
                if (ancestor.HasIcon)
                    return IconWalkResult.Found;

                complete &= ancestor.Complete;
            }

            return complete ? IconWalkResult.NotFound : IconWalkResult.Truncated;
        }
    }

    /// <summary>
    /// Resolves a raw class name (possibly relative) to a fully qualified model ID
    /// by walking up the package hierarchy of the current model.
    /// </summary>
    private static string? ResolveModelName(DirectedGraph graph, string rawName, string currentModelFullId)
    {
        // Try the raw name as-is (already fully qualified)
        if (graph.GetNode<ModelNode>(rawName) != null)
            return rawName;

        // Walk up the package hierarchy of the current model
        var lastDot = currentModelFullId.LastIndexOf('.');
        var pkg = lastDot > 0 ? currentModelFullId[..lastDot] : null;

        while (!string.IsNullOrEmpty(pkg))
        {
            var qualifiedName = $"{pkg}.{rawName}";
            if (graph.GetNode<ModelNode>(qualifiedName) != null)
                return qualifiedName;

            var dotIdx = pkg.LastIndexOf('.');
            pkg = dotIdx > 0 ? pkg[..dotIdx] : null;
        }

        return null;
    }
}
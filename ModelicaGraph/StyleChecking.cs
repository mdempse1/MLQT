using System.Collections.Concurrent;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// Helper class for ModelicaSyntaxVisitor containing formatting and analysis functions.
/// </summary>
public static class StyleChecking
{

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
        Func<string, string, bool>? baseClassHasIcon = null)
        => RunStyleCheckingFindings(_currentModel, settings, fullModelId, knownModelIds, spellChecker,
                knownModelNames, isExcludedFromFormatting, baseClassHasIcon)
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
        bool honorSuppressions = true)
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

        // Calculate the base package (everything except the last component of fullModelId)
        // This is used when the code doesn't have a within clause
        string basePackage = "";
        if (!string.IsNullOrEmpty(fullModelId))
        {
            var lastDot = fullModelId.LastIndexOf('.');
            if (lastDot > 0)
            {
                basePackage = fullModelId.Substring(0, lastDot);
            }
        }

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
            var visitor = new SpellCheckDescriptions(spellChecker, knownModelNames, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.SpellCheckDocumentation && spellChecker != null)
        {
            var visitor = new SpellCheckDocumentation(spellChecker, knownModelNames, basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        if (settings.FollowNamingConvention)
        {
            var config = settings.NamingConvention.ToConfig();
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
            var visitor = new MissingUnits(basePackage);
            visitor.VisitStored_definition(parsedCode);
            findings.AddRange(visitor.Findings);
        }
        // MLQT.Unused.Import is not here: an import is visible to every class lexically nested below
        // it, which in a library means other files entirely, so it is decided by UnusedImportAnalyzer
        // over the graph rather than by looking at the declaring class on its own.

        // Stamp the configured severity on each finding (visitors emit at the default level).
        // A finding only exists because its rule ran, so a resolved severity of Off (e.g. the
        // ExtendsAtTop rule, which is coupled to ImportStatementsFirst rather than independently
        // toggled) falls back to the rule's default enabled severity.
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
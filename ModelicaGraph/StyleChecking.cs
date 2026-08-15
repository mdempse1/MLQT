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

        return (baseClassName, currentModelFullId) =>
            HasIconInInheritanceChain(graph, baseClassName, currentModelFullId, new HashSet<string>());
    }

    /// <summary>
    /// Recursively checks whether a base class or any of its ancestors has an Icon annotation.
    /// </summary>
    private static bool HasIconInInheritanceChain(
        DirectedGraph graph, string baseClassName, string currentModelFullId, HashSet<string> visited)
    {
        var resolvedId = ResolveModelName(graph, baseClassName, currentModelFullId);
        if (resolvedId == null || !visited.Add(resolvedId))
            return false;

        var node = graph.GetNode<ModelNode>(resolvedId);
        if (node == null)
            return false;

        // Parse the model and extract icon + extends information
        var parsedCode = node.Definition.EnsureParsed();
        if (parsedCode == null)
            return false;

        var result = IconExtractor.ExtractIconWithInheritance(parsedCode);
        if (result == null)
            return false;

        // This model directly has an Icon annotation
        if (result.Icon != null)
            return true;

        // Recursively check this model's base classes
        foreach (var ancestorName in result.ExtendsClasses)
        {
            if (HasIconInInheritanceChain(graph, ancestorName, resolvedId, visited))
                return true;
        }

        return false;
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
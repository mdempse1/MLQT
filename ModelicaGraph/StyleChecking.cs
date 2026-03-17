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
    /// Applies the style checks to a model
    /// </summary>
    /// <param name="_currentModel">The model to check</param>
    /// <param name="settings">Style checking settings</param>
    /// <param name="fullModelId">The fully qualified model ID (e.g., "MyPackage.MySubPackage.MyModel")</param>
    /// <summary>
    /// Applies the style checks to a model (synchronous version for parallel processing).
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
    {
        List<LogMessage> violations = new();
        _currentModel.StyleRulesChecked = true;

        // Skip parsing entirely if no style rules are enabled
        if (!settings.HasAnyStyleRuleEnabled)
            return violations;

        var parsedCode = _currentModel.EnsureParsed();
        if (parsedCode == null)
            return violations;

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
            violations.AddRange(visitor.RuleViolations);
        }
        // Skip formatting-related style rules for models excluded from formatting
        if (!isExcludedFromFormatting)
        {
            if (settings.ImportStatementsFirst)
            {
                var visitor = new ImportStatementsFirst(settings.ImportStatementsFirst, basePackage);
                visitor.VisitStored_definition(parsedCode);
                violations.AddRange(visitor.RuleViolations);

                var visitor2 = new ExtendsClausesAtTop(false, basePackage);
                visitor2.VisitStored_definition(parsedCode);
                violations.AddRange(visitor2.RuleViolations);
            }
            if (settings.InitialEQAlgoFirst || settings.InitialEQAlgoLast)
            {
                var visitor = new InitialEquationFirst(settings.InitialEQAlgoFirst, settings.InitialEQAlgoLast, basePackage);
                visitor.VisitStored_definition(parsedCode);
                violations.AddRange(visitor.RuleViolations);
            }
            if (settings.OneOfEachSection || settings.DontMixEquationAndAlgorithm)
            {
                var visitor = new OneOfEachSection(settings.OneOfEachSection, settings.OneOfEachSection, settings.OneOfEachSection, settings.OneOfEachSection, !settings.DontMixEquationAndAlgorithm, basePackage);
                visitor.VisitStored_definition(parsedCode);
                violations.AddRange(visitor.RuleViolations);
            }
            if (settings.DontMixConnections)
            {
                var visitor = new MixConnectionsAndEquations(basePackage);
                visitor.VisitStored_definition(parsedCode);
                violations.AddRange(visitor.RuleViolations);
            }
        }
        if (settings.ClassHasDescription)
        {
            var visitor = new CheckClassDescriptionStrings(basePackage);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        if (settings.ClassHasDocumentationInfo || settings.ClassHasDocumentationRevisions || settings.ClassHasIcon)
        {
            var visitor = new CheckClassAnnotations(
                settings.ClassHasDocumentationInfo, settings.ClassHasDocumentationRevisions,
                settings.ClassHasIcon, basePackage, baseClassHasIcon);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        if (settings.ValidateModelReferences && knownModelIds != null)
        {
            var visitor = new CheckModelReferences(knownModelIds, basePackage);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        if (settings.SpellCheckDescription && spellChecker != null)
        {
            var visitor = new SpellCheckDescriptions(spellChecker, knownModelNames, basePackage);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        if (settings.SpellCheckDocumentation && spellChecker != null)
        {
            var visitor = new SpellCheckDocumentation(spellChecker, knownModelNames, basePackage);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        if (settings.FollowNamingConvention)
        {
            var config = settings.NamingConvention.ToConfig();
            var visitor = new FollowNamingConvention(config, basePackage);
            visitor.VisitStored_definition(parsedCode);
            violations.AddRange(visitor.RuleViolations);
        }
        return violations;
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
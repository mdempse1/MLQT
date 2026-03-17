using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
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
        bool isExcludedFromFormatting = false)
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
                var visitor = new InitialEquationFirst(settings.InitialEQAlgoFirst, basePackage);
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

}
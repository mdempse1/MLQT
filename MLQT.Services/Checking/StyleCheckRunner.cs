using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>Runs the style/spell checking pipeline using a pre-built <see cref="StyleCheckContext"/>.</summary>
public static class StyleCheckRunner
{
    /// <summary>Runs the checks for one model and returns structured findings.</summary>
    public static List<Finding> RunFindings(
        ModelNode node, StyleCheckingSettings settings, StyleCheckContext context,
        bool honorSuppressions = true)
    {
        var findings = StyleChecking.RunStyleCheckingFindings(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon, honorSuppressions: honorSuppressions);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return findings;
    }

    /// <summary>Legacy LogMessage projection for existing (GUI/MCP) consumers.</summary>
    public static List<LogMessage> Run(ModelNode node, StyleCheckingSettings settings, StyleCheckContext context)
    {
        var violations = StyleChecking.RunStyleChecking(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return violations;
    }

    public static List<LogMessage> RunStateless(string source, StyleCheckingSettings settings, StyleCheckContext context)
    {
        var definition = new ModelDefinition("Snippet", source);
        return StyleChecking.RunStyleChecking(
            definition, settings, fullModelId: string.Empty,
            knownModelIds: null, spellChecker: context.SpellChecker, knownModelNames: null,
            isExcludedFromFormatting: false, baseClassHasIcon: null);
    }
}

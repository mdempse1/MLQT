using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// Runs the style/spell checking pipeline using a pre-built <see cref="StyleCheckContext"/>.
///
/// <para>This is the per-model entry point every surface funnels through — the GUI's background
/// workers, the CLI and MCP — which is why the guard against checking a class recovered from an
/// encrypted library's documentation lives here rather than in any one caller. Those classes are
/// reconstructions, not source: a finding about one would be a finding about MLQT's own summary of
/// a third-party library the user cannot edit. The GUI reaches this method directly rather than
/// through <see cref="LibraryCheckSession"/>, so a filter there alone left it unprotected.</para>
/// </summary>
public static class StyleCheckRunner
{
    /// <summary>Runs the checks for one model and returns structured findings.</summary>
    public static List<Finding> RunFindings(
        ModelNode node, StyleCheckingSettings settings, StyleCheckContext context,
        bool honorSuppressions = true)
    {
        if (node.IsExternalStub)
            return [];

        var findings = StyleChecking.RunStyleCheckingFindings(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon, honorSuppressions: honorSuppressions,
            namingConfig: context.NamingConfig);

        // While the tree is still here. The dashboard would otherwise parse this class again to ask
        // the same questions, once for every scope it appears in.
        context.Coverage?.Measure(node);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return findings;
    }

    /// <summary>Legacy LogMessage projection for existing (GUI/MCP) consumers.</summary>
    public static List<LogMessage> Run(ModelNode node, StyleCheckingSettings settings, StyleCheckContext context)
    {
        if (node.IsExternalStub)
            return [];

        var violations = StyleChecking.RunStyleChecking(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon, namingConfig: context.NamingConfig);

        // While the tree is still here — see RunFindings.
        context.Coverage?.Measure(node);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return violations;
    }

    public static List<LogMessage> RunStateless(string source, StyleCheckingSettings settings, StyleCheckContext context)
    {
        var definition = new ModelDefinition("Snippet", source);
        return StyleChecking.RunStyleChecking(
            definition, settings, fullModelId: string.Empty,
            knownModelIds: null, spellChecker: context.SpellChecker, knownModelNames: null,
            isExcludedFromFormatting: false, baseClassHasIcon: null, namingConfig: context.NamingConfig);
    }
}

using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// Runs the style/spell checking pipeline using a pre-built <see cref="StyleCheckContext"/>.
///
/// <para>It also holds every check to its own subject. A visitor walks into a nested class carrying
/// <c>replaceable</c>/<c>redeclare</c> — deliberately, because a check with no graph behind it has
/// nothing else that would ever look at one — and attributes what it finds there to the nested class.
/// When that class has a node of its own it is checked in its own right as well, so the parent's pass
/// is a second copy of every finding: same rule, same element, same fingerprint. Worse, the copy's
/// line was counted from the parent's source while naming the nested class, so a report mapping it to
/// a file pointed at a line belonging to something else. Dropping it here, where the class under check
/// is known, fixes both and takes nothing away from the snippet case.</para>
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

        var findings = OnlyAbout(node, context, StyleChecking.RunStyleCheckingFindings(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon, honorSuppressions: honorSuppressions,
            namingConfig: context.NamingConfig, inheritedElementNames: context.InheritedElementNames,
            unitLookup: context.UnitLookup));

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

        // Through the structured path, so the same "only about this class" rule applies as in
        // RunFindings rather than the projection quietly keeping the duplicates.
        var findings = OnlyAbout(node, context, StyleChecking.RunStyleCheckingFindings(
            node.Definition, settings, node.Id, context.KnownModelIds, context.SpellChecker, context.KnownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: context.BaseClassHasIcon, namingConfig: context.NamingConfig,
            inheritedElementNames: context.InheritedElementNames, unitLookup: context.UnitLookup));

        // While the tree is still here — see RunFindings.
        context.Coverage?.Measure(node);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return findings.Select(f => f.ToLogMessage()).ToList();
    }

    /// <summary>
    /// The findings that are about <paramref name="node"/> itself. One naming another class is the
    /// parent's second copy of a nested class's finding, and is dropped only when that class is in
    /// the graph and therefore gets a check of its own — which is what keeps a snippet check, where
    /// nothing else will ever look at it, reporting everything it found.
    /// </summary>
    private static List<Finding> OnlyAbout(
        ModelNode node, StyleCheckContext context, List<Finding> findings)
    {
        if (context.ClassesCheckedSeparately is not { } checkedSeparately)
            return findings;

        return findings
            .Where(f => f.ModelId == node.Id || !checkedSeparately.Contains(f.ModelId))
            .ToList();
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

using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Runs the style/spell checking pipeline with the same contextual inputs the MLQT background
/// worker uses (known model ids/names for reference validation and spell-check context, a spell
/// checker when spelling rules are on, and a base-class icon callback for icon inheritance).
/// </summary>
internal static class StyleCheckRunner
{
    /// <summary>Check one loaded model, wiring graph-derived context.</summary>
    public static List<LogMessage> Run(
        ModelNode node,
        StyleCheckingSettings settings,
        DirectedGraph graph,
        IStyleCheckingService styleService)
    {
        IReadOnlySet<string>? knownModelIds = null;
        if (settings.ValidateModelReferences)
            knownModelIds = graph.ModelNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        SpellChecker? spellChecker = null;
        IReadOnlySet<string>? knownModelNames = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
        {
            // Public EnsureSpellChecker() uses the default bundled dictionaries (en_US/en_GB) plus the
            // user's custom dictionary. Language selection is not settable via the service interface.
            spellChecker = styleService.EnsureSpellChecker();
            knownModelNames = graph.ModelNodes
                .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var baseClassHasIcon = settings.ClassHasIcon
            ? StyleChecking.CreateBaseClassHasIconCallback(graph)
            : null;

        var violations = StyleChecking.RunStyleChecking(
            node.Definition, settings, node.Id, knownModelIds, spellChecker, knownModelNames,
            isExcludedFromFormatting: settings.IsModelExcludedFromFormatting(node.Id),
            baseClassHasIcon: baseClassHasIcon);

        node.Definition.ParsedCode = null; // release the parse tree to bound memory
        return violations;
    }

    /// <summary>Check an arbitrary source snippet with no loaded graph. Reference validation and
    /// icon-inheritance cannot run without a graph; spelling and structural rules still apply.</summary>
    public static List<LogMessage> RunStateless(
        string source,
        StyleCheckingSettings settings,
        IStyleCheckingService styleService)
    {
        var definition = new ModelDefinition("Snippet", source);

        SpellChecker? spellChecker = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            spellChecker = styleService.EnsureSpellChecker();

        return StyleChecking.RunStyleChecking(
            definition, settings, fullModelId: string.Empty,
            knownModelIds: null, spellChecker: spellChecker, knownModelNames: null,
            isExcludedFromFormatting: false, baseClassHasIcon: null);
    }
}

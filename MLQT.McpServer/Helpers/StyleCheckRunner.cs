using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The contextual inputs the style/spell pipeline needs, built ONCE per check operation (rather
/// than per model): known model ids/names for reference validation and spell-check context, a spell
/// checker for the chosen languages, and a base-class icon callback for icon inheritance.
/// </summary>
internal sealed class StyleCheckContext
{
    public IReadOnlySet<string>? KnownModelIds { get; private init; }
    public IReadOnlySet<string>? KnownModelNames { get; private init; }
    public SpellChecker? SpellChecker { get; private init; }
    public Func<string, string, bool>? BaseClassHasIcon { get; private init; }

    /// <summary>Context for checking loaded models against a graph.</summary>
    public static StyleCheckContext Build(
        StyleCheckingSettings settings,
        DirectedGraph graph,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager)
    {
        IReadOnlySet<string>? knownModelIds = null;
        if (settings.ValidateModelReferences)
            knownModelIds = graph.ModelNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        SpellChecker? spellChecker = null;
        IReadOnlySet<string>? knownModelNames = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
        {
            spellChecker = SpellCheckerFactory.Build(settings.SpellCheckLanguages, customDictionary, dictionaryManager);
            knownModelNames = graph.ModelNodes
                .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return new StyleCheckContext
        {
            KnownModelIds = knownModelIds,
            KnownModelNames = knownModelNames,
            SpellChecker = spellChecker,
            BaseClassHasIcon = settings.ClassHasIcon ? StyleChecking.CreateBaseClassHasIconCallback(graph) : null,
        };
    }

    /// <summary>Context for checking an arbitrary snippet (no graph): only a spell checker applies.</summary>
    public static StyleCheckContext BuildStateless(
        StyleCheckingSettings settings,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager)
    {
        SpellChecker? spellChecker = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            spellChecker = SpellCheckerFactory.Build(settings.SpellCheckLanguages, customDictionary, dictionaryManager);

        return new StyleCheckContext { SpellChecker = spellChecker };
    }
}

/// <summary>Runs the style/spell checking pipeline using a pre-built <see cref="StyleCheckContext"/>.</summary>
internal static class StyleCheckRunner
{
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

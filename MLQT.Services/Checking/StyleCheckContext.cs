using ModelicaGraph;
using ModelicaParser.SpellChecking;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// The contextual inputs the style/spell pipeline needs, built ONCE per check operation (rather
/// than per model): known model ids/names for reference validation and spell-check context, a spell
/// checker for the chosen languages, and a base-class icon callback for icon inheritance.
/// </summary>
public sealed class StyleCheckContext
{
    public IReadOnlySet<string>? KnownModelIds { get; private init; }
    public IReadOnlySet<string>? KnownModelNames { get; private init; }
    public SpellChecker? SpellChecker { get; private init; }
    public Func<string, string, bool>? BaseClassHasIcon { get; private init; }

    /// <summary>Context for checking loaded models against a graph, building a spell checker from the
    /// dictionary services for the settings' languages (used by the CLI and MCP).</summary>
    public static StyleCheckContext Build(
        StyleCheckingSettings settings,
        DirectedGraph graph,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager)
    {
        SpellChecker? spellChecker = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            spellChecker = SpellCheckerFactory.Build(settings.SpellCheckLanguages, customDictionary, dictionaryManager);

        return Build(settings, graph, spellChecker);
    }

    /// <summary>Context for checking loaded models against a graph, reusing an already-built spell
    /// checker (used by the GUI's background workers, which cache a spell checker with reload logic).
    /// Single source of truth for the known-ids / known-names / icon-callback derivation so the GUI
    /// can't drift from the CLI/MCP.</summary>
    public static StyleCheckContext Build(
        StyleCheckingSettings settings,
        DirectedGraph graph,
        SpellChecker? spellChecker)
    {
        IReadOnlySet<string>? knownModelIds = null;
        if (settings.ValidateModelReferences)
            knownModelIds = graph.ModelNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        IReadOnlySet<string>? knownModelNames = null;
        if ((settings.SpellCheckDescription || settings.SpellCheckDocumentation) && spellChecker != null)
            knownModelNames = graph.ModelNodes
                .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

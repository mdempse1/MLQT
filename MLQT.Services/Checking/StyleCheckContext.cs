using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
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

    /// <summary>
    /// Every class the graph holds, which is every class that gets a check of its own. Null for a
    /// check with no graph behind it (a snippet), where nothing else will report a nested class and
    /// its parent's walk is all there is. See <see cref="StyleCheckRunner"/>.
    /// </summary>
    public IReadOnlySet<string>? ClassesCheckedSeparately { get; private init; }
    public IReadOnlySet<string>? KnownModelNames { get; private init; }
    public SpellChecker? SpellChecker { get; private init; }
    public Func<string, string, bool>? BaseClassHasIcon { get; private init; }

    /// <summary>
    /// The element names a class inherits, for the spell checkers' context words. Null when there is
    /// no graph to resolve base classes with, in which case only a class's own declarations count.
    /// </summary>
    public Func<string, IReadOnlySet<string>>? InheritedElementNames { get; private init; }

    /// <summary>
    /// Whether a declared type is a Real-derived quantity and whether it fixes a unit, for the
    /// missing-unit rule. Null when there is no graph, leaving the rule to judge plain <c>Real</c>
    /// only — all a snippet check can honestly say.
    /// </summary>
    public Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? UnitLookup { get; private init; }

    /// <summary>
    /// Measures each class's coverage contribution as it is checked, or null when the caller does not
    /// want coverage collected.
    ///
    /// <para>Checking already parses every class and throws the tree away; measuring there costs the
    /// measurement alone and saves the dashboard a whole parse pass over the library. Off by default
    /// because the measurement is real work — around a second per thousand classes — and a CI run that
    /// never asks for coverage should not pay for it.</para>
    /// </summary>
    public CoverageMeasurer? Coverage { get; private init; }

    /// <summary>
    /// The naming rules in the form the visitor wants them. Derived purely from the settings, so it
    /// is the same for every class in a run — it belongs here with the other once-per-run inputs
    /// rather than being rebuilt, with its dictionaries and sets, for each of a library's thousands
    /// of classes.
    /// </summary>
    public NamingConventionConfig? NamingConfig { get; private init; }

    /// <summary>Context for checking loaded models against a graph, building a spell checker from the
    /// dictionary services for the settings' languages (used by the CLI and MCP).</summary>
    /// <param name="repositoryRoot">Root of the repository whose libraries are being checked, which
    /// is where its accepted spellings live. Null when the caller has no repository — a snippet, or a
    /// library loaded on its own — in which case there are no accepted words rather than someone
    /// else's.</param>
    public static StyleCheckContext Build(
        StyleCheckingSettings settings,
        DirectedGraph graph,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        string? repositoryRoot = null,
        bool collectCoverage = false)
    {
        SpellChecker? spellChecker = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            spellChecker = SpellCheckerFactory.Build(
                settings.SpellCheckLanguages, customDictionary.WordsFor(repositoryRoot), dictionaryManager);

        return Build(settings, graph, spellChecker, collectCoverage);
    }

    /// <summary>Context for checking loaded models against a graph, reusing an already-built spell
    /// checker (used by the GUI's background workers, which cache a spell checker with reload logic).
    /// Single source of truth for the known-ids / known-names / icon-callback derivation so the GUI
    /// can't drift from the CLI/MCP.</summary>
    public static StyleCheckContext Build(
        StyleCheckingSettings settings,
        DirectedGraph graph,
        SpellChecker? spellChecker,
        bool collectCoverage = false)
    {
        // Every class in the graph. Needed unconditionally (see ClassesCheckedSeparately), and the
        // reference-validation rule wants the same set, so it is built once.
        var classIds = graph.ModelNodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string>? knownModelIds = settings.ValidateModelReferences ? classIds : null;

        IReadOnlySet<string>? knownModelNames = null;
        if ((settings.SpellCheckDescription || settings.SpellCheckDocumentation) && spellChecker != null)
            knownModelNames = graph.ModelNodes
                .Select(n => n.Id.Contains('.') ? n.Id[(n.Id.LastIndexOf('.') + 1)..] : n.Id)
                .ToHashSet(StringComparer.Ordinal);   // Modelica is case sensitive

        return new StyleCheckContext
        {
            KnownModelIds = knownModelIds,
            ClassesCheckedSeparately = classIds,
            KnownModelNames = knownModelNames,
            SpellChecker = spellChecker,
            BaseClassHasIcon = settings.ClassHasIcon ? StyleChecking.CreateBaseClassHasIconCallback(graph) : null,
            // Descriptions and documentation name inherited members as freely as declared ones, so
            // the chain is followed whenever anything is being spell checked.
            InheritedElementNames = spellChecker != null && (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
                ? StyleChecking.CreateInheritedElementNamesCallback(graph)
                : null,
            // The rule resolves types the same way the Unit coverage dimension does, so the findings
            // and the dashboard describe the same gaps.
            UnitLookup = settings.CheckMissingUnits ? StyleChecking.CreateUnitLookup(graph) : null,
            NamingConfig = settings.FollowNamingConvention ? settings.NamingConvention.ToConfig() : null,
            // Measured for what this repository tracks: a rule nobody enabled buys a tree walk
            // per class for a row the report will not show.
            Coverage = collectCoverage
                ? new CoverageMeasurer(graph, CoverageDimensions.TrackedFor(settings))
                : null,
        };
    }

    /// <summary>Context for checking an arbitrary snippet (no graph): only a spell checker applies.</summary>
    public static StyleCheckContext BuildStateless(
        StyleCheckingSettings settings,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        string? repositoryRoot = null)
    {
        SpellChecker? spellChecker = null;
        if (settings.SpellCheckDescription || settings.SpellCheckDocumentation)
            spellChecker = SpellCheckerFactory.Build(
                settings.SpellCheckLanguages, customDictionary.WordsFor(repositoryRoot), dictionaryManager);

        return new StyleCheckContext
        {
            SpellChecker = spellChecker,
            NamingConfig = settings.FollowNamingConvention ? settings.NamingConvention.ToConfig() : null,
        };
    }
}

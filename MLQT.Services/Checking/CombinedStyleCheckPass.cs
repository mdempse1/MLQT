using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// The style check that rides along with dependency analysis.
/// </summary>
/// <remarks>
/// <para>Dependency analysis parses every class. Checking each one while its parse tree is still in
/// hand saves a second pass over the whole library, which on a library the size of MSL is the
/// difference between one long wait and two.</para>
///
/// <para>Extracted from <c>MainLayout</c> in phase 7a-4, and the piece of that method most worth
/// moving: it had drifted from the paths it is meant to agree with <b>twice</b>. Spelling,
/// model-reference and inherited-icon rules were once skipped here entirely because it derived the
/// checker's inputs by hand, and the naming configuration was rebuilt per class. Both are why the
/// inputs now come from <see cref="StyleCheckContext.Build"/> — the same builder the background
/// worker and the CLI use — rather than being assembled here.</para>
/// </remarks>
public static class CombinedStyleCheckPass
{
    /// <summary>
    /// Builds the per-class action dependency analysis runs, and the bag it fills.
    /// </summary>
    /// <param name="graph">The loaded graph.</param>
    /// <param name="repositories">Every repository, for its settings and accepted spellings.</param>
    /// <param name="modelToSettings">Which rules apply to each class (see <see cref="ModelScope"/>).</param>
    /// <param name="repositoryByModel">Which repository each class came from, where it has one.</param>
    /// <param name="spellCheckerFor">
    /// The spell checker for a repository, or null when no spelling rule is on. Passed in because
    /// building one reads that repository's accepted-spellings file.
    /// </param>
    /// <param name="findings">Filled as classes are checked; the caller owns it.</param>
    /// <returns>
    /// The per-class action, and the contexts it checks through, keyed by repository id — the empty
    /// key being the default. The contexts are returned rather than kept private because each holds
    /// a <c>CoverageMeasurer</c> that accumulates as the pass runs, and coverage is a result of the
    /// pass in the same way the findings are.
    /// </returns>
    public static (Action<ModelNode> Pass, IReadOnlyDictionary<string, StyleCheckContext> Contexts) Build(
        DirectedGraph graph,
        IEnumerable<Repository> repositories,
        IReadOnlyDictionary<string, StyleCheckingSettings> modelToSettings,
        IReadOnlyDictionary<string, string> repositoryByModel,
        Func<Repository, SpellChecker?> spellCheckerFor,
        ConcurrentBag<LogMessage> findings)
    {
        // One context per repository, built once rather than per class. Each carries the spell
        // checker, the known-ids set, the inherited-name and icon callbacks and the unit lookup —
        // all of which are per-run inputs, and all of which this pass used to derive for itself.
        var contexts = new Dictionary<string, StyleCheckContext>(StringComparer.Ordinal);
        foreach (var repository in repositories.Where(r => r.StyleSettings is not null))
        {
            contexts[repository.Id] = StyleCheckContext.Build(
                repository.StyleSettings!, graph, spellCheckerFor(repository), collectCoverage: true);
        }

        // For a class in a library that belongs to no repository: the tool's default rules, and no
        // accepted spellings, because there is no repository to have accepted any.
        contexts[""] = StyleCheckContext.Build(
            new StyleCheckingSettings(), graph, spellChecker: null, collectCoverage: true);

        Action<ModelNode> pass = model =>
        {
            // Every class, including non-standalone ones. CanBeStoredStandalone is a
            // non-deterministic file-storage property, not a style-check gate, and filtering on it
            // made GUI counts unstable and inconsistent with the CLI and MCP.
            if (!modelToSettings.TryGetValue(model.Id, out var settings))
                return;

            var repositoryId = repositoryByModel.TryGetValue(model.Id, out var id) ? id : "";
            if (!contexts.TryGetValue(repositoryId, out var context))
                context = contexts[""];

            if (settings.HasAnyStyleRuleEnabled)
            {
                foreach (var finding in StyleCheckRunner.Run(model, settings, context))
                    findings.Add(finding);
            }
            else
            {
                // A class in a library with no rules enabled still counts towards coverage, which
                // reports the state of the code rather than the result of the rules.
                context.Coverage?.Measure(model);
            }
        };

        return (pass, contexts);
    }
}

using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for running style checking rules against Modelica models.
/// Handles background processing, queue management, and progress reporting.
/// </summary>
public interface IStyleCheckingService
{
    /// <summary>
    /// Event fired when style checking progress changes and passes true when all checks are complete
    /// </summary>
    event Action<bool>? OnProgressChanged;

    /// <summary>
    /// Event fired when new violations are found.
    /// </summary>
    event Action<List<LogMessage>>? OnViolationsFound;

    /// <summary>
    /// Gets whether style checking is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Completes when the run started by the last Start* call has finished — workers, whole-graph
    /// analyses and the final flush. Returns an already-completed task when nothing is running, so a
    /// caller can always await it. This is how a caller knows the work is over: the Start* methods
    /// queue and return, so they say nothing about when checking finishes.
    /// </summary>
    Task WaitForCompletionAsync();

    /// <summary>
    /// Gets the number of models queued for checking.
    /// </summary>
    int QueuedCount { get; }

    /// <summary>
    /// The spell checker for one repository, built on demand and cached until that repository's word
    /// list or the installed language dictionaries change.
    ///
    /// <para>Scoped to a repository because the accepted words are: a single shared checker would
    /// hand one repository's spellings to another, and the reason the lists moved into each
    /// repository was to stop exactly that kind of leakage between what different tools and projects
    /// consider correct.</para>
    /// </summary>
    /// <param name="repositoryRoot">Root of the repository whose words apply.</param>
    /// <param name="languages">Language codes to load; the repository's configured languages when
    /// omitted for an already-built checker.</param>
    SpellChecker EnsureSpellChecker(string repositoryRoot, IEnumerable<string>? languages = null);

    /// <summary>
    /// Runs style checking on a single model.
    /// </summary>
    /// <param name="model">The model definition to check.</param>
    /// <param name="settings">The style checking settings to be used.</param>
    /// <returns>List of rule violations found.</returns>
    Task<List<LogMessage>> CheckModelAsync(ModelDefinition model, StyleCheckingSettings settings);

    /// <summary>
    /// Queues all models in a repository for background style checking, including the whole-graph
    /// analyses. Running dependency analysis first is handled internally when an enabled rule needs it.
    /// </summary>
    /// <param name="repository">The repository to be checked.</param>
    Task StartBackgroundCheckingAsync(Repository repository);

    /// <summary>
    /// Queues all models in a repository for background style checking, including the whole-graph
    /// analyses. Running dependency analysis first is handled internally when an enabled rule needs it.
    /// </summary>
    /// <param name="repository">The repository to be checked.</param>
    void StartBackgroundChecking(Repository repository);

    /// <summary>
    /// Queues all models across multiple repositories for background style checking, including the
    /// whole-graph analyses. Repositories with no enabled style rules are skipped.
    /// Fires <see cref="OnProgressChanged"/> with <c>true</c> only after all repositories
    /// are processed, avoiding premature completion signals when some repos are skipped.
    /// </summary>
    /// <param name="repositories">The repositories to check.</param>
    void StartBackgroundCheckingForRepositories(IReadOnlyList<Repository> repositories);

    /// <summary>
    /// Checks only specific models and updates their violations.
    /// Clears previous violations for the specified models before checking.
    /// </summary>
    /// <param name="modelIds">The model IDs to check.</param>
    /// <param name="graph">The graph containing the models.</param>
    Task CheckModelsAsync(IEnumerable<string> modelIds, DirectedGraph graph);

    /// <summary>
    /// Returns the spell checker configured for the given settings (matching the language dictionaries),
    /// or null when spell checking is disabled. Lets callers build the same spell-checking context the
    /// background workers use.
    /// </summary>
    /// <param name="repository">The repository being checked — its settings choose the languages,
    /// and its word list supplies the accepted spellings.</param>
    SpellChecker? GetSpellCheckerIfNeeded(Repository repository);

    /// <summary>
    /// Runs the whole-graph analyses (package.order, uses hygiene, unused class/member, shadowing) for
    /// the given repositories and delivers their findings. Dependency analysis is run first when an
    /// enabled rule needs the edges, so the dependency-based analyzers always see a complete graph.
    /// Any stale graph findings are cleared first, so this is safe to call after a combined
    /// dependency+style pass that only ran the per-model rules.
    ///
    /// The other <c>StartBackgroundChecking*</c> entry points already include a graph-analysis pass;
    /// this exists for the deferred pipeline, which runs the per-model rules itself.
    /// </summary>
    /// <param name="repositories">The repositories to analyse.</param>
    /// <returns>A task that completes once the findings have been delivered.</returns>
    Task RunGraphAnalysesForRepositoriesAsync(IReadOnlyList<Repository> repositories);

}

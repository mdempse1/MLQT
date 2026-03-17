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
    /// Gets the number of models queued for checking.
    /// </summary>
    int QueuedCount { get; }

    /// <summary>
    /// Gets the current spell checker instance, or null if not yet created.
    /// </summary>
    SpellChecker? GetSpellChecker();

    /// <summary>
    /// Ensures a spell checker instance exists, creating one if needed.
    /// </summary>
    /// <param name="customWords">Optional custom words to include.</param>
    SpellChecker EnsureSpellChecker(IEnumerable<string>? customWords = null);

    /// <summary>
    /// Recreates the spell checker instance with updated custom words.
    /// Call this when the custom dictionary changes.
    /// </summary>
    /// <param name="customWords">Optional custom words to include.</param>
    void ReloadSpellChecker(IEnumerable<string>? customWords = null);

    /// <summary>
    /// Runs style checking on a single model.
    /// </summary>
    /// <param name="model">The model definition to check.</param>
    /// <param name="settings">The style checking settings to be used.</param>
    /// <returns>List of rule violations found.</returns>
    Task<List<LogMessage>> CheckModelAsync(ModelDefinition model, StyleCheckingSettings settings);

    /// <summary>
    /// Queues all models in a repository for background style checking.
    /// </summary>
    /// <param name="repository">The repository to be checked.</param>
    Task StartBackgroundCheckingAsync(Repository repository);

    /// <summary>
    /// Queues all models in a repository for background style checking.
    /// </summary>
    /// <param name="repository">The repository to be checked.</param>
    void StartBackgroundChecking(Repository repository);

    /// <summary>
    /// Queues all models across multiple repositories for background style checking.
    /// Repositories with no enabled style rules are skipped.
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

}

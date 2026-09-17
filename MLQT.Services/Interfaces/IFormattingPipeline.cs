using ModelicaGraph;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Applies a repository's formatting rules to the files on disk.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>MainLayout</c> in phase 7a-4. There are two ways MLQT writes formatted
/// Modelica, they are reached from different places, and they used to be two private methods on a
/// 3,000-line layout component — where the only way to run either was to start the application.
/// <b>B65</b> was a defect that existed because one of them had drifted from the other.</para>
///
/// <para>The interface exists as much for the phase 7a-6 test host as for the desktop app: the
/// journey "change a repository setting, formatting reruns, the file on disk changes" has to drive
/// the same implementation the app does, and it cannot render a layout to get at it.</para>
/// </remarks>
public interface IFormattingPipeline
{
    /// <summary>
    /// Reformats the VCS-modified and untracked files of every repository that has formatting
    /// switched on.
    /// </summary>
    /// <remarks>
    /// The incremental path, run at startup and after every VCS operation. It is the assumption the
    /// whole design rests on: a repository is already formatted, so only what changed needs writing.
    /// Reference-only repositories are skipped — the settings page promises they are never written.
    /// </remarks>
    /// <returns>How many files were rewritten.</returns>
    Task<int> FormatModifiedFilesAsync();

    /// <summary>
    /// Rewrites every file of every library, restructuring the layout to match the current rules.
    /// </summary>
    /// <remarks>
    /// The full pass behind <b>Format All Files</b>, and after a change to the formatting rules. It
    /// moves classes between files, deletes what the new layout orphans and updates the graph to
    /// match, so it is not an expensive version of the incremental path — it is a different
    /// operation.
    /// </remarks>
    /// <param name="filterRepositoryId">One repository, or null for every library.</param>
    /// <param name="onLibraryFailed">
    /// Told which library failed and why, so a host can surface it. A library that will not save is
    /// not a reason to abandon the others.
    /// </param>
    Task SaveAllLibrariesWithFormattingAsync(
        string? filterRepositoryId = null,
        Action<string, Exception>? onLibraryFailed = null);

    /// <summary>
    /// Reformats a named set of files — what a VCS operation or a commit touched.
    /// </summary>
    /// <remarks>
    /// The same incremental write as <see cref="FormatModifiedFilesAsync"/>, for a set of files the
    /// caller already knows. Kept on this interface rather than called directly so every write MLQT
    /// makes is recorded in one place: the monitor tells MLQT's writes from the user's by their
    /// timestamps, and a write recorded somewhere else starts a formatting pass on its own output.
    /// </remarks>
    Task FormatChangedFilesAsync(IEnumerable<string> changedFilePaths, StyleCheckingSettings styleSettings);

    /// <summary>
    /// Files this pipeline has written, and when. A change whose timestamp matches one of these is
    /// MLQT's own write rather than the user's, and must not start another pass.
    /// </summary>
    IReadOnlyDictionary<string, DateTime> WrittenFileTimestamps { get; }

    /// <summary>Forgets the recorded write times, after the file monitor has been restarted.</summary>
    void ClearWrittenFileTimestamps();
}

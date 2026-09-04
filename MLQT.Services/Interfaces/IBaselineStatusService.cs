using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Classifies the desktop app's issue list against each repository's committed baseline, so a user
/// can see only what their working copy has changed rather than the whole standing debt.
///
/// "Touched" here means <b>modified in the working copy and not yet committed</b> — deliberately not
/// the CLI's commit-to-commit <c>--changed-from</c>. In the app the question a user is asking is "what
/// have I done to this library right now", and the answer must not depend on which commit they happen
/// to be sitting on.
/// </summary>
public interface IBaselineStatusService
{
    /// <summary>True when at least one loaded repository has a baseline to compare against.</summary>
    bool HasBaseline { get; }

    /// <summary>Number of files whose changes are pending commit, across all repositories.</summary>
    int TouchedFileCount { get; }

    /// <summary>
    /// Where the issue stands relative to its repository's baseline, or <c>null</c> when there is no
    /// baseline for it — in which case the caller should show it rather than hide it, since "not
    /// classifiable" is not the same as "already accepted".
    /// </summary>
    FindingStatus? StatusOf(LogMessage message);

    /// <summary>
    /// The current classification. Read it once and use that instance for a whole pass — it never
    /// changes under you, where repeated calls through this interface could straddle a refresh.
    /// </summary>
    BaselineStatusSnapshot Snapshot { get; }

    /// <summary>Reloads the baselines and re-reads which files are pending commit.</summary>
    void Refresh();

    /// <summary>Raised after <see cref="Refresh"/> changes anything a view is showing.</summary>
    event Action? OnChanged;
}

using System.Text.Json;
using ModelicaGraph.Analysis;

namespace MLQT.Services.Helpers;

/// <summary>
/// Reads and appends the coverage-trend history (a list of <see cref="MetricsSnapshot"/>) to a JSON
/// file. A repository-backed library keeps its history in the version-controlled
/// <c>&lt;repo&gt;/.mlqt/metrics-history.json</c> (see <see cref="RepoPath"/>) so the burndown travels
/// with the library and every reviewer sees the same data; libraries not backed by a repository fall
/// back to the per-user <see cref="DefaultPath"/>. Corrupt or missing files are treated as an empty
/// history rather than throwing, so the dashboard is always usable.
/// </summary>
public static class MetricsHistoryStore
{
    private const int MaxSnapshots = 500;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Per-user fallback location, used for libraries not loaded from a repository.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT", "metrics-history.json");

    /// <summary>The shared, version-controllable history for a repository-backed library:
    /// <c>&lt;localPath&gt;/.mlqt/metrics-history.json</c> — the same <c>.mlqt</c> folder that holds the
    /// per-repo <c>settings.json</c>, so committing it shares the burndown with the whole team.</summary>
    public static string RepoPath(string repositoryLocalPath)
        => Path.Combine(repositoryLocalPath, ".mlqt", "metrics-history.json");

    /// <summary>Load the history, oldest first. Never throws; returns an empty list on any problem.</summary>
    public static List<MetricsSnapshot> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new();
            return JsonSerializer.Deserialize<List<MetricsSnapshot>>(File.ReadAllText(path)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Why <see cref="AppendIfChanged"/> did or did not write.</summary>
    public enum AppendOutcome
    {
        /// <summary>The snapshot was added.</summary>
        Appended,

        /// <summary>The history already has a point for this revision.</summary>
        RevisionAlreadyRecorded,

        /// <summary>The numbers are identical to the most recent point for this scope.</summary>
        Unchanged
    }

    /// <summary>
    /// Append a snapshot only if it says something new, and report which.
    ///
    /// This is what makes recording from CI safe. A CI job that commits the updated history file
    /// triggers a build of its own commit; that build measures the same library and would append an
    /// identical point, commit again, and loop forever. Skipping an unchanged point breaks the cycle
    /// after one extra run without depending on the CI system's path filters or <c>[skip ci]</c>
    /// conventions being configured correctly.
    ///
    /// Revision is checked as well so rebuilding the same commit (a retry, a re-run of an old build)
    /// does not stack duplicate points on one revision.
    /// </summary>
    public static (AppendOutcome Outcome, List<MetricsSnapshot> History) AppendIfChanged(
        string path, MetricsSnapshot snapshot)
    {
        var history = Load(path);
        var sameScope = history.Where(s => (s.Scope ?? "") == (snapshot.Scope ?? "")).ToList();

        if (snapshot.Revision is not null &&
            sameScope.Any(s => string.Equals(s.Revision, snapshot.Revision, StringComparison.Ordinal)))
            return (AppendOutcome.RevisionAlreadyRecorded, history);

        var latest = sameScope.OrderBy(s => s.TimestampUtc).LastOrDefault();
        if (snapshot.HasSameMetricsAs(latest))
            return (AppendOutcome.Unchanged, history);

        return (AppendOutcome.Appended, Append(path, snapshot));
    }

    /// <summary>Append a snapshot and persist, keeping at most the most recent <see cref="MaxSnapshots"/>.</summary>
    public static List<MetricsSnapshot> Append(string path, MetricsSnapshot snapshot)
    {
        var history = Load(path);
        history.Add(snapshot);
        if (history.Count > MaxSnapshots)
            history.RemoveRange(0, history.Count - MaxSnapshots);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(history, Options));
        return history;
    }
}

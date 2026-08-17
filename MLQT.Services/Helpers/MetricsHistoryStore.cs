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

using System.Text.Json;
using ModelicaGraph.Analysis;

namespace MLQT.Services.Helpers;

/// <summary>
/// Reads and appends the coverage-trend history (a list of <see cref="MetricsSnapshot"/>) to a JSON
/// file. The default location is <c>%LocalAppData%/MLQT/metrics-history.json</c>. Corrupt or missing
/// files are treated as an empty history rather than throwing, so the dashboard is always usable.
/// </summary>
public static class MetricsHistoryStore
{
    private const int MaxSnapshots = 500;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT", "metrics-history.json");

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

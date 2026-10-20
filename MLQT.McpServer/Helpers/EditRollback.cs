using ModelicaParser.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Undoing a sequence of edits that has to succeed or fail as a whole: each file the sequence may touch
/// is read before the first edit, and every one that has changed is written back and reloaded. Used by
/// <c>mlqt_edit_class</c>, whose operations apply all together or not at all.
/// </summary>
internal static class EditRollback
{
    /// <summary>Read <paramref name="filePath"/> as it is now, unless it has already been read.</summary>
    public static async Task SnapshotAsync(Dictionary<string, string> snapshots, string filePath)
    {
        if (!snapshots.ContainsKey(filePath))
            snapshots[filePath] = await ModelicaFileEncoding.ReadAllTextOnlyAsync(filePath);
    }

    /// <summary>Write back every snapshotted file whose text has changed, then reload and refresh.</summary>
    public static async Task RestoreAsync(
        Dictionary<string, string> snapshots, ILibraryDataService libraries, IExternalResourceService resources,
        SessionState session)
    {
        var affected = new List<string>();
        foreach (var (path, original) in snapshots)
        {
            if (!File.Exists(path) || ModelicaFileEncoding.ReadAllTextOnly(path) == original)
                continue; // untouched (e.g. an op failed before writing this file)
            await ModelicaFileEncoding.WriteAllTextAsync(path, original);
            affected.AddRange(await libraries.ReloadFileAsync(path));
        }
        if (affected.Count > 0)
            await GraphRefresh.RefreshAfterEditAsync(affected, libraries, resources, session);
    }
}

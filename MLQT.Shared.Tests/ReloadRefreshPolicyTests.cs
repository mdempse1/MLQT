using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every reload the desktop app makes gives the reloaded classes their dependency edges back (B290).
/// </summary>
/// <remarks>
/// <para><c>ILibraryDataService.ReloadFileAsync</c> replaces each class in a file with a new node that
/// uses nothing, and the graph goes on saying its dependencies are analysed. Correcting a word in
/// MSL's <c>Der</c> from Code Review left it offering no classes to go to, with nothing to say why.
/// The MCP server refreshed after its edits and the desktop app never had.</para>
///
/// <para>So a reload in <c>MLQT.Shared</c> is followed by <c>RefreshDependenciesAsync</c> within a few
/// lines, or is in <see cref="Ledger"/> with the reason it does not need to be.</para>
/// </remarks>
public class ReloadRefreshPolicyTests
{
    /// <summary>How many lines after a reload the refresh may come.</summary>
    private const int Window = 6;

    /// <summary>Reloads that are refreshed some other way, by file name and the text of the call.</summary>
    private static readonly Dictionary<(string File, string Call), string> Ledger = new()
    {
        [("LibraryBrowser.razor.cs", "await LibraryDataService.ReloadFileAsync(fullPath);")] =
            "A revert. The loop reloads each reverted file and then fires VcsFilesChanged, whose pipeline " +
            "re-analyses the changed models' dependencies - once, after every file is back.",
    };

    private static string SharedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared");
            if (File.Exists(Path.Combine(candidate, "_Imports.razor")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MLQT.Shared sources not found");
    }

    private static List<(string File, int Line, string Text, bool Refreshed)> Reloads()
    {
        var found = new List<(string, int, string, bool)>();
        foreach (var file in Directory.EnumerateFiles(SharedDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || !lines[i].Contains(".ReloadFileAsync(", StringComparison.Ordinal))
                    continue;

                // The refresh may wrap the reload, or follow it.
                var refreshed = Enumerable.Range(Math.Max(0, i - 1), Math.Min(Window + 1, lines.Length - Math.Max(0, i - 1)))
                    .Any(k => lines[k].Contains("RefreshDependenciesAsync(", StringComparison.Ordinal));
                found.Add((Path.GetFileName(file), i + 1, trimmed, refreshed));
            }
        }
        return found;
    }

    [Fact]
    public void EveryReloadIsRefreshedOrLedgered()
    {
        var offenders = Reloads()
            .Where(r => !r.Refreshed && !Ledger.ContainsKey((r.File, r.Text)))
            .Select(r => $"{r.File}:{r.Line}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These reload a file without restoring its dependency edges, so the reloaded classes use " +
            "nothing while the graph says it is analysed (B290). Follow each with " +
            "LibraryDataService.RefreshDependenciesAsync(affected), or add it to the ledger with a reason: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void EveryLedgerEntryStillNamesAReload()
    {
        // An entry for a call that has gone would excuse whatever is written there next.
        var reloads = Reloads().Select(r => (r.File, r.Text)).ToHashSet();

        Assert.All(Ledger.Keys, key => Assert.Contains(key, reloads));
    }

    [Fact]
    public void TheScanFindsTheReloads()
    {
        // Guards the scan: a pattern that found nothing would pass the rule over an empty set.
        Assert.True(Reloads().Count >= 4);
    }
}

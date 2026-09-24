using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// "Which library does this class belong to?" is answered in one place —
/// <c>ILibraryDataService.GetOwningLibrary</c>, over <c>LibraryOwnership.Owner</c> — and never by
/// searching the loaded libraries' <c>ModelIds</c> (B268).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The search is the obvious way to write it, it reads correctly, and
/// it has been written wrong three times: <c>TotalModelCount</c> over-counted differently on each
/// launch, <c>DictionaryScope</c> picked whichever repository loaded first, and Code Review opened a
/// class of the user's own as a vendor's encrypted one with every diff view disabled. Each was fixed
/// where it hurt and the next caller wrote the search again. WP15 made the index true — an encrypted
/// library is no longer loaded beside source for the same library — which removes today's reason the
/// search goes wrong, but not the next one, and not the habit.</para>
///
/// <para><b>A ledger, as <c>ModelicaFileAccessPolicyTests</c> keeps one.</b> A scan cannot tell a
/// membership question ("is this id one of this library's?") from an ownership question ("which
/// library's is it?"), so every read of a library's <c>ModelIds</c> in production code has to be
/// accounted for with a reason. A new one fails until somebody says which kind it is. The count is
/// part of the entry so a second read in a file is not covered by the first one's reason.</para>
/// </remarks>
public class LibraryOwnershipPolicyTests
{
    /// <summary>A library's class list being searched. <c>knownModelIds.Contains</c> and the like
    /// are other sets and do not match: the dot before <c>ModelIds</c> is what makes it a
    /// member of something.</summary>
    private static readonly Regex LibraryMembershipRead = new(@"\.ModelIds\.Contains\(", RegexOptions.Compiled);

    private static readonly Dictionary<string, (int Count, string Reason)> Accounted = new(StringComparer.Ordinal)
    {
        ["MLQT.Services/Helpers/LibraryOwnership.cs"] = (1,
            "the one answer: collects the claimants, and asks the graph only when there are two"),
        ["MLQT.Services/LibraryDataService.cs"] = (1,
            "GetChildModelsAsync filters a package's children to the library already chosen as its "
            + "owner - membership of a known library, not a search for one"),
        ["MLQT.Services/Checking/DictionaryScope.cs"] = (1,
            "asks GetOwningLibrary first; searches only when the owner has no repository (a library "
            + "loaded from a bare directory), for another claimant that does"),
    };

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("could not find the repository root (MLQT.slnx)");
    }

    private static bool IsProduction(string relative) =>
        !relative.Contains(".Tests/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.Journeys/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.TestHost/", StringComparison.Ordinal)
        && !relative.StartsWith("TestSupport/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal)
        && !relative.Contains("/obj/", StringComparison.Ordinal);

    /// <summary>Comment-only lines removed, so the prose explaining this rule — the interface's own
    /// documentation quotes the search it forbids — does not count as a use of it.</summary>
    private static string WithoutCommentLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line =>
        {
            var trimmed = line.TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal)
                && !trimmed.StartsWith("/*", StringComparison.Ordinal);
        }));

    private static Dictionary<string, int> ReadsByFile()
    {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.Ordinal) || p.EndsWith(".razor", StringComparison.Ordinal))
            .Select(p => (Full: p, Relative: Path.GetRelativePath(root, p).Replace('\\', '/')))
            .Where(f => IsProduction(f.Relative))
            .Select(f => (f.Relative, Count: LibraryMembershipRead.Matches(WithoutCommentLines(File.ReadAllText(f.Full))).Count))
            .Where(f => f.Count > 0)
            .ToDictionary(f => f.Relative, f => f.Count, StringComparer.Ordinal);
    }

    [Fact]
    public void EveryReadOfALibrarysClassList_IsAccountedFor()
    {
        var unaccounted = ReadsByFile()
            .Where(kv => !Accounted.TryGetValue(kv.Key, out var entry) || kv.Value > entry.Count)
            .Select(kv => $"{kv.Key} ({kv.Value})")
            .ToList();

        Assert.True(unaccounted.Count == 0,
            "These read a library's ModelIds without an entry in the ledger. If the question is which "
            + "library a class belongs to, ask ILibraryDataService.GetOwningLibrary instead - searching the "
            + "list returns whichever library loaded first (B268). If it really is a membership test of a "
            + "library already chosen, add it to the ledger with the reason: "
            + string.Join(", ", unaccounted));
    }

    [Fact]
    public void TheLedgerHasNoStaleEntries()
    {
        // An entry for a read that has gone is a reason nobody will re-read, waiting to cover the next
        // one written in that file.
        var reads = ReadsByFile();
        var stale = Accounted
            .Where(kv => !reads.TryGetValue(kv.Key, out var count) || count < kv.Value.Count)
            .Select(kv => $"{kv.Key} (ledger {kv.Value.Count}, found {reads.GetValueOrDefault(kv.Key)})")
            .ToList();

        Assert.True(stale.Count == 0, "Ledger entries with fewer reads than recorded: " + string.Join(", ", stale));
    }

    [Fact]
    public void TheScanFindsTheReadsItIsHolding()
    {
        // Guards the scan itself: a root or a pattern that found nothing would pass the first test
        // over an empty set, which is the failure shape this repository keeps finding.
        Assert.Contains("MLQT.Services/Helpers/LibraryOwnership.cs", ReadsByFile().Keys);
        Assert.True(LibraryMembershipRead.IsMatch("Libraries.FirstOrDefault(l => l.ModelIds.Contains(id))"));
        Assert.False(LibraryMembershipRead.IsMatch("_knownModelIds.Contains(id)"));
    }
}

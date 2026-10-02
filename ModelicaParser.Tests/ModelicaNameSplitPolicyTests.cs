using System.Text.RegularExpressions;

namespace ModelicaParser.Tests;

/// <summary>
/// The rule that a Modelica name is taken apart only by <see cref="ModelicaParser.Helpers.ModelicaName"/>,
/// held to the production source.
///
/// <para><b>Why this exists.</b> A quoted identifier may contain a dot - <c>Lib.'a.b'.C</c> is three
/// segments - and about twenty places split names with <c>Split('.')</c> or cut them at
/// <c>LastIndexOf('.')</c>, so each of them read such a name differently from the others and from
/// the reference resolver, which did handle quotes. They were found by a review, not by a test, and
/// a new one would be found the same way: by nobody.</para>
///
/// <para><b>A ledger, as for file access.</b> A scan cannot tell a Modelica name from a version
/// string or an enumeration value, so every dot-split in production code has to be accounted for
/// with a reason, and a new one fails until somebody says which it is.</para>
/// </summary>
public class ModelicaNameSplitPolicyTests
{
    private static readonly Regex DotSplit = new(
        @"\.Split\('\.'\)|\.(Last)?IndexOf\('\.'(\s*,[^)]*)?\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Every accounted-for split on a dot, by repository-relative path, with how many and why none of
    /// them is a Modelica name. <b>If one is, the entry is wrong</b>: use <c>ModelicaName</c>.
    /// </summary>
    private static readonly Dictionary<string, (int Count, string Reason)> Accounted = new(StringComparer.Ordinal)
    {
        ["ModelicaParser/Visitors/IconExtractor.cs"] = (1, "an enumeration value such as FillPattern.Solid, "
            + "whose literals the language defines and no quoted identifier is among"),
        ["ModelicaParser/Helpers/ModelicaUriScanner.cs"] = (1, "a file extension in a modelica:// URI's path"),
        ["ModelicaParser/Icons/IconText.cs"] = (1, "a label value, taken as a name only when it is plain "
            + "identifier characters and dots - a quoted one is refused before the split"),
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

    // Production source only, and not the helper itself, which is the one place allowed to do it.
    private static bool IsProduction(string relative) =>
        !relative.Contains(".Tests/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.Journeys/", StringComparison.Ordinal)
        && !relative.StartsWith("MLQT.TestHost/", StringComparison.Ordinal)
        && !relative.StartsWith("TestSupport/", StringComparison.Ordinal)
        && !relative.Contains("/bin/", StringComparison.Ordinal)
        && !relative.Contains("/obj/", StringComparison.Ordinal)
        && !relative.StartsWith(".claude/", StringComparison.Ordinal)
        && relative != "ModelicaParser/Helpers/ModelicaName.cs";

    // Comment lines removed, so prose naming the forbidden calls is not counted.
    private static string WithoutCommentLines(string source) =>
        string.Join('\n', source.Split('\n')
            .Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("*", StringComparison.Ordinal)
                    && !trimmed.StartsWith("/*", StringComparison.Ordinal);
            }));

    private static Dictionary<string, int> DotSplitsByFile()
    {
        var root = RepositoryRoot();
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!IsProduction(relative))
                continue;
            var count = DotSplit.Matches(WithoutCommentLines(File.ReadAllText(file))).Count;
            if (count > 0)
                found[relative] = count;
        }
        return found;
    }

    [Fact]
    public void EveryDotSplitInProductionCode_IsAccountedFor()
    {
        var unaccounted = DotSplitsByFile()
            .Where(kv => !Accounted.TryGetValue(kv.Key, out var entry) || entry.Count != kv.Value)
            .Select(kv => $"{kv.Key}: {kv.Value} split(s) on a dot"
                          + (Accounted.TryGetValue(kv.Key, out var e) ? $", ledger says {e.Count}" : ", not in the ledger"))
            .ToList();

        Assert.True(unaccounted.Count == 0,
            "A Modelica name is taken apart with ModelicaName (Segments, EnclosingPackageOf, LeafOf, "
            + "RootLibraryOf, EnclosingNamesOf), which knows a quoted identifier may contain a dot. If this "
            + "split is not on a Modelica name, add it to the ledger with the reason:\n"
            + string.Join("\n", unaccounted));
    }

    [Fact]
    public void TheLedgerHasNoEntryForAFileThatNoLongerSplits()
    {
        var present = DotSplitsByFile();
        var stale = Accounted.Keys.Where(k => !present.ContainsKey(k)).ToList();

        Assert.True(stale.Count == 0, "Remove these ledger entries: " + string.Join(", ", stale));
    }
}

using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// That the tables in the planning documents and CLAUDE.md are still tables.
/// </summary>
/// <remarks>
/// <para>The backlog in <c>Design/backlog.md</c> is appended to by script far more often than by
/// hand. Two ways of breaking it silently have already happened, and neither
/// shows up until somebody looks at the rendered page:</para>
///
/// <list type="number">
///   <item><description>A blank line between two rows <b>ends the table</b>. Everything after it
///   renders as a second table with no header, or as plain text. The file still reads fine in an
///   editor.</description></item>
///   <item><description>An unescaped <c>|</c> in a cell splits that cell, <b>including inside
///   backticks</b> — GitHub-flavoured markdown does not treat inline code as protecting it. One row
///   quietly grew two extra columns because it quoted a rule severity as
///   <c>MLQT.Doc.ClassDescription | Off</c>.</description></item>
/// </list>
///
/// <para>Both are cheap to check and neither is visible in review, which is exactly the shape B97
/// named: a sweep worth running twice is worth a test.</para>
/// </remarks>
public class MarkdownTableTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    public static TheoryData<string> MarkdownFiles()
    {
        var root = RepositoryRoot();
        var data = new TheoryData<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Design"), "*.md"))
            data.Add(Path.GetRelativePath(root, file));

        data.Add("CLAUDE.md");
        return data;
    }

    /// <summary>Pipes that actually separate cells — an escaped <c>\|</c> is content.</summary>
    private static int CellSeparators(string line)
    {
        var count = 0;
        for (var i = 0; i < line.Length; i++)
            if (line[i] == '|' && (i == 0 || line[i - 1] != '\\'))
                count++;
        return count;
    }

    private static bool IsSeparatorRow(string line) =>
        Regex.IsMatch(line, @"^\|[\s:|-]+\|\s*$");

    [Theory]
    [MemberData(nameof(MarkdownFiles))]
    public void EveryRowHasTheSameNumberOfColumnsAsItsHeader(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), relativePath));
        var problems = new List<string>();

        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].StartsWith('|') || !IsSeparatorRow(lines[i + 1]))
                continue;

            var columns = CellSeparators(lines[i]);

            for (var row = i + 2; row < lines.Length && lines[row].StartsWith('|'); row++)
            {
                var actual = CellSeparators(lines[row]);
                if (actual != columns)
                {
                    // Naming the likely cause, because the fix is not obvious from the count alone.
                    problems.Add(
                        $"line {row + 1}: {actual} cell separators, header has {columns}"
                        + " — an unescaped '|' in a cell splits it, even inside backticks; write it as \\|");
                }
            }
        }

        Assert.True(problems.Count == 0, $"{relativePath}:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", problems));
    }

    private static string BacklogPath() => Path.Combine(RepositoryRoot(), "Design", "backlog.md");

    [Fact]
    public void TheBacklogTableIsNotSplitByABlankLine()
    {
        // The one that bit twice. A blank line between rows ends the table, so every row after it
        // loses its header - and the raw file looks entirely reasonable. Note the backlog is now
        // several tables rather than one, so this only fires on a break *between two rows*, which is
        // still the accident; a deliberate heading between groups has prose around it.
        var lines = File.ReadAllLines(BacklogPath());

        var breaks = new List<int>();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var isBlank = lines[i].Trim().Length == 0;
            var betweenRows = Regex.IsMatch(lines[i - 1], BacklogRow)
                              && Regex.IsMatch(lines[i + 1], BacklogRow);

            if (isBlank && betweenRows)
                breaks.Add(i + 1);
        }

        Assert.True(breaks.Count == 0,
            "blank line(s) inside the backlog table, which ends it at that point: "
            + string.Join(", ", breaks.Select(b => $"line {b}")));
    }

    /// <summary>
    /// A backlog row, open or closed. The id cell carries a tick once the item is done, so a
    /// pattern that insisted on <c>| Bnnn |</c> counted only the open ones — which made the row
    /// count a measure of how much work was outstanding rather than of whether the table still
    /// parses, and left a duplicate or a lost row among the closed ones invisible.
    /// </summary>
    private const string BacklogRow = @"^\| B(\d+)[^|]*\|";

    /// <summary>
    /// The ids are unique, unbroken above the watermark, and never reissued below it.
    /// </summary>
    /// <remarks>
    /// <para>Backlog ids are cited from code comments, test summaries, build scripts and CI
    /// workflows, so <b>an id is permanent</b>: a closed item's row leaves the file but its number
    /// is never given to something else. The file states both facts in prose - which range has been
    /// issued, and where new items start - and this reads that prose rather than carrying a second
    /// copy of it, so the document and the guard cannot disagree.</para>
    ///
    /// <para>The original version of this test asserted the ids ran unbroken from B1, which was true
    /// while nothing was ever removed. Closing out phases 1-7 retired most of B1-B167, so the invariant
    /// above the watermark is what is left of it: a gap there still means a row was lost by a
    /// scripted edit rather than deliberately retired.</para>
    /// </remarks>
    [Fact]
    public void TheBacklogIdsAreUniqueAndNeverReissued()
    {
        var text = File.ReadAllText(BacklogPath());

        var issuedThrough = int.Parse(
            Regex.Match(text, @"\*\*B1\s*[-–—]\s*B(\d+) have been issued").Groups[1].Value is { Length: > 0 } c
                ? c
                : throw new InvalidOperationException(
                    "Design/backlog.md no longer states which ids have been issued. The line reads "
                    + "'**B1-B<n> have been issued.**' and this test reads the number out of it."));

        var startAt = int.Parse(
            Regex.Match(text, @"New items start at \*\*B(\d+)\*\*").Groups[1].Value is { Length: > 0 } s
                ? s
                : throw new InvalidOperationException(
                    "Design/backlog.md no longer states where new ids start. The line reads "
                    + "'New items start at **B<n>**.' and this test reads the number out of it."));

        Assert.True(startAt == issuedThrough + 1,
            $"the backlog says B1-B{issuedThrough} have been issued but that new items start at "
            + $"B{startAt}; those two sentences have to agree or an id gets reissued");

        var ids = Regex.Matches(text, "(?m)" + BacklogRow).Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.True(ids.Count > 20, $"only found {ids.Count} backlog rows; the table format may have changed");

        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => $"B{g.Key}").ToList();
        Assert.True(duplicates.Count == 0, "duplicate backlog ids: " + string.Join(", ", duplicates));

        // Below the watermark only carried-forward ids may appear, and nothing may sit in the gap
        // between the issued range and the start of new items - which is what a reissued number, or
        // a watermark that was moved without moving the other sentence, would look like.
        var stranded = ids.Where(i => i > issuedThrough && i < startAt).ToList();
        Assert.True(stranded.Count == 0,
            "ids between the issued range and the start of new items: " + string.Join(", ", stranded.Select(i => $"B{i}")));

        var newIds = ids.Where(i => i >= startAt).ToList();
        if (newIds.Count == 0)
            return;

        var missing = Enumerable.Range(startAt, newIds.Max() - startAt + 1).Except(newIds).Select(i => $"B{i}").ToList();
        Assert.True(missing.Count == 0,
            $"gaps in the backlog ids above B{startAt}: " + string.Join(", ", missing)
            + " — a closed item's row is removed and its number retired, so a gap here means a row "
            + "was lost rather than closed; carry a retired id forward only when work is still attached to it");
    }
}

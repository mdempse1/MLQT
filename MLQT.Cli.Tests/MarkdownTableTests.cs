using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// That the tables in the planning documents and CLAUDE.md are still tables.
/// </summary>
/// <remarks>
/// <para>The backlog that lived in <c>Design/backlog.md</c> until 2026-09-28 was appended to by
/// script far more often than by hand, and two ways of breaking a table silently happened there.
/// Neither shows up until somebody looks at the rendered page:</para>
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

    private static string RoadmapPath() => Path.Combine(RepositoryRoot(), "Design", "roadmap.md");

    /// <summary>
    /// A row of an item table: its first cell is an id such as <c>B492</c>, with or without a tick.
    /// </summary>
    private const string ItemRow = @"^\| B(\d+)[^|]*\|";

    [Fact]
    public void TheItemTablesAreNotSplitByABlankLine()
    {
        // The one that bit twice in the old backlog. A blank line between rows ends the table, so
        // every row after it loses its header - and the raw file looks entirely reasonable.
        var lines = File.ReadAllLines(RoadmapPath());

        var breaks = new List<int>();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var isBlank = lines[i].Trim().Length == 0;
            var betweenRows = Regex.IsMatch(lines[i - 1], ItemRow)
                              && Regex.IsMatch(lines[i + 1], ItemRow);

            if (isBlank && betweenRows)
                breaks.Add(i + 1);
        }

        Assert.True(breaks.Count == 0,
            "blank line(s) inside an item table, which ends it at that point: "
            + string.Join(", ", breaks.Select(b => $"line {b}")));
    }

    /// <summary>
    /// Item ids are unique, and every one is below the next id the roadmap says to issue.
    /// </summary>
    /// <remarks>
    /// <para>Ids are cited from code comments, test summaries, build scripts and CI workflows, so
    /// <b>an id is permanent</b>: a closed item's row leaves the file but its number is never given
    /// to something else. B1-B498 were issued in <c>Design/backlog.md</c>, retired on 2026-09-28;
    /// the roadmap now states where new ids continue from, and this reads that sentence rather than
    /// carrying a second copy of the number, so the document and the guard cannot disagree.</para>
    ///
    /// <para>Requiring every id to sit below the stated number is what makes adding an item move
    /// the number: a row given the next id without it fails here, rather than leaving the number
    /// to be issued again once that row is closed and removed.</para>
    /// </remarks>
    [Fact]
    public void ItemIdsAreUniqueAndBelowTheNextToIssue()
    {
        var text = File.ReadAllText(RoadmapPath());

        var next = int.Parse(
            Regex.Match(text, @"continues from \*\*B(\d+)\*\*").Groups[1].Value is { Length: > 0 } n
                ? n
                : throw new InvalidOperationException(
                    "Design/roadmap.md no longer states where new item ids continue from. The line reads "
                    + "'...continues from **B<n>**.' and this test reads the number out of it."));

        Assert.True(next > 498, $"the roadmap says new ids continue from B{next}, but B1-B498 were issued in the retired backlog");

        var ids = Regex.Matches(text, "(?m)" + ItemRow).Select(m => int.Parse(m.Groups[1].Value)).ToList();

        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => $"B{g.Key}").ToList();
        Assert.True(duplicates.Count == 0, "duplicate item ids: " + string.Join(", ", duplicates));

        var unissued = ids.Where(i => i >= next).Select(i => $"B{i}").ToList();
        Assert.True(unissued.Count == 0,
            $"item id(s) at or above B{next}, the next the roadmap says to issue: " + string.Join(", ", unissued)
            + " - move the 'continues from' number past every id in use, or it will be issued again");
    }
}

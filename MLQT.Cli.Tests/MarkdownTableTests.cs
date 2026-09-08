using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Cli.Tests;

/// <summary>
/// That the tables in the design notes and CLAUDE.md are still tables.
/// </summary>
/// <remarks>
/// <para>The backlog in <c>Design/roadmap.md</c> is 116 rows long and is appended to by script far
/// more often than by hand. Two ways of breaking it silently have already happened, and neither
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

    [Fact]
    public void TheBacklogTableIsNotSplitByABlankLine()
    {
        // The one that bit twice. A blank line between rows ends the table, so every row after it
        // loses its header - and the raw file looks entirely reasonable.
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "Design", "roadmap.md"));

        var breaks = new List<int>();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var isBlank = lines[i].Trim().Length == 0;
            var betweenRows = Regex.IsMatch(lines[i - 1], @"^\| B\d+ \|")
                              && Regex.IsMatch(lines[i + 1], @"^\| B\d+ \|");

            if (isBlank && betweenRows)
                breaks.Add(i + 1);
        }

        Assert.True(breaks.Count == 0,
            "blank line(s) inside the backlog table, which ends it at that point: "
            + string.Join(", ", breaks.Select(b => $"line {b}")));
    }

    [Fact]
    public void TheBacklogIdsAreUniqueAndUnbroken()
    {
        // A duplicate id means two items answer to one name in every discussion that follows; a gap
        // usually means a row was lost by a scripted edit rather than deliberately retired.
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Design", "roadmap.md"));
        var ids = Regex.Matches(text, @"(?m)^\| B(\d+) \|").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.True(ids.Count > 100, $"only found {ids.Count} backlog rows; the table format may have changed");

        var duplicates = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => $"B{g.Key}").ToList();
        Assert.True(duplicates.Count == 0, "duplicate backlog ids: " + string.Join(", ", duplicates));

        var missing = Enumerable.Range(1, ids.Max()).Except(ids).Select(i => $"B{i}").ToList();
        Assert.True(missing.Count == 0, "gaps in the backlog ids: " + string.Join(", ", missing));
    }
}

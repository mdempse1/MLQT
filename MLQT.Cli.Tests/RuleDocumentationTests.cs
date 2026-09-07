using System.Text.RegularExpressions;
using ModelicaGraph;
using ModelicaParser.StyleRules;

namespace MLQT.Cli.Tests;

/// <summary>
/// Every SARIF alert MLQT writes carries a <c>helpUri</c> pointing at
/// <c>Documentation/settings-reference.md</c>, on the reasoning that one page listing every rule id
/// beats a per-rule anchor that might not exist. That is only true while the page actually lists
/// them — and for a long time it listed eleven of thirty-one, so the alert most people saw first
/// linked to a page that never mentioned its rule.
/// </summary>
public class RuleDocumentationTests
{
    /// <summary>
    /// Not settings, so not on the settings page: a parse diagnostic cannot be configured, only
    /// fixed. They are documented in the CLI reference instead, which the page points at.
    /// </summary>
    private static readonly HashSet<string> NotSettings =
        [RuleIds.SyntaxError, RuleIds.ParseFailure, RuleIds.CheckFailed];

    /// <summary>
    /// The repository's Documentation directory, found by walking up from the test binary. Asserted
    /// rather than returned as null: a check that passes when it cannot find what it checks is a
    /// check that cannot fail, which is the defect B100 was, on the guard for every SARIF helpUri.
    /// </summary>
    private static string DocumentationDirectory()
    {
        var found = FindDocumentation();
        Assert.True(found is not null,
            "The Documentation directory was not found by walking up from " + AppContext.BaseDirectory
            + ". These checks exist to hold the page to the catalogue; passing without reading it "
            + "would be worse than not running at all.");
        return found!;
    }

    private static string? FindDocumentation()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Documentation");
            if (File.Exists(Path.Combine(candidate, "settings-reference.md")))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    [Fact]
    public void TheSettingsReferenceNamesEveryConfigurableRule()
    {
        var docs = DocumentationDirectory();

        var page = File.ReadAllText(Path.Combine(docs, "settings-reference.md"));

        var missing = RuleCatalog.BuiltIn.Keys
            .Where(id => !NotSettings.Contains(id))
            .Where(id => !page.Contains(id, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "settings-reference.md is what every SARIF alert links to, so it has to name the rule " +
            "the alert is about. Missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void TheDiagnosticsAreDocumentedWhereTheyBelong()
    {
        // They are excluded from the page above on purpose, which is only defensible while they are
        // written down somewhere else.
        var docs = DocumentationDirectory();

        var cli = File.ReadAllText(Path.Combine(docs, "cli.md"));

        foreach (var id in NotSettings)
            Assert.Contains(id, cli);
    }

    [Fact]
    public void TheDiagnosticsPageHasTheHeadingTheirAlertsLinkTo()
    {
        // The alert body links to cli.md#diagnostics. A helpUri pointing at a heading that has been
        // renamed lands the reader at the top of a long page, which is the same failure as linking
        // to a page that never mentions the rule.
        var docs = DocumentationDirectory();

        Assert.Contains("## Diagnostics", File.ReadAllText(Path.Combine(docs, "cli.md")));
    }

    // ---- the row itself, not just the id on the page ---------------------------------------------
    //
    // The three checks above ask whether a rule id appears somewhere in the file. That is a weak
    // question, and it was answered "yes" for rows that named a control the app does not have and
    // stated a default the code does not use (B101, B102). These ask about the row.

    /// <summary>Every label the settings dialog can show, from both places they are written.</summary>
    private static string UiLabels(string repositoryRoot) =>
        File.ReadAllText(Path.Combine(repositoryRoot, "ModelicaGraph", "RuleSettingsLayout.cs"))
        + File.ReadAllText(Path.Combine(repositoryRoot, "MLQT.Shared", "Components", "SettingsRepositories.razor"));

    /// <summary>The repository root — the Documentation directory's parent.</summary>
    private static string RepositoryRoot() => Directory.GetParent(DocumentationDirectory())!.FullName;

    /// <summary>A rule row that names the control: <c>| **Label** | `MLQT.X.Y` | Default |</c>.</summary>
    private static readonly Regex SettingRow =
        new(@"^\|\s*\*\*([^*|]+)\*\*\s*\|\s*`(MLQT\.[A-Za-z.]+)`[^|]*\|\s*([^|]+?)\s*\|", RegexOptions.Multiline);

    /// <summary>A Static Analysis row, which has no Setting column: <c>| `MLQT.X.Y` | Severity | …</c>.</summary>
    private static readonly Regex AnalysisRow =
        new(@"^\|\s*`(MLQT\.[A-Za-z.]+)`\s*\|\s*([A-Za-z]+)\s*\|", RegexOptions.Multiline);

    [Fact]
    public void EverySettingTheReferenceNamesIsALabelTheDialogShows()
    {
        // The Setting column is what a reader searches the Edit Repository Details dialog for. Four
        // of the fifteen named something that appears nowhere in the app -- the section-ordering
        // rules, whose dialog labels are full sentences hardcoded in the razor while the page used a
        // short name of its own. Exact match, not substring: a table claiming to name the control
        // should name it.
        var labels = UiLabels(RepositoryRoot());
        var page = File.ReadAllText(Path.Combine(DocumentationDirectory(), "settings-reference.md"));

        var wrong = SettingRow.Matches(page)
            .Select(m => (Label: m.Groups[1].Value.Trim(), Rule: m.Groups[2].Value))
            .Where(r => !labels.Contains($"\"{r.Label}\"", StringComparison.Ordinal))
            .Select(r => $"{r.Rule}: the page says \"{r.Label}\"")
            .ToList();

        Assert.True(wrong.Count == 0,
            "settings-reference.md names a setting the dialog does not show, so a reader cannot find "
            + "the control it documents. Use the label from RuleSettingsLayout or "
            + "SettingsRepositories.razor verbatim:\n  " + string.Join("\n  ", wrong));
    }

    [Fact]
    public void EveryStatedDefaultIsTheDefaultTheCodeUses()
    {
        // Two tables used the header "Default" for two different things -- enablement in one, and in
        // the other the severity a rule takes once it is switched on, two lines under a sentence
        // saying those rules are off by default. So the header decides the question asked of the
        // column: "Default" is answered by SeverityFor on fresh settings (always Off, because the
        // severity map starts empty), and "Severity when on" by the catalogue.
        var page = File.ReadAllText(Path.Combine(DocumentationDirectory(), "settings-reference.md"));
        var offByDefault = new StyleCheckingSettings();
        var wrong = new List<string>();
        var checkedRows = 0;

        foreach (var table in Tables(page))
        {
            var ruleColumn = table.Headers.FindIndex(h => h.Contains("Rule id", StringComparison.OrdinalIgnoreCase));
            if (ruleColumn < 0)
                continue;

            for (var column = 0; column < table.Headers.Count; column++)
            {
                var header = table.Headers[column];
                var asksAboutDefault = header.Equals("Default", StringComparison.OrdinalIgnoreCase);
                var asksAboutLevel = header.Equals("Severity when on", StringComparison.OrdinalIgnoreCase);
                if (!asksAboutDefault && !asksAboutLevel)
                    continue;

                foreach (var row in table.Rows)
                {
                    if (row.Count <= Math.Max(column, ruleColumn))
                        continue;
                    var rule = row[ruleColumn].Trim('`', ' ');
                    if (!RuleCatalog.BuiltIn.TryGetValue(rule, out var definition))
                        continue;

                    var stated = row[column].Trim();
                    var expected = asksAboutDefault
                        ? offByDefault.SeverityFor(rule).ToString()
                        : definition.DefaultSeverity.ToString();
                    checkedRows++;

                    if (!string.Equals(stated, expected, StringComparison.OrdinalIgnoreCase))
                        wrong.Add($"{rule}: under \"{header}\" the page says \"{stated}\", the code says {expected}");
                }
            }
        }

        Assert.True(checkedRows > 20, $"only {checkedRows} rule rows were checked; the tables moved");
        Assert.True(wrong.Count == 0,
            "A rule reference that misstates what a rule does out of the box sends someone to look for "
            + "a finding they will not get, or to expect a gate that is not armed. A column headed "
            + "\"Default\" is about whether the rule is on; call it \"Severity when on\" if it is about "
            + "the level it carries once it is:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>A markdown table: its header cells, and each body row's cells.</summary>
    private static List<(List<string> Headers, List<List<string>> Rows)> Tables(string page)
    {
        var tables = new List<(List<string>, List<List<string>>)>();
        var lines = page.Split('\n');

        for (var i = 0; i < lines.Length - 1; i++)
        {
            if (!lines[i].TrimStart().StartsWith('|') || !lines[i + 1].Contains("---"))
                continue;

            var headers = Cells(lines[i]);
            var rows = new List<List<string>>();
            for (var j = i + 2; j < lines.Length && lines[j].TrimStart().StartsWith('|'); j++)
                rows.Add(Cells(lines[j]));

            tables.Add((headers, rows));
            i += rows.Count + 1;
        }

        return tables;
    }

    private static List<string> Cells(string line) =>
        [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim().Trim('*').Trim())];
}

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
    /// The repository's Documentation directory, found by walking up from the test binary. Null when
    /// the tests run from somewhere the sources are not, which is not a failure — the check simply
    /// has nothing to read.
    /// </summary>
    private static string? DocumentationDirectory()
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
        if (docs is null)
            return;

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
        if (docs is null)
            return;

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
        if (docs is null)
            return;

        Assert.Contains("## Diagnostics", File.ReadAllText(Path.Combine(docs, "cli.md")));
    }
}

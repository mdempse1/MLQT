using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests.Theming;

/// <summary>
/// The CSS half of <c>MLQT.UI.Theme</c>'s public surface: the classes and custom properties its
/// stylesheets define are exactly the ones <c>MLQT.UI.Theme/PublicCss.txt</c> lists, and every
/// <c>--mlqt-*</c> property anything uses is one the theme defines.
/// </summary>
/// <remarks>
/// <para>Other tools reference the theme and use its class names and tokens by name. A renamed class
/// or token fails nowhere: the element is simply no longer styled, in a repository this one cannot
/// see. <c>PublicApiAnalyzers</c> holds the theme's types to <c>PublicAPI.*.txt</c>; this holds its
/// stylesheets to a list in the same way, so that every change a consumer would feel is a line in a
/// reviewed file.</para>
///
/// <para>The use check is the same failure from the inside: <c>var(--mlqt-typo)</c> is not an error
/// in CSS, it is the property's initial value, so a misspelt token makes a control lose its size or
/// colour with nothing anywhere saying why.</para>
/// </remarks>
public class ThemeCssContractTests
{
    /// <summary>
    /// Classes the theme's selectors name that it does not define: MudSplitPanel's own parts, the
    /// utility class MudStack renders as, and the class Blazor's ErrorBoundary renders. MudBlazor's
    /// <c>mud-*</c> classes are recognised by their prefix.
    /// </summary>
    private static readonly string[] ClassesOfTheFrameworks =
        ["child-panel", "divider", "horizontal", "d-flex", "blazor-error-boundary"];

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.Shared", "_Imports.razor")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static string ThemeFolder() => Path.Combine(RepositoryRoot(), "MLQT.UI.Theme");

    private static List<string> ThemeStylesheets() =>
        Directory.EnumerateFiles(Path.Combine(ThemeFolder(), "wwwroot"), "*.css", SearchOption.AllDirectories)
                 .ToList();

    private static string WithoutComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);

    /// <summary>
    /// The class names in a stylesheet's selectors. The text before each <c>{</c> back to the last
    /// brace is a rule's prelude - a selector list, or an at-rule's condition, which is skipped. A
    /// declaration block never contains a <c>{</c>, so a value such as <c>0.75rem</c> is never read
    /// as a class.
    /// </summary>
    internal static IEnumerable<string> ClassesIn(string css) =>
        Regex.Matches(WithoutComments(css), @"([^{}]+)\{")
             .Select(m => m.Groups[1].Value.Trim())
             .Where(prelude => !prelude.StartsWith('@'))
             .SelectMany(prelude => Regex.Matches(prelude, @"\.(-?[_a-zA-Z][\w-]*)").Select(c => c.Groups[1].Value));

    internal static IEnumerable<string> TokensDefinedIn(string css) =>
        Regex.Matches(WithoutComments(css), @"(--mlqt-[\w-]+)\s*:").Select(m => m.Groups[1].Value);

    internal static IEnumerable<string> TokensUsedIn(string text) =>
        Regex.Matches(text, @"var\(\s*(--mlqt-[\w-]+)").Select(m => m.Groups[1].Value);

    private static List<string> Listed() =>
        File.ReadAllLines(Path.Combine(ThemeFolder(), "PublicCss.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static void AssertSameSet(IEnumerable<string> listed, IEnumerable<string> defined, string what)
    {
        var expected = listed.ToHashSet(StringComparer.Ordinal);
        var actual = defined.ToHashSet(StringComparer.Ordinal);

        var unlisted = actual.Except(expected).Order().ToList();
        var gone = expected.Except(actual).Order().ToList();

        Assert.True(unlisted.Count == 0,
            $"The theme's stylesheets define {what} that MLQT.UI.Theme/PublicCss.txt does not list: "
            + string.Join(", ", unlisted) + ". Add each one's line if it is meant for other tools to use.");
        Assert.True(gone.Count == 0,
            $"MLQT.UI.Theme/PublicCss.txt lists {what} the stylesheets no longer define: "
            + string.Join(", ", gone) + ". A tool using one by name has just lost its styling; "
            + "keep the old name beside the new one for a release, then remove its line.");
    }

    [Fact]
    public void TheStylesheetsDefineExactlyTheListedClasses()
    {
        var defined = ThemeStylesheets()
            .SelectMany(path => ClassesIn(File.ReadAllText(path)))
            .Where(name => !name.StartsWith("mud-", StringComparison.Ordinal))
            .Where(name => !ClassesOfTheFrameworks.Contains(name))
            .Select(name => "." + name);

        AssertSameSet(Listed().Where(l => l.StartsWith('.')), defined, "classes");
    }

    [Fact]
    public void TheStylesheetsDefineExactlyTheListedTokens()
    {
        var defined = ThemeStylesheets().SelectMany(path => TokensDefinedIn(File.ReadAllText(path)));

        AssertSameSet(Listed().Where(l => l.StartsWith("--")), defined, "custom properties");
    }

    [Fact]
    public void TheThemeNeverSizesAnythingToTheViewport()
    {
        // MLQT's window never scrolls, so MLQT sizes its MudLayout to 100vh x 100vw - in MainLayout.
        // Every tool on the theme got that rule while it was in mlqt-density.css, and on a page that
        // scrolls 100vw counts the vertical scrollbar, so the layout overflowed sideways by its width
        // (measured: 800px of layout in a 796px page). Sizing the page is the tool's business.
        var offenders = ThemeStylesheets()
            .SelectMany(path => Regex.Matches(WithoutComments(File.ReadAllText(path)), @"[\d.]+(vw|vh|dvh|svh|lvh)\b")
                                     .Select(m => $"{Path.GetFileName(path)}: {m.Value}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "The theme sizes something to the viewport, which every tool's page then inherits: "
            + string.Join(", ", offenders) + ". Put viewport sizing in the application's own layout.");
    }

    [Fact]
    public void EveryFrameworkClassTheTestExemptsIsStillStyled()
    {
        // An exemption for a class the stylesheets stopped naming is a hole the next class of ours
        // with that name would fall through unlisted.
        var named = ThemeStylesheets().SelectMany(path => ClassesIn(File.ReadAllText(path))).ToHashSet();

        foreach (var exempt in ClassesOfTheFrameworks)
            Assert.True(named.Contains(exempt), $"{exempt} is exempted but no theme selector names it any more");
    }

    [Fact]
    public void EveryMlqtTokenInUseIsDefined()
    {
        // The theme's own sheets, and everything in this repository that styles a page on top of it.
        var root = RepositoryRoot();
        var users = ThemeStylesheets()
            .Concat(new[] { "MLQT.Shared", "MLQT.Photino", "MLQT.TestHost", "MLQT.McpTester" }
                .SelectMany(project => new[] { "*.css", "*.razor" }
                    .SelectMany(pattern => Directory.EnumerateFiles(Path.Combine(root, project), pattern, SearchOption.AllDirectories)))
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                            && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
            .ToList();

        var defined = ThemeStylesheets().SelectMany(path => TokensDefinedIn(File.ReadAllText(path))).ToHashSet();
        var uses = users
            .SelectMany(path => TokensUsedIn(File.ReadAllText(path)).Select(token => (Path.GetFileName(path), token)))
            .ToList();

        // The scan found something: without this a pattern that stopped matching would check nothing.
        Assert.True(uses.Count >= 30, $"found only {uses.Count} uses of --mlqt-* tokens; the scan is not reading the files");

        var undefined = uses.Where(u => !defined.Contains(u.token)).Select(u => $"{u.Item1}: {u.token}").Distinct().ToList();
        Assert.True(undefined.Count == 0,
            "These use a --mlqt-* token the theme does not define, so the property silently takes its "
            + "initial value: " + string.Join(", ", undefined));
    }

    [Theory]
    [InlineData(".a { color: red; } .b .c:hover { width: 0.75rem; }", "a,b,c")]
    [InlineData("@media (prefers-color-scheme: dark) { .x { top: 1.5px; } }", "x")]
    [InlineData("/* .commented { } */ .kept::before { inset: 0 }", "kept")]
    [InlineData(".p > .q, .r.s { }", "p,q,r,s")]
    public void TheClassReaderReadsSelectorsOnly(string css, string expected)
    {
        // Pinned because both contract tests pass vacuously if it reads nothing, and fail on every
        // stylesheet if it starts reading the decimal point in a value as a class.
        Assert.Equal(expected.Split(','), ClassesIn(css).ToArray());
    }

    [Fact]
    public void TheTokenReadersTellADefinitionFromAUse()
    {
        const string css = ":root { --mlqt-a: 1px; --mud-b: 2px; } .x { width: var(--mlqt-a); height: var( --mlqt-c ); }";

        Assert.Equal(["--mlqt-a"], TokensDefinedIn(css).ToArray());
        Assert.Equal(["--mlqt-a", "--mlqt-c"], TokensUsedIn(css).ToArray());
    }
}

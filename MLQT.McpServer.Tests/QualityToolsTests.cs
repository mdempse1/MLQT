using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class QualityToolsTests
{
    private static StyleTools Style(TestHost h) => new(h.Libraries, h.StyleChecking, h.CodeReview, h.Settings);
    private static SpellingTools Spelling(TestHost h) => new(h.Libraries, h.StyleChecking);
    private static FormattingTools Formatting(TestHost h) => new(h.Libraries);

    private static void LoadSingle(TestHost h, string file, string content)
        => h.Libraries.AddLibraryFromFileAsync(h.WriteMoFile(file, content)).GetAwaiter().GetResult();

    // ----- style -----

    [Fact]
    public async Task GetStyleSettings_DefaultsOff()
    {
        using var host = new TestHost();
        var settings = await Style(host).GetStyleSettings();
        Assert.False(settings.ClassHasDescription);
        Assert.False(settings.SpellCheckDescription);
    }

    [Fact]
    public void CheckStyle_Stateless_FindsMissingDescription()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<CheckResult>(Style(host).CheckStyle(
            "model B\n Real p;\nequation\n p=1;\nend B;",
            new StyleSettingsInput { ClassHasDescription = true }));
        Assert.Equal(1, res.ViolationCount);
    }

    [Fact]
    public void CheckStyle_EmptySource_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Style(host).CheckStyle("  ", null));
    }

    [Fact]
    public void CheckClass_StoresViolations_VisibleInListIssues()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B\n Real p;\nequation\n p=1;\nend B;");
        var style = Style(host);

        var res = ToolAssert.Ok<CheckResult>(style.CheckClass("B", new StyleSettingsInput { ClassHasDescription = true }));
        Assert.Equal(1, res.ViolationCount);

        var issues = ToolAssert.Ok<IssuesResult>(style.ListIssues());
        Assert.Contains(issues.Items, i => i.ModelId == "B" && i.Category == "style");
    }

    [Fact]
    public void CheckClass_WithReferenceAndIconRules_RunsAllContext()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B \"d\"\n Real p \"pp\";\nequation\n p=1;\nend B;");
        // Enabling ValidateModelReferences + ClassHasIcon exercises the graph-context branches
        // (known model ids and the base-class icon callback) in the check runner.
        var res = ToolAssert.Ok<CheckResult>(Style(host).CheckClass("B",
            new StyleSettingsInput { ValidateModelReferences = true, ClassHasIcon = true }));
        Assert.NotNull(res.Violations);
    }

    [Fact]
    public void CheckClass_Missing_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Style(host).CheckClass("Nope"));
    }

    [Fact]
    public void CheckLibrary_ChecksAll()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B\n Real p;\nequation\n p=1;\nend B;");
        var res = ToolAssert.Ok<CheckResult>(Style(host).CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true }));
        Assert.True(res.ModelsChecked >= 1);
        Assert.True(res.ViolationCount >= 1);
    }

    [Fact]
    public void CheckLibrary_NothingLoaded_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Style(host).CheckLibrary());
    }

    [Fact]
    public void ListIssues_IncludesParseErrors()
    {
        using var host = new TestHost();
        LoadSingle(host, "Bad.mo", "model Bad \"broken\"\n  Real x;\nequation\n  x = ;\nend Bad;");
        var issues = ToolAssert.Ok<IssuesResult>(Style(host).ListIssues(includeParseErrors: true));
        Assert.Contains(issues.Items, i => i.Category == "parse");

        var noParse = ToolAssert.Ok<IssuesResult>(Style(host).ListIssues(includeParseErrors: false));
        Assert.DoesNotContain(noParse.Items, i => i.Category == "parse");
    }

    // ----- spelling -----

    [Fact]
    public void SpellingSuggestions_MisspelledAndCorrect()
    {
        using var host = new TestHost();
        var bad = ToolAssert.Ok<SpellSuggestionsResult>(Spelling(host).SpellingSuggestions("postion"));
        Assert.False(bad.IsCorrect);
        Assert.Contains("position", bad.Suggestions);

        var good = ToolAssert.Ok<SpellSuggestionsResult>(Spelling(host).SpellingSuggestions("model"));
        Assert.True(good.IsCorrect);
        Assert.Empty(good.Suggestions);

        Assert.IsType<ToolError>(Spelling(host).SpellingSuggestions(" "));
    }

    [Fact]
    public void SpellCheck_SourceAndClass()
    {
        using var host = new TestHost();
        var fromSource = Spelling(host).SpellCheck(source: "model P\n Real q \"The postion of q\";\nequation\n q=1;\nend P;");
        var list = Assert.IsAssignableFrom<IReadOnlyList<StyleViolationDto>>(fromSource);
        Assert.Contains(list, v => v.Summary.Contains("postion"));

        Assert.IsType<ToolError>(Spelling(host).SpellCheck());
    }

    [Fact]
    public void CorrectSpelling_WritesAndReloads()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", "model Foo\n  Real x \"The postion\";\nequation\n x=1;\nend Foo;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var spelling = Spelling(host);

        // preview: no write
        var preview = ToolAssert.Ok<CorrectSpellingResult>(
            spelling.CorrectSpelling("Foo", "postion", "position", preview: true).GetAwaiter().GetResult());
        Assert.True(preview.Replacements >= 1);
        Assert.True(preview.PreviewOnly);
        Assert.DoesNotContain("position", File.ReadAllText(path)); // unchanged on disk

        // write
        var written = ToolAssert.Ok<CorrectSpellingResult>(
            spelling.CorrectSpelling("Foo", "postion", "position").GetAwaiter().GetResult());
        Assert.True(written.Changed);
        Assert.Contains("position", File.ReadAllText(path));
    }

    [Fact]
    public void CorrectSpelling_NoMatch_ReturnsZero()
    {
        using var host = new TestHost();
        LoadSingle(host, "Foo.mo", "model Foo\n  Real x \"clean\";\nequation\n x=1;\nend Foo;");
        var res = ToolAssert.Ok<CorrectSpellingResult>(
            Spelling(host).CorrectSpelling("Foo", "zzz", "yyy").GetAwaiter().GetResult());
        Assert.Equal(0, res.Replacements);
        Assert.False(res.Changed);
    }

    [Fact]
    public void CorrectSpelling_Validation()
    {
        using var host = new TestHost();
        LoadSingle(host, "Foo.mo", "model Foo\n Real x;\nequation\n x=1;\nend Foo;");
        Assert.IsType<ToolError>(Spelling(host).CorrectSpelling("Foo", " ", "y").GetAwaiter().GetResult());
        Assert.IsType<ToolError>(Spelling(host).CorrectSpelling("Nope", "a", "b").GetAwaiter().GetResult());
    }

    // ----- formatting -----

    [Fact]
    public void FormatCode_FormatsAndErrorsOnEmpty()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<FormatCodeResult>(Formatting(host).FormatCode("model M \"d\"\n  Real   y=2   \"yy\";\nequation\n y=1;\nend M;"));
        Assert.Contains("model M", res.Source);
        Assert.IsType<ToolError>(Formatting(host).FormatCode(" "));
    }

    [Fact]
    public void FormatClass_PreviewDoesNotWrite_ThenWrites()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Foo.mo", "model Foo \"d\"\n      Real x=1   \"xx\";\nequation\n x=2*time;\nend Foo;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var fmt = Formatting(host);
        var before = File.ReadAllText(path);

        var preview = ToolAssert.Ok<FormatClassResult>(fmt.FormatClass("Foo", preview: true).GetAwaiter().GetResult());
        Assert.True(preview.PreviewOnly);
        Assert.Equal(before, File.ReadAllText(path));

        var written = ToolAssert.Ok<FormatClassResult>(fmt.FormatClass("Foo").GetAwaiter().GetResult());
        Assert.False(written.PreviewOnly);
        Assert.NotNull(written.FilePath);
    }

    [Fact]
    public void FormatClass_Missing_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Formatting(host).FormatClass("Nope").GetAwaiter().GetResult());
    }
}

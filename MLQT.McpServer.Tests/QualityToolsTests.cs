using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class QualityToolsTests
{
    private static StyleTools Style(TestHost h)
        => new(h.Libraries, h.CodeReview, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Session);
    private static SpellingTools Spelling(TestHost h)
        => new(h.Libraries, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Resources, h.Session);
    private static FormattingTools Formatting(TestHost h) => new(h.Libraries, h.Resources, h.Session);

    private static void LoadSingle(TestHost h, string file, string content)
        => h.Libraries.AddLibraryFromFileAsync(h.WriteMoFile(file, content)).GetAwaiter().GetResult();

    [Fact]
    public void CheckLibrary_SurfacesGraphFindings_PackageOrder()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage P \"p\"\n  constant Real c = 1;\nend P;",
            ["A.mo"] = "within P;\nmodel A \"a\"\nend A;",
            ["package.order"] = "A\nGhost\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var result = Style(host).CheckLibrary(settings: new StyleSettingsInput { CheckPackageOrder = true }).GetAwaiter().GetResult();

        var cr = Assert.IsType<CheckResult>(result);
        Assert.Contains(cr.Violations, v => v.Summary.Contains("Ghost"));   // stale package.order entry
    }

    // ----- style -----

    [Fact]
    public void GetStyleSettings_DefaultsOff_WhenNoRepository()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StyleSettingsResult>(Style(host).GetStyleSettings());
        Assert.False(res.Settings.ClassHasDescription);
        Assert.False(res.Settings.SpellCheckDescription);
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
        var res = ToolAssert.Ok<CheckResult>(Style(host).CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true }).GetAwaiter().GetResult());
        Assert.True(res.ModelsChecked >= 1);
        Assert.True(res.ViolationCount >= 1);
    }

    // ----- parse diagnostics -----
    // A file that does not parse is the one problem no style rule can report — every rule reads a
    // parse tree that is missing the code in question. The MCP surface must say so, or an agent
    // reads "no violations" as "this class is fine".

    // A Documentation(info=...) annotation missing its closing quote. The parser recovers, so the
    // class still loads and nothing else flags it.
    private const string UnterminatedString = """
        model B "b"
          annotation(Documentation(info="<html><p>docs</p>));
        end B;
        """;

    [Fact]
    public void CheckClass_ReportsSyntaxError_AlongsideStyleViolations()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", UnterminatedString);

        var res = ToolAssert.Ok<CheckResult>(
            Style(host).CheckClass("B", new StyleSettingsInput { ClassHasDescription = true }));

        Assert.Contains(res.Violations, v => v.Summary == "Parser error");
    }

    [Fact]
    public void CheckClass_ReportsSyntaxError_EvenWithNoRulesEnabled()
    {
        // "No rules enabled" means no style opinions; it cannot mean silence about unreadable code.
        using var host = new TestHost();
        LoadSingle(host, "B.mo", UnterminatedString);

        var res = ToolAssert.Ok<CheckResult>(Style(host).CheckClass("B", new StyleSettingsInput()));

        Assert.Contains(res.Violations, v => v.Summary == "Parser error");
    }

    [Fact]
    public void CheckLibrary_ReportsSyntaxError()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", UnterminatedString);

        var res = ToolAssert.Ok<CheckResult>(
            Style(host).CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true })
                .GetAwaiter().GetResult());

        // One unterminated string produces both a lexer and a parser diagnostic.
        var parseErrors = res.Violations.Where(v => v.Summary == "Parser error").ToList();
        Assert.NotEmpty(parseErrors);
        // Error, not the "Style warning" every other finding projects to, and tagged as the parser's
        // so a style re-run cannot clear it.
        Assert.All(parseErrors, v => Assert.Equal("Error", v.Severity));
        Assert.All(parseErrors, v => Assert.Equal("Parser", v.Source));
        Assert.Contains(parseErrors, v => v.Details.Contains("Unterminated string literal"));
    }

    [Fact]
    public void CheckLibrary_CleanLibrary_ReportsNoParseDiagnostics()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B \"b\"\n  Real p \"p\";\nend B;");

        var res = ToolAssert.Ok<CheckResult>(
            Style(host).CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true })
                .GetAwaiter().GetResult());

        Assert.DoesNotContain(res.Violations, v => v.Summary is "Parser error" or "Fatal parse failure");
    }

    [Fact]
    public void CheckLibrary_NothingLoaded_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Style(host).CheckLibrary().GetAwaiter().GetResult());
    }

    [Fact]
    public void CheckLibrary_AutoRunsDependencyAnalysis_WhenRuleRequiresIt()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B\n Real p;\nequation\n p=1;\nend B;");
        Assert.False(host.Session.DependenciesAnalyzed);

        // The unused-class rule needs cross-model edges. check_library must run dependency analysis
        // itself (as the GUI and CLI do) so its count includes those findings without an extra step.
        Style(host).CheckLibrary(settings: new StyleSettingsInput { CheckUnusedClass = true }).GetAwaiter().GetResult();

        Assert.True(host.Session.DependenciesAnalyzed);
    }

    [Fact]
    public void CheckLibrary_SkipsDependencyAnalysis_WhenNoRuleRequiresIt()
    {
        using var host = new TestHost();
        LoadSingle(host, "B.mo", "model B\n Real p;\nequation\n p=1;\nend B;");

        // A plain style rule needs no dependency edges — the auto-run must stay off to keep it cheap.
        Style(host).CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true }).GetAwaiter().GetResult();

        Assert.False(host.Session.DependenciesAnalyzed);
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
        var result = Assert.IsType<SpellCheckResult>(fromSource);
        Assert.Contains(result.Violations, v => v.Summary.Contains("postion"));
        Assert.Null(result.Note);   // the bundled dictionaries are always present

        Assert.IsType<ToolError>(Spelling(host).SpellCheck());
    }

    [Fact]
    public void SpellCheck_ByClassId_FindsMisspelling()
    {
        using var host = new TestHost();
        LoadSingle(host, "P.mo", "model P\n  Real q \"The postion\";\nequation\n q=1;\nend P;");
        var res = Spelling(host).SpellCheck(classId: "P");
        var result = Assert.IsType<SpellCheckResult>(res);
        Assert.Contains(result.Violations, v => v.Summary.Contains("postion"));

        Assert.IsType<ToolError>(Spelling(host).SpellCheck(classId: "Nope"));
    }

    [Fact]
    public async Task SpellCheck_SaysSoWhenAConfiguredDictionaryIsMissing()
    {
        // The languages are committed with the repository; the dictionaries are installed per machine.
        // An agent on a box without one gets results that are not the ones the settings describe, and
        // the CLI has always warned about exactly this on stderr.
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["P.mo"] = "model P\n  Real q \"The postion\";\nequation\n q=1;\nend P;",
        });
        var added = await host.Repositories.AddRepositoryAsync(dir, startMonitoring: false);
        await host.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new ModelicaGraph.StyleCheckingSettings
        {
            SpellCheckLanguages = ["en_GB", "de_DE"],
        };

        var result = Assert.IsType<SpellCheckResult>(Spelling(host).SpellCheck(classId: "P"));

        Assert.NotNull(result.Note);
        Assert.Contains("de_DE", result.Note);
    }

    [Fact]
    public void SpellingSuggestions_UnknownRepository_Errors()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(Spelling(host).SpellingSuggestions("postion", repositoryId: "nope"));
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

    /// <summary>
    /// A package that stores its classes inline has them trimmed out of its stored source once
    /// checking has run — each class has its own node. Rewriting the file from that stored source
    /// would write the file back without them, so the file on disk has to be the source of truth.
    /// </summary>
    private static string TrimmedInlinePackage(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = @"within;
package P ""The postion package""
  model A ""a""
  end A;
  model B ""b""
  end B;
end P;
",
            ["package.order"] = @"A
B
",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        ModelicaGraph.PackageCodeTrimmer.TrimStandaloneChildren(host.Libraries.CombinedGraph);
        var stored = host.Libraries.GetModelById("P")!.Definition.ModelicaCode;
        Assert.DoesNotContain("model A", stored);   // the situation this guards against is real

        return dir;
    }

    [Fact]
    public void CorrectSpelling_KeepsClassesTrimmedFromTheStoredSource()
    {
        using var host = new TestHost();
        var dir = TrimmedInlinePackage(host);

        var res = ToolAssert.Ok<CorrectSpellingResult>(
            Spelling(host).CorrectSpelling("P", "postion", "position").GetAwaiter().GetResult());

        var onDisk = File.ReadAllText(Path.Combine(dir, "package.mo"));
        Assert.Equal(1, res.Replacements);
        Assert.Contains("position", onDisk);
        Assert.Contains("model A", onDisk);
        Assert.Contains("model B", onDisk);
    }

    [Fact]
    public void FormatClass_KeepsClassesTrimmedFromTheStoredSource()
    {
        using var host = new TestHost();
        var dir = TrimmedInlinePackage(host);

        ToolAssert.Ok<FormatClassResult>(Formatting(host).FormatClass("P").GetAwaiter().GetResult());

        var onDisk = File.ReadAllText(Path.Combine(dir, "package.mo"));
        Assert.Contains("model A", onDisk);
        Assert.Contains("model B", onDisk);
    }

    [Fact]
    public void CorrectSpelling_ChangesTheWordAndNothingElse()
    {
        // The tool used to rebuild the file through the formatter, so an agent's spelling fix and a
        // user's produced different diffs for the same correction. The word is the only change now,
        // including the file's own line endings.
        using var host = new TestHost();
        var original = "model Foo \"The postion\"\r\n  Real x;\r\nequation\r\n  x=1;\r\nend Foo;\r\n";
        var path = host.WriteMoFile("Foo.mo", original);
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();

        ToolAssert.Ok<CorrectSpellingResult>(
            Spelling(host).CorrectSpelling("Foo", "postion", "position").GetAwaiter().GetResult());

        Assert.Equal(original.Replace("postion", "position"), File.ReadAllText(path));
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
    public void FormatCode_Fragment_ReturnsGuidanceError()
    {
        using var host = new TestHost();
        // A bare equation / declaration is not a complete class and cannot be formatted.
        var eqErr = ToolAssert.Error(Formatting(host).FormatCode("x = 2*y + 1;"));
        Assert.Contains("class definition", eqErr.Error, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<ToolError>(Formatting(host).FormatCode("Real x = 1 \"desc\";"));
    }

    [Fact]
    public void FormatCode_SyntaxError_IsReported()
    {
        using var host = new TestHost();
        // 'type = Real;' is missing the type name — previously returned 'type ;' with no hint.
        var err = ToolAssert.Error(Formatting(host).FormatCode("type = Real;"));
        Assert.Contains("syntax error", err.Error, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void FormatClass_SyntaxErrorInFile_ReportsAndDoesNotWrite()
    {
        using var host = new TestHost();
        var path = host.WriteMoFile("Bad.mo", "model Bad \"d\"\n  Real x;\nequation\n  x = ;\nend Bad;");
        host.Libraries.AddLibraryFromFileAsync(path).GetAwaiter().GetResult();
        var before = File.ReadAllText(path);

        var err = ToolAssert.Error(Formatting(host).FormatClass("Bad").GetAwaiter().GetResult());
        Assert.Contains("syntax", err.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(path)); // file left untouched
    }
}

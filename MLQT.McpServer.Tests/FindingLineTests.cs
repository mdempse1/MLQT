using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using MLQT.Services.Checking;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The two line numbers <c>list_findings</c> returns. An agent given a file path and a line will
/// edit that line, so the pair has to be counted from the same place — which for a class nested down
/// a <c>package.mo</c> is not where the class's own source starts.
/// </summary>
public class FindingLineTests
{
    private static StyleTools Style(TestHost h)
        => new(h.Libraries, h.CodeReview, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Session);

    /// <summary>
    /// A package whose second class starts well down the file, so the class-relative line and the
    /// file line cannot be confused with each other.
    /// </summary>
    private const string PackageWithANestedClass = """
        within;
        package P "p"
          model First "described"
            Real a;
            Real b;
            Real c;
          end First;

          model Second
          end Second;
        end P;
        """;

    private const string PackageThatDoesNotParse =
        "within;\npackage P \"p\"\n  model Broken\n    Real x = ;\n  end Broken;\nend P;";

    private static TestHost Library(string packageMo, string order)
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = packageMo,
            ["package.order"] = order,
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static TestHost BrokenLibrary() => Library(PackageThatDoesNotParse, "Broken\n");

    [Fact]
    public void TheLineGoesWithTheFilePath_AndTheModelLineWithTheClass()
    {
        using var host = Library(PackageWithANestedClass, "First\nSecond\n");
        var style = Style(host);
        style.CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true })
            .GetAwaiter().GetResult();

        var findings = ToolAssert.Ok<FindingsResult>(style.ListFindings());
        var second = Assert.Single(findings.Items, i => i.ModelId == "P.Second");

        // "  model Second" is line 9 of package.mo and line 1 of the class's own source.
        Assert.Equal(9, second.Line);
        Assert.Equal(1, second.ModelLine);
        Assert.EndsWith("package.mo", second.FilePath);
    }

    [Fact]
    public void AParseErrorIsCountedTheSameWayAsAStyleFinding()
    {
        // The two used to disagree: a style finding's line came from the class and a parse error's
        // straight from the parser, which reads whole files. One array, one field, two conventions.
        using var host = BrokenLibrary();

        var findings = ToolAssert.Ok<FindingsResult>(Style(host).ListFindings());
        var parse = findings.Items.FirstOrDefault(i => i.Category == "parse");

        Assert.NotNull(parse);
        Assert.True(parse!.Line >= parse.ModelLine,
            "the file line cannot be above the class's own first line");
        Assert.NotNull(parse.FilePath);
    }

    [Fact]
    public void ACheckDoesNotAddASecondCopyOfEveryParseError()
    {
        // A check records parse errors on the review list as well as leaving them on the graph, and
        // list_findings used to report both — so calling check_library added a second copy of every
        // parse error, differing from the first only in which line it named. The graph is the answer.
        using var host = BrokenLibrary();
        var style = Style(host);

        style.CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true })
            .GetAwaiter().GetResult();

        var reported = ToolAssert.Ok<FindingsResult>(style.ListFindings())
            .Items.Count(i => i.Category == "parse");
        var onTheGraph = ParserErrorReporter.ToFindings(host.Libraries.GetAllModels()).Count;

        Assert.True(onTheGraph > 0, "the fixture must actually fail to parse for this to mean anything");
        Assert.Equal(onTheGraph, reported);
    }

    [Fact]
    public void ExcludingParseErrorsExcludesThemAfterACheckToo()
    {
        // They used to come back anyway, through the review list a check had written them to.
        using var host = BrokenLibrary();
        var style = Style(host);
        style.CheckLibrary(settings: new StyleSettingsInput { ClassHasDescription = true })
            .GetAwaiter().GetResult();

        var findings = ToolAssert.Ok<FindingsResult>(style.ListFindings(includeParseErrors: false));

        Assert.DoesNotContain(findings.Items, i => i.Category == "parse");
        Assert.DoesNotContain(findings.Items, i => i.Source == "Parser");
    }

    [Fact]
    public void AClassWithNoFileStillReportsItsOwnLine()
    {
        // A standalone file: the class starts at line 1, so the two lines coincide. Worth pinning —
        // it is the case where a mapping bug would be invisible.
        using var host = new TestHost();
        var style = Style(host);
        host.Libraries.AddLibraryFromFileAsync(
            host.WriteMoFile("B.mo", "model B\n Real p;\nequation\n p=1;\nend B;")).GetAwaiter().GetResult();
        style.CheckClass("B", new StyleSettingsInput { ClassHasDescription = true });

        var item = Assert.Single(
            ToolAssert.Ok<FindingsResult>(style.ListFindings()).Items, i => i.ModelId == "B");

        Assert.Equal(item.ModelLine, item.Line);
    }
}

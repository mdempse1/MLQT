using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_find</c>: code search, which is new, and the hand-off of the other kinds of search to the tools
/// that already do them.
/// </summary>
public class FindToolsTests
{
    // Lines are numbered in the comments: the file lines the code search must report.
    private static readonly string PackageMo = Lf("""
        within;
        package L "lib"
          model Outer "outer"
            parameter Real T = 1 "time constant";
            Inner inner(T = 2);
            model Inner "nested inline"
              parameter Real T = 0.5;
            end Inner;
          equation
            connect(a.y, b.u);
          end Outer;
        end L;
        """);

    private static readonly string Standalone = Lf("""
        within L;
        model Alone "in a file of its own"
          parameter Real T = 3;
          Real lag(T = 4);
        end Alone;
        """);

    private static readonly string OtherLib = Lf("""
        within;
        package Other "another library"
          model Uses "uses T"
            parameter Real T = 9;
          end Uses;
        end Other;
        """);

    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static FindTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = PackageMo,
            ["Alone.mo"] = Standalone,
            ["package.order"] = "Outer\nAlone\n",
        });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var other = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = OtherLib });
        h.Libraries.AddLibraryFromDirectoryAsync(other).GetAwaiter().GetResult();
        return new FindTools(h.Libraries);
    }

    private static CodeSearchResult Code(FindTools tools, string query, bool regex = false, bool ignoreCase = false,
        string? library = null, int limit = 50, int offset = 0) =>
        ToolAssert.Ok<CodeSearchResult>(tools.Find(query, "code", library, regex: regex, ignoreCase: ignoreCase,
            limit: limit, offset: offset));

    [Fact]
    public void Code_ReportsEachLineOnce_ForTheInnermostClass()
    {
        using var host = new TestHost();
        var res = Code(Load(host), "parameter Real T");

        var byClass = res.Items.Select(i => (i.ClassId, i.Text)).ToList();
        Assert.Contains(("L.Outer", "parameter Real T = 1 \"time constant\";"), byClass);
        Assert.Contains(("L.Outer.Inner", "parameter Real T = 0.5;"), byClass);
        Assert.Contains(("L.Alone", "parameter Real T = 3;"), byClass);
        Assert.Contains(("Other.Uses", "parameter Real T = 9;"), byClass);
        // The nested class's line is in its parent's stored source too, and must not be reported twice.
        Assert.Single(res.Items, i => i.Text == "parameter Real T = 0.5;");
        Assert.Equal(4, res.Total);
    }

    [Fact]
    public void Code_GivesTheFileAndTheLineInIt()
    {
        using var host = new TestHost();
        var res = Code(Load(host), "connect(a.y");

        var hit = Assert.Single(res.Items);
        Assert.Equal("L.Outer", hit.ClassId);
        Assert.EndsWith("package.mo", hit.FilePath);
        Assert.Equal(10, hit.FileLine);
        Assert.Equal(File.ReadAllLines(hit.FilePath!)[9].Trim(), hit.Text);

        var alone = Assert.Single(Code(Load2(host), "lag(T = 4)").Items);
        Assert.EndsWith("Alone.mo", alone.FilePath);
        Assert.Equal(File.ReadAllLines(alone.FilePath!)[alone.FileLine!.Value - 1].Trim(), alone.Text);

        static FindTools Load2(TestHost h) => new(h.Libraries);
    }

    [Fact]
    public void Code_ReadsARegularExpression_AndCanIgnoreCase()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var modifiers = Code(tools, @"\w+\(T = \d", regex: true);
        Assert.Equal(["Inner inner(T = 2);", "Real lag(T = 4);"], modifiers.Items.Select(i => i.Text).Order().ToArray());

        Assert.Equal(0, Code(tools, "PARAMETER REAL").Total);
        Assert.Equal(4, Code(tools, "PARAMETER REAL", ignoreCase: true).Total);
    }

    [Fact]
    public void Code_CanBeLimitedToOneLibrary()
    {
        using var host = new TestHost();
        var res = Code(Load(host), "parameter Real T", library: "Other");
        Assert.Equal("Other.Uses", Assert.Single(res.Items).ClassId);
    }

    [Fact]
    public void Code_PagesItsResults()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var first = Code(tools, "parameter Real T", limit: 3);
        Assert.Equal(3, first.Returned);
        Assert.True(first.Truncated);
        var rest = Code(tools, "parameter Real T", limit: 3, offset: 3);
        Assert.Equal(1, rest.Returned);
        Assert.False(rest.Truncated);
        Assert.Empty(first.Items.Select(i => i.Text).Intersect(rest.Items.Select(i => i.Text)));
    }

    [Fact]
    public void Code_RefusesAnEmptyQuery_AnUnknownLibrary_AndABadRegex()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.Contains("query", Assert.IsType<ToolError>(tools.Find("", "code")).Error);
        Assert.IsType<ToolError>(tools.Find("x", "code", library: "NoSuchLibrary"));
        Assert.Contains("regular expression", Assert.IsType<ToolError>(tools.Find("(", "code", regex: true)).Error);
    }

    [Fact]
    public void Code_WithNothingLoaded_SaysToLoadALibrary()
    {
        using var host = new TestHost();
        Assert.IsType<ToolError>(new FindTools(host.Libraries).Find("x", "code"));
    }

    [Fact]
    public void TheOtherKindsOfSearch_AreHandedToTheToolsThatDoThem()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.IsType<ClassSearchResult>(tools.Find("Alone"));
        Assert.IsType<ClassListResult>(tools.Find(library: "L", classType: "model"));
        Assert.IsType<TextSearchResult>(tools.Find("nested inline", "text"));
        Assert.IsType<InterfaceSearchResult>(tools.Find("L.", "interface", classType: "model"));
    }

    [Fact]
    public void AnUnknownKindOfMatch_IsRefused()
    {
        using var host = new TestHost();
        Assert.Contains("'name', 'text', 'interface' or 'code'",
            Assert.IsType<ToolError>(Load(host).Find("x", "fuzzy")).Error);
    }
}

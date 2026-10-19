using System.Text.Json;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_check</c> and <c>mlqt_dependencies</c>, the consolidated set's facades over the check and
/// dependency tools (B503 step 2): each check and mode answers what the tool it replaces answers, and
/// the analysis they need is run for them.
/// </summary>
public class AnalysisFacadeToolsTests
{
    private static readonly string PackageMo = """
        within;
        package P "p"
          model A "a gain blok"
            parameter Real k = 1 "gain";
          end A;
          model B "uses a"
            A a1(k = 2);
            NoSuch.Thing broken;
          end B;
        end P;
        """.Replace("\r\n", "\n");

    private static AnalysisFacadeTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageMo });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new AnalysisFacadeTools(h.Libraries, h.Repositories, h.Resources, h.CodeReview, h.CustomDictionary,
            h.DictionaryManager, h.Impact, h.Session);
    }

    private static StyleTools Style(TestHost h) =>
        new(h.Libraries, h.CodeReview, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Session);

    private static SpellingTools Spelling(TestHost h) =>
        new(h.Libraries, h.Repositories, h.CustomDictionary, h.DictionaryManager, h.Resources, h.Session);

    private static DependencyTools Dependencies(TestHost h) => new(h.Libraries, h.Impact, h.Resources, h.Session);

    private static string Json(object? o) => o is null ? "null" : JsonSerializer.Serialize(o, o.GetType(), McpServerJson.Options);

    // ---- mlqt_check -----------------------------------------------------------------------------

    [Fact]
    public async Task ByDefault_ItIsTheStyleCheck_OfTheTargetGiven()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var forClass = Assert.IsType<CheckFacadeResult>(await tools.Check(classId: "P.A"));
        Assert.Equal(Json(Style(host).CheckClass("P.A")), Json(forClass.Style));
        Assert.Null(forClass.Spelling);

        var forSource = Assert.IsType<CheckFacadeResult>(await tools.Check(source: "model X end X;"));
        Assert.Equal(Json(Style(host).CheckStyle("model X end X;")), Json(forSource.Style));

        var forLibrary = Assert.IsType<CheckFacadeResult>(await tools.Check());
        Assert.Equal(Json(await Style(host).CheckLibrary()), Json(forLibrary.Style));
    }

    [Fact]
    public async Task Spelling_IsTheSpellCheck_WithSuggestionsForEachWordFlagged()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<CheckFacadeResult>(await tools.Check(classId: "P.A", checks: ["spelling"]));

        Assert.Equal(Json(Spelling(host).SpellCheck("P.A")), Json(res.Spelling));
        var blok = Assert.Single(res.SpellingSuggestions!, s => s.Word == "blok");
        Assert.Contains("block", blok.Suggestions);
    }

    [Fact]
    public async Task References_AreTheReferenceCheck()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<CheckFacadeResult>(await tools.Check(classId: "P.B", checks: ["references"]));

        Assert.Equal(Json(new ViewTools(host.Libraries).ValidateClassReferences("P.B")), Json(res.References));
        Assert.Contains("NoSuch.Thing", Json(res.References));
    }

    [Fact]
    public async Task Resources_RunTheAnalysisFirst()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<CheckFacadeResult>(await tools.Check(checks: ["resources"]));

        Assert.True(host.Session.DependenciesAnalyzed);
        Assert.Equal(Json(new ResourceTools(host.Libraries, host.Resources, host.Session).GetResourceWarnings(100, 0)),
            Json(res.Resources));
    }

    [Fact]
    public async Task ACheckThatDoesNotApplyToTheTarget_SaysSoInItsPlace()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var forLibrary = Assert.IsType<CheckFacadeResult>(await tools.Check(checks: ["spelling"]));
        Assert.Contains("one class or source", Assert.IsType<ToolError>(forLibrary.Spelling).Error);

        var forSource = Assert.IsType<CheckFacadeResult>(await tools.Check(source: "model X end X;", checks: ["references"]));
        Assert.Contains("not in source text", Assert.IsType<ToolError>(forSource.References).Error);
    }

    [Fact]
    public async Task TwoTargets_OrAnUnknownCheck_AreRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.Contains("not several", Assert.IsType<ToolError>(await tools.Check(classId: "P.A", source: "model X end X;")).Error);
        Assert.Contains("Checks: style, spelling", Assert.IsType<ToolError>(await tools.Check(checks: ["units"])).Error);
    }

    // ---- mlqt_dependencies ----------------------------------------------------------------------

    [Fact]
    public async Task Uses_IsEachClassesDependencies_AfterTheAnalysisItRuns()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<DependenciesFacadeResult>(await tools.Dependencies(["P.B"]));

        Assert.True(host.Session.DependenciesAnalyzed);
        Assert.Equal("uses", res.Direction);
        Assert.Equal(Json(Dependencies(host).GetDependencies("P.B")), Json(Assert.Single(res.Classes).Answer));
        Assert.Contains("P.A", Json(res));
    }

    [Fact]
    public async Task UsedBy_IsWhatUsesEachClass_AndTransitivelyTheImpact()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var direct = Assert.IsType<DependenciesFacadeResult>(await tools.Dependencies(["P.A"], "used_by"));
        Assert.Equal(Json(Dependencies(host).FindUsages("P.A")), Json(Assert.Single(direct.Classes).Answer));

        var impact = await tools.Dependencies(["P.A"], "used_by", transitive: true);
        Assert.Equal(Json(Dependencies(host).AnalyzeImpact(["P.A"])), Json(impact));
    }

    [Fact]
    public async Task AResourcePath_IsTheClassesReferringToIt()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var path = Path.Combine(Path.GetTempPath(), "no-such-resource.txt");

        var res = await tools.Dependencies(resourcePath: path);

        Assert.Equal(Json(new ResourceTools(host.Libraries, host.Resources, host.Session).FindResourceUsages(path)), Json(res));
    }

    [Theory]
    [InlineData(false, null, null, "exactly one target")]
    [InlineData(true, "P", null, "exactly one target")]
    [InlineData(true, null, "sideways", "'uses' or 'used_by'")]
    public async Task AMissingOrDoubleTarget_OrAnUnknownDirection_IsRefused(bool withClass, string? repositoryId,
        string? direction, string expected)
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = await tools.Dependencies(withClass ? ["P.A"] : null, direction ?? "uses", repositoryId: repositoryId);

        Assert.Contains(expected, Assert.IsType<ToolError>(res).Error);
    }

    [Fact]
    public async Task TransitiveUses_IsRefused_SayingWhatTransitiveIsFor()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var err = Assert.IsType<ToolError>(await tools.Dependencies(["P.B"], "uses", transitive: true));

        Assert.Contains("transitive is for 'used_by'", err.Error);
    }
}

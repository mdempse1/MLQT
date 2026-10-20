using System.Text.Json;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>mlqt_get_class</c> and <c>mlqt_refactor_class</c>, the consolidated set's facades over the class
/// reads and the rename, move and delete tools (B503 step 2): each view and action answers what the tool
/// it replaces answers.
/// </summary>
public class ClassFacadeToolsTests
{
    private static readonly string PackageMo = """
        within;
        package P "p"
          model A "a"
            parameter Real k = 1 "gain";
            Real y;
          equation
            y = k;
          end A;
          model B "uses a"
            A a1(k = 2);
          end B;
          package Q "q"
          end Q;
        end P;
        """.Replace("\r\n", "\n");

    private static ClassFacadeTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = PackageMo });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new ClassFacadeTools(h.Libraries, h.Resources, h.Impact, h.Session);
    }

    private static string Json(object? o) => o is null ? "null" : JsonSerializer.Serialize(o, o.GetType(), McpServerJson.Options);

    [Fact]
    public async Task ByDefault_ItIsInfoAndSource_AndNoOtherView()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<GetClassResult>(await tools.GetClass(["P.A"]));

        var a = Assert.Single(res.Classes);
        Assert.Equal(Json(new ClassQueryTools(host.Libraries).GetClassInfo("P.A")), Json(a.Info));
        Assert.Equal(Json(new ClassQueryTools(host.Libraries).GetClassSource("P.A")), Json(a.Source));
        Assert.Null(a.Interface);
        Assert.DoesNotContain("\"interface\"", Json(res));   // a view not asked for is left out of the answer
    }

    public static TheoryData<string> EveryView => [.. ClassFacadeTools.Views];

    [Theory]
    [MemberData(nameof(EveryView))]
    public async Task EachView_IsWhatItsOwnToolAnswers(string view)
    {
        using var host = new TestHost();
        var tools = Load(host);
        var res = Assert.IsType<GetClassResult>(await tools.GetClass(["P.B"], [view]));
        var b = Assert.Single(res.Classes);

        // Read after the facade, which runs the analysis the resources view needs before reading it.
        var (actual, expected) = view switch
        {
            "info" => (b.Info, new ClassQueryTools(host.Libraries).GetClassInfo("P.B")),
            "source" => (b.Source, new ClassQueryTools(host.Libraries).GetClassSource("P.B")),
            "interface" => (b.Interface, new ViewTools(host.Libraries).GetClassInterface("P.B")),
            "elements" => (b.Elements, new ViewTools(host.Libraries).ListClassElements("P.B")),
            "behavior" => (b.Behavior, new ViewTools(host.Libraries).GetClassBehavior("P.B")),
            "connections" => (b.Connections, new StructureEditTools(host.Libraries, host.Resources, host.Session).ListConnections("P.B")),
            "documentation" => (b.Documentation, new ViewTools(host.Libraries).GetClassDocumentation("P.B")),
            "layout" => (b.Layout, new DiagramTools(host.Libraries, host.Resources, host.Session).GetDiagramLayout("P.B")),
            "resources" => (b.Resources, new ResourceTools(host.Libraries, host.Resources, host.Session).GetClassResources("P.B")),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
        Assert.NotNull(actual);
        Assert.Equal(Json(expected), Json(actual));
    }

    [Fact]
    public async Task SeveralClasses_AreAnsweredInOrder_AndAnUnknownOneOnce()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = Assert.IsType<GetClassResult>(await tools.GetClass(["P.B", "P.Nope", "P.A"], ["info"]));

        Assert.Equal(["P.B", "P.Nope", "P.A"], res.Classes.Select(c => c.ClassId));
        var nope = res.Classes[1];
        Assert.NotNull(nope.Error);
        Assert.Null(nope.Info);
        Assert.Null(res.Classes[0].Error);
    }

    [Fact]
    public async Task AnUnknownView_TooManyClasses_OrNone_AreRefused()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.Contains("Views: info, source", Assert.IsType<ToolError>(await tools.GetClass(["P.A"], ["diagram"])).Error);
        Assert.Contains("At most 20", Assert.IsType<ToolError>(
            await tools.GetClass([.. Enumerable.Repeat("P.A", ClassFacadeTools.MaxClasses + 1)])).Error);
        Assert.Contains("at least one", Assert.IsType<ToolError>(await tools.GetClass([])).Error);
    }

    // ---- mlqt_refactor_class --------------------------------------------------------------------

    [Fact]
    public async Task Rename_RewritesTheReferences_AsMlqtRenameClassDoes()
    {
        using var host = new TestHost();
        var tools = Load(host);

        var res = await tools.RefactorClass("P.A", "rename", newName: "Amp");

        Assert.False(res is ToolError, (res as ToolError)?.Error);   // the analysis it needs is run for it
        Assert.NotNull(host.Libraries.GetModelById("P.Amp"));
        Assert.Null(host.Libraries.GetModelById("P.A"));
        Assert.Contains("Amp a1(k = 2);", host.Libraries.GetModelById("P.B")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task APreview_WritesNothing()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.IsNotType<ToolError>(await tools.RefactorClass("P.A", "rename", newName: "Amp", preview: true));

        Assert.NotNull(host.Libraries.GetModelById("P.A"));
    }

    [Fact]
    public async Task Move_PutsTheClassInTheNewPackage()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.IsNotType<ToolError>(await tools.RefactorClass("P.A", "move", newParentId: "P.Q"));

        Assert.NotNull(host.Libraries.GetModelById("P.Q.A"));
    }

    [Fact]
    public async Task Delete_RemovesTheClass()
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.IsNotType<ToolError>(await tools.RefactorClass("P.Q", "delete"));

        Assert.Null(host.Libraries.GetModelById("P.Q"));
    }

    [Theory]
    [InlineData("rename", "needs newName")]
    [InlineData("move", "needs newParentId")]
    [InlineData("copy", "'rename', 'move' or 'delete'")]
    public async Task AMissingArgument_OrAnUnknownAction_IsRefused(string action, string expected)
    {
        using var host = new TestHost();
        var tools = Load(host);

        Assert.Contains(expected, Assert.IsType<ToolError>(await tools.RefactorClass("P.A", action)).Error);
    }
}

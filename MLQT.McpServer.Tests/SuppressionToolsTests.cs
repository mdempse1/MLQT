using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class SuppressionToolsTests
{
    private const string Package = """
        within;
        package P "p"
          model M "m"
            parameter Real k = 1 "gain";
            Real x;
          end M;
        end P;
        """;

    private static SuppressionTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new SuppressionTools(h.Libraries, h.Resources, h.Session);
    }

    private static string Source(TestHost h, string id) => h.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    [Fact]
    public async Task SuppressRule_ClassLevel_WritesAnnotation()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Documentation.ClassDescription", reason: "legacy model"));

        Assert.True(res.Changed);
        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(suppress=\"MLQT.Documentation.ClassDescription\"", src);
        Assert.Contains("reason=\"legacy model\"", src);
    }

    [Fact]
    public async Task SuppressRule_ComponentLevel_ScopesToComponent()
    {
        using var host = new TestHost();
        ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "Documentation.ParameterDescription", component: "k"));

        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(suppress=\"Documentation.ParameterDescription\")", src);
        // The waiver is on the parameter line, not the model.
        var kLine = src.Split('\n').Single(l => l.Contains("parameter Real k"));
        Assert.Contains("__MLQT", kLine);
    }

    [Fact]
    public async Task SuppressRule_UnknownComponent_Errors()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).SuppressRule("P.M", "MLQT.X", component: "nope"));
        Assert.Contains("no component named 'nope'", err.Error);
    }

    [Fact]
    public async Task SuppressRule_UnknownRuleId_NotesButStillWrites()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Totally.Made.Up"));
        Assert.NotNull(res.Note);
        Assert.Contains("not a known built-in rule", res.Note!);
    }

    [Fact]
    public async Task SuppressRule_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).SuppressRule("P.M", "MLQT.Documentation.ClassDescription", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.False(res.Changed);
        Assert.NotNull(res.NewFileContent);
        Assert.Contains("__MLQT", res.NewFileContent!);
        Assert.DoesNotContain("__MLQT", Source(host, "P.M")); // original untouched
    }
}

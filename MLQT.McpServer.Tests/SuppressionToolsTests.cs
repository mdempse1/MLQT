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
        Assert.Contains("'nope'", err.Error);
        Assert.Contains("not found", err.Error);
    }

    [Fact]
    public async Task SuppressRule_ShortClassType_WritesAnnotation()
    {
        const string pkg = """
            within;
            package P "p"
              type Len = Real(unit="m");
              model M "m"
                Real x;
              end M;
            end P;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new SuppressionTools(host.Libraries, host.Resources, host.Session);

        // A `type` is a short class definition with no body — this used to fail to locate a class body.
        var res = ToolAssert.Ok<StructureEditResult>(
            await tools.SuppressRule("P.Len", "MLQT.Units.MissingUnit", preview: true));
        Assert.Contains("type Len = Real(unit=\"m\") annotation(__MLQT(suppress=\"MLQT.Units.MissingUnit\"))", res.NewFileContent);
    }

    [Fact]
    public async Task SuppressRule_NestedClass_WritesOntoThatClass()
    {
        const string pkg = """
            within;
            package P "p"
              model Inner "inner"
                Real x;
              end Inner;
            end P;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = pkg });
        await host.Libraries.AddLibraryFromDirectoryAsync(dir);
        var tools = new SuppressionTools(host.Libraries, host.Resources, host.Session);

        var res = ToolAssert.Ok<StructureEditResult>(
            await tools.SuppressRule("P.Inner", "MLQT.Doc.ClassDescription", preview: true));
        var content = res.NewFileContent!;
        var innerStart = content.IndexOf("model Inner", StringComparison.Ordinal);
        var innerEnd = content.IndexOf("end Inner", StringComparison.Ordinal);
        var mlqtAt = content.IndexOf("__MLQT", StringComparison.Ordinal);
        Assert.InRange(mlqtAt, innerStart, innerEnd); // on Inner, not the package
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

    [Fact]
    public async Task AcceptSpellingInClass_WritesAnnotation()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AcceptSpellingInClass("P.M", "wibbler", reason: "vendor term"));

        Assert.True(res.Changed);
        var src = Source(host, "P.M");
        Assert.Contains("__MLQT(spelling=\"wibbler\"", src);
        Assert.Contains("reason=\"vendor term\"", src);
    }

    [Fact]
    public async Task AcceptSpellingInClass_MergesASecondWord()
    {
        using var host = new TestHost();
        var tools = Load(host);
        ToolAssert.Ok<StructureEditResult>(await tools.AcceptSpellingInClass("P.M", "wibbler"));
        ToolAssert.Ok<StructureEditResult>(await tools.AcceptSpellingInClass("P.M", "frimbo"));

        Assert.Contains("spelling=\"wibbler,frimbo\"", Source(host, "P.M"));
    }

    [Fact]
    public async Task AcceptSpellingInClass_EmptyWord_Errors()
    {
        using var host = new TestHost();
        var err = ToolAssert.Error(await Load(host).AcceptSpellingInClass("P.M", "  "));
        Assert.Contains("word is required", err.Error);
    }

    [Fact]
    public async Task AcceptSpellingInClass_UnknownClass_Errors()
    {
        using var host = new TestHost();
        ToolAssert.Error(await Load(host).AcceptSpellingInClass("P.Nope", "wibbler"));
    }

    [Fact]
    public async Task AcceptSpellingInClass_Preview_DoesNotWrite()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<StructureEditResult>(
            await Load(host).AcceptSpellingInClass("P.M", "wibbler", preview: true));

        Assert.True(res.PreviewOnly);
        Assert.False(res.Changed);
        Assert.Contains("spelling=\"wibbler\"", res.NewFileContent!);
        Assert.DoesNotContain("__MLQT", Source(host, "P.M"));
    }
}

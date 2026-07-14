using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class DocumentationToolsTests
{
    private const string Package = """
        within;
        package P "p"
          model A "old desc"
            Real k;
            Real j "has one";
          end A;
          model B
            Real x;
            annotation (Documentation(info="<html>original</html>", revisions="<html>r1</html>"));
          end B;
          model C
            Real x;
          end C;
        end P;
        """;

    private static (DocumentationTools tools, TestHost host) Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new DocumentationTools(h.Libraries, h.Resources, h.Session), h);
    }

    private static string Source(TestHost h, string id) => h.Libraries.GetModelById(id)!.Definition.ModelicaCode!;

    [Fact]
    public async Task SetClassDescription_ReplacesExisting()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        ToolAssert.Ok<StructureEditResult>(await tools.SetClassDescription("P.A", "new desc"));
        var src = Source(host, "P.A");
        Assert.Contains("model A \"new desc\"", src);
        Assert.DoesNotContain("old desc", src);
    }

    [Fact]
    public async Task SetClassDescription_AddsWhenAbsent()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        ToolAssert.Ok<StructureEditResult>(await tools.SetClassDescription("P.C", "C is documented"));
        Assert.Contains("model C \"C is documented\"", Source(host, "P.C"));
    }

    [Fact]
    public async Task SetComponentDescription_AddsAndReplaces()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentDescription("P.A", "k", "the gain"));
        Assert.Contains("Real k \"the gain\";", Source(host, "P.A"));

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentDescription("P.A", "j", "changed"));
        var src = Source(host, "P.A");
        Assert.Contains("Real j \"changed\";", src);
        Assert.DoesNotContain("has one", src);
    }

    [Fact]
    public async Task SetClassDocumentation_AddsToClassWithNone()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        ToolAssert.Ok<StructureEditResult>(
            await tools.SetClassDocumentation("P.C", info: "<html><p>hello</p></html>"));
        var src = Source(host, "P.C");
        Assert.Contains("Documentation(info=\"<html><p>hello</p></html>\")", src);
    }

    [Fact]
    public async Task SetClassDocumentation_ReplacesInfo_KeepsRevisions()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        ToolAssert.Ok<StructureEditResult>(
            await tools.SetClassDocumentation("P.B", info: "<html>updated</html>"));
        var src = Source(host, "P.B");
        Assert.Contains("info=\"<html>updated</html>\"", src);
        Assert.Contains("revisions=\"<html>r1</html>\"", src); // untouched revisions preserved
        Assert.DoesNotContain("original", src);
    }

    [Fact]
    public async Task SetClassDocumentation_ReadableByGetTool()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        await tools.SetClassDocumentation("P.C", info: "<html><b>Rich</b> docs</html>");

        var view = new ViewTools(host.Libraries);
        var doc = ToolAssert.Ok<ClassDocumentationResult>(view.GetClassDocumentation("P.C", format: "text"));
        Assert.Contains("Rich docs", doc.Info);
    }

    [Fact]
    public async Task SetClassDocumentation_NeitherProvided_Error()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        Assert.IsType<ToolError>(await tools.SetClassDocumentation("P.C"));
    }
}

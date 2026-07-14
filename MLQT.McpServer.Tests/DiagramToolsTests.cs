using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class DiagramToolsTests
{
    private const string Package = """
        within;
        package D "d"
          model M
            Real plain;
            Real placed annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Real described "has desc" annotation (Dialog(group="x"));
          equation
            connect(plain, placed);
          end M;
        end D;
        """;

    private static (DiagramTools tools, TestHost host) Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new DiagramTools(h.Libraries, h.Resources, h.Session), h);
    }

    private static string Source(TestHost h) => h.Libraries.GetModelById("D.M")!.Definition.ModelicaCode!;

    [Fact]
    public void GetLayout_ReadsExtentAndConnections()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        var layout = ToolAssert.Ok<DiagramLayoutResult>(tools.GetDiagramLayout("D.M"));
        var placed = layout.Components.Single(c => c.Name == "placed");
        Assert.Equal(new[] { -10, -10, 10, 10 }, placed.Extent);
        Assert.Null(layout.Components.Single(c => c.Name == "plain").Extent);
        Assert.Contains(layout.Connections, c => c.PortA == "plain" && c.PortB == "placed");
    }

    [Fact]
    public async Task SetPlacement_AddsToComponentWithoutAnnotation()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("D.M", "plain", -20, -20, 20, 20));
        var src = Source(host);
        Assert.Contains("annotation (Placement(transformation(extent={{-20,-20},{20,20}})))", src);
        Assert.Contains("Real plain", src);
    }

    [Fact]
    public async Task SetPlacement_ReplacesExistingPlacement_WithRotation()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("D.M", "placed", 0, 0, 40, 40, rotation: 90));
        var src = Source(host);
        Assert.Contains("extent={{0,0},{40,40}}, rotation=90", src);
        Assert.DoesNotContain("{-10,-10}", src); // old extent gone
    }

    [Fact]
    public async Task SetPlacement_AddsPlacementToExistingAnnotation()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        // 'described' already has a Dialog annotation but no Placement.
        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("D.M", "described", -5, -5, 5, 5));
        var src = Source(host);
        Assert.Contains("Placement(transformation(extent={{-5,-5},{5,5}}))", src);
        Assert.Contains("Dialog(group=\"x\")", src); // existing annotation content kept
    }

    [Fact]
    public async Task SetPlacement_MissingComponent_Errors()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);
        Assert.IsType<ToolError>(await tools.SetComponentPlacement("D.M", "nope", 0, 0, 1, 1));
    }
}

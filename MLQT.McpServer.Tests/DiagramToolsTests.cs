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
            Real port annotation (Placement(visible=true,
              transformation(extent={{-8,-8},{8,8}}, rotation=90, origin={-100,0}),
              iconTransformation(extent={{-120,-20},{-80,20}})));
            Real iconOnly annotation (Placement(iconTransformation(extent={{80,-20},{120,20}})));
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

    /// <summary>
    /// Moving a component on the diagram replaces where it sits on the diagram and nothing else
    /// (B321). The whole Placement used to be overwritten, so moving a class's own connector deleted
    /// its iconTransformation - which moved it on the class's icon, in every diagram that uses the
    /// class - along with its visible flag.
    /// </summary>
    [Fact]
    public async Task SetPlacement_ReplacesOnlyTheTransformation()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("D.M", "port", -60, -10, -40, 10));
        var src = Source(host);

        Assert.Contains("Placement(visible=true,", src);
        Assert.Contains("transformation(extent={{-60,-10},{-40,10}})", src);
        Assert.Contains("iconTransformation(extent={{-120,-20},{-80,20}})", src);
        // The old transformation's origin and rotation went with it: the new extent is absolute.
        Assert.DoesNotContain("origin={-100,0}", src);
        Assert.DoesNotContain("rotation=90", src);
    }

    [Fact]
    public async Task SetPlacement_AddsATransformationBesideAnIconTransformation()
    {
        using var host = new TestHost();
        var (tools, _) = Load(host);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("D.M", "iconOnly", 0, 0, 20, 20));

        Assert.Contains(
            "Placement(transformation(extent={{0,0},{20,20}}), iconTransformation(extent={{80,-20},{120,20}}))",
            Source(host));
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

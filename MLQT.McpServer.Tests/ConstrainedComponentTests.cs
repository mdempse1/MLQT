using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A replaceable component with a constraint carries its description and annotation after the
/// constraining clause, as Dymola and OMEdit write it. It is on the diagram, and moving it or giving it a
/// description changes the annotation and description it has rather than adding a second one before
/// <c>constrainedby</c>.
/// </summary>
public class ConstrainedComponentTests
{
    private const string Package = """
        within;
        package V "v"
          partial model Base
          end Base;
          model Engine
            extends Base;
          end Engine;
          model Car
            replaceable Engine engine constrainedby Base "The engine" annotation (choicesAllMatching=true,
              Placement(transformation(extent={{-80,-10},{-60,10}})));
            replaceable Engine spare constrainedby Base;
          end Car;
        end V;
        """;

    private static TestHost Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Source(TestHost host) => host.Libraries.GetModelById("V.Car")!.Definition.ModelicaCode!;

    private static int Count(string text, string what) =>
        System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(what)).Count;

    [Fact]
    public void TheLayout_PlacesIt()
    {
        using var host = Load(new TestHost());

        var layout = ToolAssert.Ok<DiagramLayoutResult>(new DiagramTools(host.Libraries, host.Resources, host.Session).GetDiagramLayout("V.Car"));

        Assert.Equal(new[] { -80, -10, -60, 10 }, layout.Components.Single(c => c.Name == "engine").Extent);
    }

    [Fact]
    public async Task MovingIt_ChangesThePlacementItHas()
    {
        using var host = Load(new TestHost());
        var tools = new DiagramTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("V.Car", "engine", 0, 0, 20, 20));

        var src = Source(host);
        Assert.Contains("extent={{0,0},{20,20}}", src);
        Assert.DoesNotContain("{-80,-10}", src);
        Assert.Contains("choicesAllMatching=true", src);
        Assert.Equal(1, Count(src, "annotation"));   // engine's own, and no second one before constrainedby
    }

    [Fact]
    public async Task PlacingOneWithNoAnnotation_PutsItAfterTheConstraint()
    {
        using var host = Load(new TestHost());
        var tools = new DiagramTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<StructureEditResult>(await tools.SetComponentPlacement("V.Car", "spare", 0, 0, 20, 20));

        Assert.Contains("replaceable Engine spare constrainedby Base annotation (Placement(", Source(host));
        Assert.Equal(new[] { 0, 0, 20, 20 },
            ToolAssert.Ok<DiagramLayoutResult>(tools.GetDiagramLayout("V.Car")).Components.Single(c => c.Name == "spare").Extent);
    }

    [Fact]
    public async Task OneOfTwoSharingAnAnnotation_IsRefused_RatherThanMovingBoth()
    {
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage W \"w\"\n  model Car\n    replaceable Real e1, e2 constrainedby Real " +
                             "annotation (Placement(transformation(extent={{0,0},{10,10}})));\n  end Car;\nend W;\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var before = host.Libraries.GetModelById("W.Car")!.Definition.ModelicaCode;

        var placed = ToolAssert.Error(await new DiagramTools(host.Libraries, host.Resources, host.Session)
            .SetComponentPlacement("W.Car", "e1", 20, 20, 40, 40));
        var described = ToolAssert.Error(await new DocumentationTools(host.Libraries, host.Resources, host.Session)
            .SetComponentDescription("W.Car", "e1", "First"));

        Assert.Contains("'e2'", placed.Error);
        Assert.Contains("'e2'", described.Error);
        Assert.Equal(before, host.Libraries.GetModelById("W.Car")!.Definition.ModelicaCode);
    }

    [Fact]
    public async Task DescribingIt_ChangesTheDescriptionItHas()
    {
        using var host = Load(new TestHost());
        var docs = new DocumentationTools(host.Libraries, host.Resources, host.Session);

        Assert.IsNotType<ToolError>(await docs.SetComponentDescription("V.Car", "engine", "The power unit"));
        Assert.IsNotType<ToolError>(await docs.SetComponentDescription("V.Car", "spare", "A spare"));

        var src = Source(host);
        Assert.Contains("constrainedby Base \"The power unit\" annotation", src);
        Assert.DoesNotContain("The engine", src);
        Assert.Contains("replaceable Engine spare constrainedby Base \"A spare\";", src);
    }
}

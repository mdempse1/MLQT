using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A connection line ends where the connector it connects is <b>drawn</b> (B314).
///
/// <para>The image draws a component's connectors from its type's <i>icon</i> layer - the
/// <c>iconTransformation</c> where there is one - in the icon's coordinate system, turned about the
/// placement's <c>origin</c>. The router used to read the <i>diagram</i> layer's placement, the first
/// <c>coordinateSystem</c> in the type's text (which may be the Diagram's) and the extent's centre,
/// so for any connector whose two placements differ the line ended where nothing was drawn - and
/// <c>mlqt_edit_class's add_connection</c> and <c>mlqt_edit_class's set_component_placement</c> wrote those points into the user's
/// file. MSL has 157 files using <c>iconTransformation</c>.</para>
/// </summary>
public class DiagramRoutingTests
{
    private static readonly string Package = """
        within;
        package R "r"
          connector RealInput = input Real;
          connector RealOutput = output Real;

          block Src "src"
            RealOutput y annotation (Placement(transformation(extent={{100,-10},{120,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Src;

          block Framed "placed differently on its two layers"
            RealInput u annotation (Placement(
              transformation(extent={{-140,-20},{-100,20}}),
              iconTransformation(extent={{0,60},{40,100}})));
            annotation (
              Diagram(coordinateSystem(extent={{-200,-200},{200,200}})),
              Icon(coordinateSystem(extent={{-100,-100},{100,100}}),
                graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Framed;

          model Sys "straight"
            Src src annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Framed f annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
          equation
            connect(src.y, f.u);
          end Sys;

          model Turned "rotated about an origin that is not the extent's centre"
            Src src annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Framed f annotation (Placement(transformation(
              extent={{0,-10},{20,10}}, rotation=90, origin={0,0})));
          equation
            connect(src.y, f.u);
          end Turned;

          model Fan "one source, two sinks: four endpoints on three components"
            Src src annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Framed f1 annotation (Placement(transformation(extent={{-10,20},{10,40}})));
            Framed f2 annotation (Placement(transformation(extent={{-10,-40},{10,-20}})));
          equation
            connect(src.y, f1.u);
            connect(src.y, f2.u);
          end Fan;
        end R;
        """.Replace("\r\n", "\n");

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "RealInput\nRealOutput\nSrc\nFramed\nSys\nTurned\nFan\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static IReadOnlyList<DiagramGeometry.Pt> Route(TestHost host, string classId)
    {
        var code = host.Libraries.GetModelById(classId)!.Definition.ModelicaCode!;
        var route = DiagramGeometry.RouteConnection(host.Libraries, classId, code, "src.y", "f.u");
        Assert.NotNull(route);
        return route!;
    }

    [Fact]
    public void ALineEndsAtTheConnectorsIconPlacement_NotItsDiagramPlacement()
    {
        using var host = Load();

        var route = Route(host, "R.Sys");

        // u's icon placement is centred on (20,80) of a 200-unit icon, and f is a 20-unit box at the
        // origin: (2,8). Its diagram placement, read against the Diagram's 400-unit system, gave (-6,0).
        Assert.Equal(new DiagramGeometry.Pt(-39, 0), route[0]);
        Assert.Equal(new DiagramGeometry.Pt(2, 8), route[^1]);
    }

    [Fact]
    public void ARotationTurnsAboutTheOrigin_AsTheImageDoes()
    {
        using var host = Load();

        var route = Route(host, "R.Turned");

        // The icon point (20,80) lands at (2,8) from the extent centre (10,0), i.e. (12,8) from the
        // origin; turned 90 degrees about the origin that is (-8,12). Turning about the extent's
        // centre instead put it at (2,10).
        Assert.Equal(new DiagramGeometry.Pt(-8, 12), route[^1]);
    }

    [Fact]
    public async Task TheLineWrittenIntoTheFileEndsThereToo()
    {
        using var host = Load();
        var diagram = new DiagramTools(host.Libraries, host.Resources, host.Session);

        // Re-placing src refreshes the connection's Line through the shared annotator.
        ToolAssert.Ok<StructureEditResult>(await diagram.SetComponentPlacement("R.Sys", "src", -60, -10, -40, 10));

        var code = host.Libraries.GetModelById("R.Sys")!.Definition.ModelicaCode!;
        Assert.Matches(@"connect\(src\.y, f\.u\) annotation \(Line\(points=\{\{-39,0\},.*\{2,8\}\}", code);
    }

    /// <summary>
    /// A router builds each component once, however many connections end on it (B392). Routing
    /// rebuilt both endpoints' components - the icon down its extends chain, its connectors, its
    /// parameter values - for every connection, and the annotator routes every connection in the
    /// class on each edit: on MSL's <c>CauerLowPassSC</c> that was 0.98 s per edit, 0.24 s since.
    /// </summary>
    [Fact]
    public void ARouterBuildsEachComponentOnce_HoweverManyConnectionsEndOnIt()
    {
        using var host = Load();
        var code = host.Libraries.GetModelById("R.Fan")!.Definition.ModelicaCode!;
        var router = new DiagramGeometry.Router(host.Libraries, "R.Fan", code);

        var first = router.Route("src.y", "f1.u");
        var second = router.Route("src.y", "f2.u");

        // Four endpoints, three components.
        Assert.Equal(3, router.ComponentsBuilt);

        // And the same answers as routing each connection on its own.
        Assert.Equal(DiagramGeometry.RouteConnection(host.Libraries, "R.Fan", code, "src.y", "f1.u"), first);
        Assert.Equal(DiagramGeometry.RouteConnection(host.Libraries, "R.Fan", code, "src.y", "f2.u"), second);
    }

    [Fact]
    public void ARouterSeededWithTheImagesComponents_BuildsNone()
    {
        using var host = Load();
        var code = host.Libraries.GetModelById("R.Fan")!.Definition.ModelicaCode!;
        var placements = DiagramGeometry.Placements(host.Libraries, "R.Fan", code);
        var built = new[] { "src", "f1", "f2" }
            .Select(n => DiagramImage.ComponentOn(host.Libraries, "R.Fan", placements, n)!)
            .ToList();
        var router = new DiagramGeometry.Router(host.Libraries, "R.Fan", code, built);

        var route = router.Route("src.y", "f2.u");

        Assert.Equal(0, router.ComponentsBuilt);
        Assert.Equal(DiagramGeometry.RouteConnection(host.Libraries, "R.Fan", code, "src.y", "f2.u"), route);
    }

    [Fact]
    public void AnLWhoseCornerIsInFrontOfBothConnectors_IsTheRoute_WithNoLeadThatDoublesBack()
    {
        // A connector on one component's bottom edge to one a few units lower on another's left edge:
        // the router added a 15-unit lead out of each first, so the line went down past the turn, back
        // up and across, over itself. Dymola draws the plain L (VehicleInterfaces' engine mount to its
        // chassis frame, which is this geometry).
        const string pkg = """
            within;
            package L "l"
              connector Frame "acausal"
                Real s;
                flow Real f;
              end Frame;
              model Mounted "a frame on its bottom edge"
                Frame mount annotation (Placement(transformation(extent={{-10,-110},{10,-90}})));
                annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
              end Mounted;
              model Chassis "a frame on its left edge"
                Frame frame annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
                annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
              end Chassis;
              model Sys
                Mounted engine annotation (Placement(transformation(extent={{-110,-30},{-80,0}})));
                Chassis chassis annotation (Placement(transformation(extent={{40,-54},{80,-14}})));
              equation
                connect(engine.mount, chassis.frame);
              end Sys;
            end L;
            """;
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = pkg.ReplaceLineEndings("\n"),
            ["package.order"] = "Frame\nMounted\nChassis\nSys\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var code = host.Libraries.GetModelById("L.Sys")!.Definition.ModelicaCode!;

        var route = DiagramGeometry.RouteConnection(host.Libraries, "L.Sys", code, "engine.mount", "chassis.frame");

        Assert.Equal([new(-95, -30), new(-95, -34), new(40, -34)], route);
    }

    [Fact]
    public void AnLWhoseCornerIsOnAConnector_IsNotTaken_SoNoLineRunsAlongAnEdge()
    {
        // b's corner is b itself: the L would be one segment entering b from the side, along the top
        // edge of b's component. The routed line enters b from above, as b faces.
        var route = DiagramGeometry.Route(new(0, 0), new(1, 0), new(20, 0), new(0, 1));

        Assert.True(route.Count > 2);
        Assert.Equal(route[^2].X, route[^1].X); // the last segment is vertical: into b from above
        Assert.True(route[^2].Y > route[^1].Y);
    }

    private const string Arrays = """
        within;
        package Q "q"
          connector RealInput = input Real;
          connector RealOutput = output Real;
          block Table "two outputs on its right edge"
            RealOutput y[2] annotation (Placement(transformation(extent={{100,-10},{120,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Table;
          block Sink "an input on its left edge"
            RealInput u annotation (Placement(transformation(extent={{-120,-10},{-100,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Sink;
          model Sys
            Table table annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Sink sink1 annotation (Placement(transformation(extent={{40,-10},{60,10}})));
            Sink sinks[2] annotation (Placement(transformation(extent={{40,30},{60,50}})));
          end Sys;
        end Q;
        """;

    [Fact]
    public void AnArraysElements_EndWhereTheArrayIsDrawn()
    {
        // An array of connectors, or of components, is drawn once, and each element's line ends there.
        // Read with its subscript, y[1] was no connector (a line from the table's centre) and sinks[1]
        // no component (no line at all).
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Arrays.ReplaceLineEndings("\n") });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        var code = host.Libraries.GetModelById("Q.Sys")!.Definition.ModelicaCode!;

        var element = DiagramGeometry.RouteConnection(host.Libraries, "Q.Sys", code, "table.y[1]", "sink1.u");
        var whole = DiagramGeometry.RouteConnection(host.Libraries, "Q.Sys", code, "table.y", "sink1.u");
        var intoArray = DiagramGeometry.RouteConnection(host.Libraries, "Q.Sys", code, "table.y[2]", "sinks[2].u");

        Assert.Equal(whole, element);
        Assert.NotNull(intoArray);
        Assert.Equal(new DiagramGeometry.Pt(39, 40), intoArray![^1]); // sinks' input, on its left edge
    }
}

using MLQT.McpServer.Helpers;
using ModelicaParser.Icons;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A diagram draws each base's Diagram graphics as the extends clause's <c>DiagramMap</c> says (MLS
/// 3.6 §18.6.3, B420): hidden with <c>primitivesVisible=false</c>, mapped into a region with a
/// non-null extent. Both used to be ignored - the base's graphics were drawn, at their own size.
/// </summary>
public class DiagramMapTests
{
    private static readonly string Package = """
        within;
        package C "c"
          partial model Square "a square filling the default system"
            annotation (Diagram(graphics={Rectangle(extent={{-80,-80},{80,80}})}));
          end Square;

          model Hidden "hides the base's graphics"
            extends Square annotation (DiagramMap(primitivesVisible=false));
            annotation (Diagram(graphics={Ellipse(extent={{-7,-7},{7,7}})}));
          end Hidden;

          model Mapped "maps the base into the upper right quarter"
            extends Square annotation (DiagramMap(extent={{0,0},{100,100}}));
          end Mapped;

          model Plain "draws the base where it is"
            extends Square;
          end Plain;
        end C;
        """.Replace("\r\n", "\n");

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Square\nHidden\nMapped\nPlain\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static string Svg(TestHost host, string classId)
        => DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    [Fact]
    public void TheBaseIsDrawnWhereItIs_WithoutAMap()
    {
        using var host = Load();

        Assert.Contains("<rect x=\"-80\" y=\"-80\" width=\"160\" height=\"160\"", Svg(host, "C.Plain"));
    }

    [Fact]
    public void PrimitivesVisibleFalse_HidesTheBasesGraphics()
    {
        using var host = Load();

        var svg = Svg(host, "C.Hidden");

        Assert.DoesNotContain("<rect x=\"-80\"", svg);
        Assert.Contains("<ellipse", svg);
    }

    [Fact]
    public void AnExtent_MapsTheBasesGraphicsIntoThatRegion()
    {
        using var host = Load();

        var svg = Svg(host, "C.Mapped");

        // -100..100 onto 0..100 is a half, centred at (50,50): the square becomes 10..90, drawn as
        // -40..40 about its origin, which the map moved to the region's centre.
        Assert.Contains("<rect x=\"-40\" y=\"-40\" width=\"80\" height=\"80\" transform=\"translate(50,50)\"", svg);
        Assert.DoesNotContain("<rect x=\"-80\"", svg);
    }

    // --- What a mapped base contributes besides its graphics (B436) --------------------------

    private static readonly string Contents = """
        within;
        package M "m"
          connector Pin "pin"
            Real v;
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Pin;

          partial model Two "a port on its bottom edge"
            Pin pBottom annotation (Placement(transformation(extent={{-10,-110},{10,-90}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
          end Two;

          model Flipped "maps its base upside down, as Buildings' HeatRecoveryChiller does"
            extends Two annotation (IconMap(extent={{-600,600},{600,-600}}));
            annotation (Icon(coordinateSystem(extent={{-600,-600},{600,600}})));
          end Flipped;

          model User "a Flipped with a line to its port"
            Flipped f annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            Pin src annotation (Placement(transformation(extent={{-10,40},{10,60}})));
          equation
            connect(src, f.pBottom);
          end User;

          partial model Wired "three pins, one connection routed by its author and one not"
            Pin a annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Pin b annotation (Placement(transformation(extent={{40,-10},{60,10}})));
            Pin c annotation (Placement(transformation(extent={{40,-60},{60,-40}})));
          equation
            connect(a, b) annotation (Line(points={{-40,0},{40,0}}));
            connect(a, c);
          end Wired;

          model Quarter "maps the wired base into the upper right quarter"
            extends Wired annotation (DiagramMap(extent={{0,0},{100,100}}));
          end Quarter;

          model Nested "reaches Wired through Quarter, and maps Quarter into the lower left"
            extends Quarter annotation (DiagramMap(extent={{-100,-100},{0,0}}));
          end Nested;
        end M;
        """.Replace("\r\n", "\n");

    private static TestHost LoadContents()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Contents,
            ["package.order"] = "Pin\nTwo\nFlipped\nUser\nWired\nQuarter\nNested\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static Dictionary<string, DiagramGeometry.Placement> Placements(
        TestHost host, string classId, DiagramGeometry.Layer layer = DiagramGeometry.Layer.Diagram)
        => DiagramGeometry.Placements(
            host.Libraries, classId, host.Libraries.GetModelById(classId)!.Definition.ModelicaCode!, layer);

    [Fact]
    public void AnIconMap_PutsTheBasesConnectorsWhereItPutsItsGraphics()
    {
        using var host = LoadContents();

        // The base's bottom edge, mapped upside down and six times the size: the top.
        var port = Placements(host, "M.Flipped", DiagramGeometry.Layer.Icon)["pBottom"];

        Assert.Equal([-60, 660, 60, 540], port.Extent);
        Assert.Equal([0, 600], port.RotationCentre);
    }

    [Fact]
    public void AnIconMap_DoesNotMoveTheBasesComponentsOnTheDiagram()
    {
        using var host = LoadContents();

        Assert.Equal([-10, -110, 10, -90], Placements(host, "M.Flipped")["pBottom"].Extent);
    }

    [Fact]
    public void ALineToAMappedBasesConnector_EndsWhereTheConnectorIsDrawn()
    {
        using var host = LoadContents();
        var code = host.Libraries.GetModelById("M.User")!.Definition.ModelicaCode!;
        var placements = DiagramGeometry.Placements(host.Libraries, "M.User", code);
        var f = DiagramImage.ComponentOn(host.Libraries, "M.User", placements, "f")!;

        var route = DiagramGeometry.RouteConnection(host.Libraries, "M.User", code, "src", "f.pBottom")!;

        // f is a 20-unit box over a 1200-unit icon: the port the map put at (0,600) is at (0,10),
        // the top edge, facing up - and the line ends there, on the connector the image draws.
        var drawn = DiagramSvgRenderer.PortOf(f, "pBottom")!.Value;
        Assert.Equal((0d, 10d, 0d, 1d), (drawn.X, drawn.Y, drawn.FacingX, drawn.FacingY));
        Assert.Equal(new DiagramGeometry.Pt(0, 10), route[^1]);
    }

    [Fact]
    public void ADiagramMap_PutsTheBasesComponentsWhereItPutsItsGraphics()
    {
        using var host = LoadContents();

        var placements = Placements(host, "M.Quarter");

        // -100..100 onto 0..100: a half, centred at (50,50).
        Assert.Equal([20, 45, 30, 55], placements["a"].Extent);
        Assert.Equal([70, 20, 80, 30], placements["c"].Extent);
    }

    [Fact]
    public void ADiagramMap_MapsTheBasesConnectLines_RoutedOrNot()
    {
        using var host = LoadContents();

        var svg = Svg(host, "M.Quarter");

        // The route Wired wrote down, mapped; and the one it did not, routed between the mapped
        // components' centres (25,50) and (75,25).
        Assert.Contains("<polyline points=\"30,50 70,50\"", svg);
        Assert.Matches("<polyline points=\"25,50 [^\"]* 75,25\"", svg);
    }

    [Fact]
    public void MapsCompose_DownTheExtendsChain()
    {
        using var host = LoadContents();

        // Wired into Quarter's upper right quarter, and Quarter into Nested's lower left: x -> x/4 - 25.
        Assert.Equal([-40, -27.5, -35, -22.5], Placements(host, "M.Nested")["a"].Extent);
        Assert.Contains("<polyline points=\"-35,-25 -15,-25\"", Svg(host, "M.Nested"));
    }
}

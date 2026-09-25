using ModelicaParser.Icons;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// Composing a diagram out of the icons of the things on it (B196).
///
/// <para>The renderer is the pure half — given placements and icons it draws; it resolves nothing.
/// These pin the decisions in it that are not arithmetic: what happens to a component with no icon,
/// what happens to one placed outside the canvas, and that <c>%name</c> becomes the component's own
/// name, which is how every icon in the Modelica Standard Library labels itself.</para>
/// </summary>
public class DiagramSvgRendererTests
{
    private static IconData Box(int[]? color = null) => new()
    {
        Graphics =
        [
            new RectanglePrimitive { Extent = [-100, -100, 100, 100], LineColor = color ?? [0, 0, 0] },
            new TextPrimitive { Extent = [-100, 110, 100, 150], TextString = "%name" },
        ],
    };

    /// <summary>
    /// The declared coordinate system's outline, drawn only when something is outside it. Named by
    /// its colour, which nothing else uses: its dash pattern is a length on the page and so depends
    /// on how far the view is zoomed in.
    /// </summary>
    private const string CanvasOutline = "stroke=\"#c0c0c0\"";

    private static DiagramComponent At(string name, double x, double y, IconData? icon, double rotation = 0)
        => new(name, [x - 10, y - 10, x + 10, y + 10], rotation, icon);

    [Fact]
    public void AComponentIsDrawnWithItsOwnIconAtItsPlacement()
    {
        var svg = DiagramSvgRenderer.Render(null, [At("src", -50, 0, Box([255, 0, 0]))], []);

        Assert.Contains("<svg", svg);
        Assert.Contains("translate(-50,0)", svg);
        Assert.Contains("#FF0000", svg);
        // 20 units of placement for 200 units of icon.
        Assert.Contains("scale(0.1,0.1)", svg);
    }

    [Fact]
    public void PercentNameBecomesTheComponentsName()
    {
        var svg = DiagramSvgRenderer.Render(null, [At("resistor1", 0, 0, Box())], []);

        Assert.Contains(">resistor1<", svg);
        Assert.DoesNotContain("%name", svg);
    }

    [Fact]
    public void AComponentWithNoIconIsDrawnAsANamedBox()
    {
        // Not skipped: an unresolved type and an absent component must not look the same to someone
        // judging a layout they cannot otherwise see.
        var svg = DiagramSvgRenderer.Render(null, [At("mystery", 0, 0, null)], []);

        Assert.Contains(">mystery<", svg);
        Assert.Contains("stroke-dasharray", svg);
    }

    [Fact]
    public void ARotatedComponentTurnsAboutItsCentre()
    {
        var svg = DiagramSvgRenderer.Render(null, [At("r", 20, 30, Box(), rotation: 90)], []);

        Assert.Contains("translate(20,30) rotate(90)", svg);
    }

    [Fact]
    public void AReversedExtentMirrorsTheIconRatherThanFailing()
    {
        // Modelica turns a component end for end by writing its extent backwards, so the negative
        // scale is the meaning rather than a case to guard against.
        var svg = DiagramSvgRenderer.Render(
            null, [new DiagramComponent("m", [10, -10, -10, 10], 0, Box())], []);

        Assert.Contains("scale(-0.1,0.1)", svg);
    }

    [Fact]
    public void ConnectionsAreDrawnInTheirOwnColourWhenTheyHaveOne()
    {
        var svg = DiagramSvgRenderer.Render(null, [],
        [
            new DiagramConnection([[-40, 0], [0, 0], [0, 20], [40, 20]], [0, 128, 0]),
            new DiagramConnection([[-40, -20], [40, -20]]),
        ]);

        Assert.Contains("points=\"-40,0 0,0 0,20 40,20\" fill=\"none\" stroke=\"#008000\"", svg);
        Assert.Contains("stroke=\"#0000C8\"", svg);
    }

    [Fact]
    public void AConnectionOfOnePointIsNotALine()
    {
        var svg = DiagramSvgRenderer.Render(null, [], [new DiagramConnection([[0, 0]])]);

        Assert.DoesNotContain("<polyline", svg);
    }

    [Fact]
    public void WhatFitsInTheCanvasIsDrawnAtTheCanvasSize()
    {
        var svg = DiagramSvgRenderer.Render(null, [At("a", 0, 0, Box())], []);

        Assert.Contains("viewBox=\"-100 -100 200 200\"", svg);
        // The declared canvas is only outlined when something is outside it.
        Assert.DoesNotContain(CanvasOutline, svg);
    }

    [Fact]
    public void AComponentPlacedOutsideTheCanvasIsStillShown_AndTheCanvasIsOutlined()
    {
        // A Modelica viewer would clip it, which hides the single most useful thing the picture has
        // to say: that the agent put something where the model does not reach.
        var svg = DiagramSvgRenderer.Render(null, [At("stray", 400, 0, Box())], []);

        Assert.Contains(CanvasOutline, svg);
        Assert.DoesNotContain("viewBox=\"-100 -100 200 200\"", svg);
        Assert.Contains("translate(400,0)", svg);
    }

    [Fact]
    public void ARotatedComponentIsMeasuredByItsSweep_NotItsExtent()
    {
        // Turned 45 degrees, a box reaches past the corner of its own extent; measuring the extent
        // would clip exactly the thing the rotation did.
        var square = DiagramSvgRenderer.Render(null, [At("a", 100, 0, Box())], []);
        var turned = DiagramSvgRenderer.Render(null, [At("a", 100, 0, Box(), rotation: 45)], []);

        Assert.NotEqual(square, turned);
        Assert.Contains(CanvasOutline, turned);
    }

    [Fact]
    public void TheClassesOwnDiagramGraphicsAreDrawnUnderTheComponents()
    {
        var layer = new IconData
        {
            CoordinateExtent = [-200, -100, 200, 100],
            Graphics = [new RectanglePrimitive { Extent = [-190, -90, 190, 90], LineColor = [0, 255, 0] }],
        };

        var svg = DiagramSvgRenderer.Render(layer, [At("a", 0, 0, Box([0, 0, 255]))], []);

        Assert.Contains("viewBox=\"-200 -100 400 200\"", svg);
        Assert.True(svg.IndexOf("#00FF00", StringComparison.Ordinal)
                    < svg.IndexOf("#0000FF", StringComparison.Ordinal),
            "the class's own graphics are a background and are drawn first");
    }

    [Fact]
    public void TheHeightFollowsTheAspectRatio()
    {
        var layer = new IconData { CoordinateExtent = [-200, -50, 200, 50] };

        var svg = DiagramSvgRenderer.Render(layer, [At("a", 0, 0, Box())], [], width: 800);

        Assert.Contains("width=\"800\" height=\"200\"", svg);
    }

    [Fact]
    public void NothingAtAllIsStillAValidDocument()
    {
        var svg = DiagramSvgRenderer.Render(null, [], []);

        Assert.StartsWith("<svg", svg);
        Assert.Contains("</svg>", svg);
        Assert.DoesNotContain("<polyline", svg);
    }

    [Fact]
    public void NullsAreRefusedRatherThanTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => DiagramSvgRenderer.Render(null, null!, []));
        Assert.Throws<ArgumentNullException>(() => DiagramSvgRenderer.Render(null, [], null!));
        Assert.Throws<ArgumentNullException>(() => DiagramSvgRenderer.ToParent(null!, 0, 0));
        Assert.Throws<ArgumentNullException>(() => DiagramSvgRenderer.PortOf(null!, "u"));
        Assert.Throws<ArgumentNullException>(() => DiagramSvgRenderer.PortOnEdge(null!, 0, 0));
    }

    // --- Where a point of an icon lands (B314) --------------------------------------------------
    //
    // A connection line has to end on the connector the picture draws, so the router asks these
    // rather than working the transform out a second time. They are held to the SVG transform the
    // drawing is made with: the same component, asked both ways, must agree.

    private static DiagramComponent Framed(
        double[] extent, double rotation = 0, double[]? origin = null, IconData? icon = null,
        IReadOnlyList<DiagramComponent>? children = null)
        => new("f", extent, rotation, icon ?? Box(), null, origin, children);

    private static void AssertPoint(double x, double y, (double X, double Y) actual)
    {
        Assert.Equal(x, actual.X, 9);
        Assert.Equal(y, actual.Y, 9);
    }

    [Fact]
    public void AnIconPointIsScaledIntoThePlacement()
    {
        AssertPoint(15, 8, DiagramSvgRenderer.ToParent(Framed([0, 0, 20, 20]), 50, -20));
    }

    [Fact]
    public void AnIconPointTurnsAboutTheOrigin_NotTheExtentCentre()
    {
        // Extent centre (10,0), origin (0,0): the icon's right edge is 20 from the origin, and
        // turned a quarter it is straight above it.
        var turned = Framed([0, -10, 20, 10], rotation: 90, origin: [0, 0]);

        AssertPoint(0, 20, DiagramSvgRenderer.ToParent(turned, 100, 0));
        // With no origin it turns about its own centre.
        AssertPoint(10, 10, DiagramSvgRenderer.ToParent(Framed([0, -10, 20, 10], rotation: 90), 100, 0));
    }

    [Fact]
    public void AnIconPointIsMeasuredInTheIconsOwnCoordinateSystem()
    {
        var wide = new IconData
        {
            CoordinateExtent = [-200, -100, 200, 100],
            Graphics = [new RectanglePrimitive { Extent = [-200, -100, 200, 100] }],
        };

        AssertPoint(20, 0, DiagramSvgRenderer.ToParent(Framed([-20, -10, 20, 10], icon: wide), 200, 0));
    }

    [Fact]
    public void AComponentWithNoIconIsFramedByTheDefaultSystem()
    {
        var bare = new DiagramComponent("b", [-10, -10, 10, 10], 0, null);

        AssertPoint(10, 0, DiagramSvgRenderer.ToParent(bare, 100, 0));
    }

    [Fact]
    public void AnIconWithNoAreaLandsOnThePlacementsCentre()
    {
        var flat = new IconData { CoordinateExtent = [0, 0, 0, 0] };
        var component = new DiagramComponent("z", [0, 0, 20, 40], 0, flat);

        AssertPoint(10, 20, DiagramSvgRenderer.ToParent(component, 100, 100));
        Assert.Equal(new DiagramPort(10, 20, 0, 0), DiagramSvgRenderer.PortOnEdge(component, 1, 0));
    }

    [Fact]
    public void APortIsWhereItsConnectorIsDrawn_AndFacesItsEdge()
    {
        // The connector's box is centred on (0,90) of the icon: the top edge.
        var top = new DiagramComponent("c", [-10, 80, 10, 100], 0, null);
        var component = Framed([-10, -10, 10, 10], children: [top]);

        var port = DiagramSvgRenderer.PortOf(component, "c");

        Assert.NotNull(port);
        Assert.Equal(0, port!.Value.X, 9);
        Assert.Equal(9, port.Value.Y, 9);
        Assert.Equal((0d, 1d), (port.Value.FacingX, port.Value.FacingY));
    }

    [Fact]
    public void APortTurnsWithItsComponent_AndWithItsOwnPlacement()
    {
        // A connector placed with its own origin, off its extent's centre, and turned: it lands
        // where its own icon's centre is drawn, not at its extent's centre.
        var connector = new DiagramComponent("c", [80, -20, 120, 0], 180, Box(), null, [100, 0]);
        var component = Framed([-10, -10, 10, 10], rotation: 90, children: [connector]);

        var port = DiagramSvgRenderer.PortOf(component, "c")!.Value;

        // Turned about (100,0) the connector's centre (100,-10) goes to (100,10); in the component
        // that is (10,1), and a quarter turn takes it to (-1,10), facing up.
        Assert.Equal(-1, port.X, 9);
        Assert.Equal(10, port.Y, 9);
        Assert.Equal((0d, 1d), (port.FacingX, port.FacingY));
    }

    [Fact]
    public void AMirroredComponentsPortFacesTheOtherWay()
    {
        var right = new DiagramComponent("y", [90, -10, 110, 10], 0, null);
        var mirrored = new DiagramComponent("m", [10, -10, -10, 10], 0, Box(), null, null, [right]);

        var port = DiagramSvgRenderer.PortOf(mirrored, "y")!.Value;

        Assert.Equal(-10, port.X, 9);
        Assert.Equal((-1d, 0d), (port.FacingX, port.FacingY));
    }

    [Fact]
    public void AConnectorTheComponentDoesNotShowHasNoPort()
    {
        Assert.Null(DiagramSvgRenderer.PortOf(Framed([-10, -10, 10, 10]), "u"));
        Assert.Null(DiagramSvgRenderer.PortOf(
            Framed([-10, -10, 10, 10], children: [new DiagramComponent("y", [90, -10, 110, 10], 0, null)]), "u"));
        Assert.Null(DiagramSvgRenderer.PortOf(
            new DiagramComponent("z", [0, 0, 1, 1], 0, new IconData { CoordinateExtent = [0, 0, 0, 0] }, null, null,
                [new DiagramComponent("u", [0, 0, 1, 1], 0, null)]), "u"));
    }

    [Fact]
    public void AConnectorWhoseIconHasNoAreaIsFoundAtItsPlacement()
    {
        var flat = new DiagramComponent("c", [-110, -10, -90, 10], 0, new IconData { CoordinateExtent = [5, 5, 5, 5] });

        var port = DiagramSvgRenderer.PortOf(Framed([-10, -10, 10, 10], children: [flat]), "c")!.Value;

        Assert.Equal(-10, port.X, 9);
        Assert.Equal((-1d, 0d), (port.FacingX, port.FacingY));
    }

    [Fact]
    public void AnEdgePortIsAFractionOfTheIcon_AndItsCentreFacesNowhere()
    {
        var component = Framed([0, 0, 20, 20], rotation: 180);

        Assert.Equal(new DiagramPort(20, 10, 1, 0), Rounded(DiagramSvgRenderer.PortOnEdge(component, -1, 0)));
        Assert.Equal(new DiagramPort(10, 10, 0, 0), Rounded(DiagramSvgRenderer.PortOnEdge(component, 0, 0)));
        Assert.Equal(new DiagramPort(10, 0, 0, -1), Rounded(DiagramSvgRenderer.PortOnEdge(component, 0, 1)));
    }

    private static DiagramPort Rounded(DiagramPort p)
        => new(Math.Round(p.X, 9) + 0, Math.Round(p.Y, 9) + 0, p.FacingX + 0, p.FacingY + 0);

    [Fact]
    public void TheDrawingAndThePortAgree()
    {
        // The whole point: the SVG transform and ToParent are one frame. A component turned about an
        // off-centre origin writes translate(origin) rotate translate(centre - origin).
        var svg = DiagramSvgRenderer.Render(null, [Framed([0, -10, 20, 10], rotation: 90, origin: [0, 0])], []);

        Assert.Contains("translate(0,0) rotate(90) translate(10,0) scale(0.1,0.1)", svg);
    }
}

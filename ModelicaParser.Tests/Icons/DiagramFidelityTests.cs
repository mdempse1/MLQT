using System.Text.RegularExpressions;
using ModelicaParser.Icons;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// The things a diagram gets wrong that only show up beside a Modelica tool's own render of the same
/// model, reported against <c>Modelica.Blocks.Examples.PID_Controller</c>.
///
/// <para>Three of them are here because they are the renderer's own: a line thickness is a length on
/// the <em>page</em> and not in the drawing, a <c>Line</c>'s arrow is part of the line, and a
/// component whose Placement extent runs right to left is mirrored — but the words on it still have
/// to be read.</para>
/// </summary>
public class DiagramFidelityTests
{
    private static readonly double[] Square = [-100, -100, 100, 100];

    private static IconData Icon(params GraphicsPrimitive[] graphics) =>
        new() { CoordinateExtent = [.. Square], Graphics = [.. graphics] };

    private static double StrokeWidth(string svg)
    {
        var match = Regex.Match(svg, @"stroke-width=""([\d.]+)""");
        Assert.True(match.Success, "nothing in the render carries a stroke width");
        return double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // --- thickness is physical -------------------------------------------------------------------

    [Theory]
    [InlineData(400)]
    [InlineData(800)]
    [InlineData(1600)]
    public void ADefaultLineComesOutOnePixelWideAtEveryZoom(int width)
    {
        // Modelica states lineThickness in millimetres, so the coordinate units it comes to depend
        // on how far in the view is zoomed. A constant gave the red frames of PID_Controller a
        // ten-pixel outline where Dymola draws a hairline.
        var svg = DiagramSvgRenderer.Render(
            Icon(new RectanglePrimitive { Extent = [-90, -90, 90, 90], LineColor = [255, 0, 0] }),
            [], [], width);

        // The view is the 200-unit coordinate system, so pixels per unit is width/200.
        var pixels = StrokeWidth(svg) * width / 200.0;
        Assert.Equal(1.0, pixels, 3);
    }

    [Fact]
    public void ThicknessStaysProportionalToWhatWasAskedFor()
    {
        var thin = DiagramSvgRenderer.Render(
            Icon(new RectanglePrimitive { Extent = [-90, -90, 90, 90], LineThickness = 0.25 }), [], [], 800);
        var thick = DiagramSvgRenderer.Render(
            Icon(new RectanglePrimitive { Extent = [-90, -90, 90, 90], LineThickness = 1.0 }), [], [], 800);

        Assert.Equal(4 * StrokeWidth(thin), StrokeWidth(thick), 3);
    }

    [Fact]
    public void AnIconIsStillDrawnTheWayItAlwaysWas()
    {
        // The icon path is calibrated for 24px in the library tree and is not what changed here.
        var svg = IconSvgRenderer.RenderToSvg(
            Icon(new RectanglePrimitive { Extent = [-90, -90, 90, 90], LineThickness = 0.25 }));

        Assert.Equal(IconSvgRenderer.DefaultUnitsPerMillimetre * 0.25, StrokeWidth(svg!), 3);
    }

    // --- arrows ----------------------------------------------------------------------------------

    [Fact]
    public void ALinesArrowIsDrawn()
    {
        // The label pointing at the PI controller came out as a plain red stroke with no head.
        var svg = DiagramSvgRenderer.Render(
            Icon(new LinePrimitive
            {
                Points = [[-76, -44], [-57, -23]],
                LineColor = [255, 0, 0],
                ArrowEnd = "Filled",
            }),
            [], [], 800);

        Assert.Contains("<polygon", svg);
        Assert.Contains("fill=\"#FF0000\"", svg);
    }

    [Fact]
    public void AnArrowAtEachEndIsTwoArrows_AndNoneIsNone()
    {
        IconData Line(string? start, string? end) => Icon(new LinePrimitive
        {
            Points = [[-50, 0], [50, 0]],
            ArrowStart = start ?? "None",
            ArrowEnd = end ?? "None",
        });

        Assert.Empty(Regex.Matches(DiagramSvgRenderer.Render(Line(null, null), [], []), "<polygon"));
        Assert.Single(Regex.Matches(DiagramSvgRenderer.Render(Line(null, "Filled"), [], []), "<polygon"));
        Assert.Equal(2, Regex.Matches(DiagramSvgRenderer.Render(Line("Filled", "Filled"), [], []), "<polygon").Count);
    }

    [Fact]
    public void AnOpenArrowIsNotFilledIn()
    {
        var svg = DiagramSvgRenderer.Render(
            Icon(new LinePrimitive { Points = [[-50, 0], [50, 0]], ArrowEnd = "Open" }), [], [], 800);

        Assert.Contains("<polyline", svg);
        Assert.DoesNotContain("<polygon", svg);
    }

    [Fact]
    public void AnArrowPointsAlongTheLineItIsOn()
    {
        // Along the last segment, away from the point before it — a poly-line's arrow follows its
        // final leg and not the straight line from where it started.
        var svg = DiagramSvgRenderer.Render(
            Icon(new LinePrimitive { Points = [[-50, 0], [0, 0], [0, 50]], ArrowEnd = "Filled" }), [], [], 800);

        var polygon = Regex.Match(svg, @"<polygon points=""([^""]+)""");
        Assert.True(polygon.Success);

        // Its tip is the line's last point, and its base is below it, not to the left.
        var points = polygon.Groups[1].Value.Split(' ')
            .Select(p => p.Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray())
            .ToList();

        Assert.Equal(0, points[0][0], 3);
        Assert.Equal(50, points[0][1], 3);
        Assert.All(points.Skip(1), p => Assert.True(p[1] < 50, "the barbs sit behind the tip, along the last leg"));
    }

    // --- mirroring draws, it does not write -------------------------------------------------------

    [Fact]
    public void AMirroredComponentKeepsItsWordsReadable()
    {
        // MSL places speedSensor as extent={{22,-50},{2,-30}} — right to left, which mirrors it. The
        // gauge is mirrored and "speedSensor" is not, which is what Dymola draws and what anyone
        // reading a label expects.
        var icon = Icon(new TextPrimitive { Extent = [-100, 110, 100, 150], TextString = "%name" });

        var normal = DiagramSvgRenderer.Render(null, [new DiagramComponent("s", [2, -50, 22, -30], 0, icon)], []);
        var mirrored = DiagramSvgRenderer.Render(null, [new DiagramComponent("s", [22, -50, 2, -30], 0, icon)], []);

        Assert.Contains("transform=\"scale(1,-1)\"", normal);
        Assert.Contains("transform=\"scale(-1,-1)\"", mirrored);
        Assert.Contains(">s<", mirrored);
    }

    [Fact]
    public void TheDrawingItselfIsStillMirrored()
    {
        var icon = Icon(new PolygonPrimitive { Points = [[-100, 100], [100, 0], [-100, -100]] });

        var mirrored = DiagramSvgRenderer.Render(
            null, [new DiagramComponent("s", [22, -50, 2, -30], 0, icon)], []);

        Assert.Contains("scale(-0.1,0.1)", mirrored);
    }

    // --- connectors on an icon --------------------------------------------------------------------

    [Fact]
    public void AComponentsConnectorsAreDrawnInsideIt()
    {
        // Without these a diagram is a row of boxes with lines ending near them.
        var port = Icon(new EllipsePrimitive { Extent = [-100, -100, 100, 100], FillColor = [95, 95, 95] });
        var body = Icon(new RectanglePrimitive { Extent = [-100, -50, 100, 50] });

        var svg = DiagramSvgRenderer.Render(null,
        [
            new DiagramComponent("inertia", [-10, -10, 10, 10], 0, body, "Inertia", null,
            [
                new DiagramComponent("flange_a", [-110, -10, -90, 10], 0, port),
                new DiagramComponent("flange_b", [90, -10, 110, 10], 0, port),
            ]),
        ], []);

        // Each inside the component's own group, in the icon's coordinates.
        Assert.Contains("translate(-100,0)", svg);
        Assert.Contains("translate(100,0)", svg);
        Assert.Equal(2, Regex.Matches(svg, "<ellipse").Count);
    }

    [Fact]
    public void AConnectorOnAnIconIsDrawnAtTheSameWeightAsEverythingElse()
    {
        // It is two transforms down, each scaling by a tenth, so a constant stroke would come out a
        // hundredth of the width the rest of the drawing is.
        var port = Icon(new EllipsePrimitive { Extent = [-100, -100, 100, 100] });
        var body = Icon(new RectanglePrimitive { Extent = [-100, -50, 100, 50] });

        var svg = DiagramSvgRenderer.Render(null,
        [
            new DiagramComponent("c", [-10, -10, 10, 10], 0, body, null, null,
                [new DiagramComponent("p", [-110, -10, -90, 10], 0, port)]),
        ], [], 800);

        var rect = Regex.Match(svg, @"<rect[^>]*stroke-width=""([\d.]+)""[^>]*/>\s*<g");
        var ellipse = Regex.Match(svg, @"<ellipse[^>]*stroke-width=""([\d.]+)""");
        Assert.True(ellipse.Success, "the connector was not drawn");

        // The pixel width is what matters, and it is the same for both.
        var svgLines = svg.Split('\n');
        var bodyStroke = double.Parse(Regex.Match(svgLines.First(l => l.Contains("<rect x=\"-100\"")), @"stroke-width=""([\d.]+)""")
            .Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var portStroke = double.Parse(ellipse.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        // The connector's frame is a tenth of the body's, so its stroke must be ten times the number
        // to land on the same width.
        Assert.Equal(10.0, portStroke / bodyStroke, 1);
    }
}

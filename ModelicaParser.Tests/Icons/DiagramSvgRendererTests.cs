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
    }
}

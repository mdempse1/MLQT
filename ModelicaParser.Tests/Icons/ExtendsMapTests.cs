using ModelicaParser.Helpers;
using ModelicaParser.Icons;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// What an extends clause's <c>IconMap</c> / <c>DiagramMap</c> does to the base it names (MLS 3.6
/// §18.6.3, B420): a non-null extent maps the base's coordinate system, and what is drawn in it, into
/// that region - keeping the aspect ratio when the base preserves it - and
/// <c>primitivesVisible=false</c> hides the base's graphics.
/// </summary>
/// <remarks>
/// Before B420 the map was read only to choose the coordinate system (B394): a mapped base was drawn
/// in the derived class's coordinates at its own size, and a hidden one was drawn anyway. MSL has four
/// <c>IconMap(primitivesVisible=false)</c> clauses, among them <c>Sensors.RelativeAngles</c> and
/// RobotR3's <c>AxisType1</c>, whose base graphics Dymola hides; Buildings'
/// <c>Templates.Plants.HeatPumps.Components.HeatRecoveryChiller</c> maps its base into
/// <c>{{-600,600},{600,-600}}</c>, which flips it vertically.
/// </remarks>
public class ExtendsMapTests
{
    // --- What the extractor records ---------------------------------------------------------

    [Fact]
    public void TheExtractorRecordsEachClausesMap_ForItsOwnLayerOnly()
    {
        const string code = """
            model M
              extends A annotation (IconMap(extent={{-50,-50},{50,50}}));
              extends B annotation (IconMap(primitivesVisible=false), DiagramMap(extent={{-10,-10},{10,10}}));
              extends C annotation (IconMap(extent={{0,0},{0,0}}, primitivesVisible=true));
              extends D annotation (IconMap(extent={{-20,-20},{20,20}}, primitivesVisible=false));
              extends E;
            end M;
            """;

        var icon = IconExtractor.ExtractIconWithInheritance(code)!;
        Assert.Equal([-50, -50, 50, 50], icon.MapFor("A")!.Region!);
        Assert.True(icon.MapFor("A")!.PrimitivesVisible);
        Assert.Null(icon.MapFor("B")!.Region);
        Assert.False(icon.MapFor("B")!.PrimitivesVisible);
        Assert.Null(icon.MapFor("C"));
        Assert.Equal([-20, -20, 20, 20], icon.MapFor("D")!.Region!);
        Assert.False(icon.MapFor("D")!.PrimitivesVisible);
        Assert.Null(icon.MapFor("E"));
        Assert.Equal(["A", "D"], icon.MappedExtends.Order());

        var diagram = IconExtractor.ExtractDiagramWithInheritance(ModelicaParserHelper.Parse(code))!;
        Assert.Equal(["B"], diagram.ExtendsMaps.Keys);
        Assert.Equal([-10, -10, 10, 10], diagram.MapFor("B")!.Region!);
        Assert.True(diagram.MapFor("B")!.PrimitivesVisible);
    }

    [Fact]
    public void AnAnnotationWithNoMap_RecordsNone()
    {
        var result = IconExtractor.ExtractIconWithInheritance("""
            model M
              extends A annotation (Documentation(info="x"));
              extends B annotation (IconMap);
            end M;
            """)!;

        Assert.Empty(result.ExtendsMaps);
    }

    // --- The icon merge ---------------------------------------------------------------------

    private static readonly Dictionary<string, string> Bases = new()
    {
        ["Square"] = """
            partial model Square
              extends Dot;
              annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
            end Square;
            """,
        ["Dot"] = """
            partial model Dot
              annotation (Icon(coordinateSystem(extent={{-100,-100},{100,100}}), graphics={Ellipse(extent={{-10,-10},{10,10}})}));
            end Dot;
            """,
        ["Loose"] = """
            partial model Loose
              annotation (Icon(coordinateSystem(extent={{-100,-100},{100,100}}, preserveAspectRatio=false),
                graphics={Rectangle(extent={{-100,-100},{100,100}})}));
            end Loose;
            """,
    };

    private static IconData Icon(string code)
        => IconSvgRenderer.ExtractIconWithInheritance(code, n => Bases.GetValueOrDefault(n))!;

    [Fact]
    public void PrimitivesVisibleFalse_HidesTheBasesGraphics_AndItsBasesToo_ButKeepsTheClassesOwn()
    {
        var icon = Icon("""
            model M
              extends Square annotation (IconMap(primitivesVisible=false));
              annotation (Icon(graphics={Line(points={{0,0},{10,10}})}));
            end M;
            """);

        Assert.IsType<LinePrimitive>(Assert.Single(icon.Graphics));
    }

    [Fact]
    public void AHiddenBase_StillLendsItsCoordinateSystem()
    {
        var bases = new Dictionary<string, string>
        {
            ["Wide"] = """
                partial model Wide
                  annotation (Icon(coordinateSystem(extent={{-300,-300},{300,300}}), graphics={Rectangle(extent={{-300,-300},{300,300}})}));
                end Wide;
                """,
        };

        var icon = IconSvgRenderer.ExtractIconWithInheritance("""
            model M
              extends Wide annotation (IconMap(primitivesVisible=false));
              annotation (Icon(graphics={Line(points={{0,0},{10,10}})}));
            end M;
            """, n => bases.GetValueOrDefault(n))!;

        Assert.Equal([-300, -300, 300, 300], icon.CoordinateExtent);
        Assert.Single(icon.Graphics);
    }

    [Fact]
    public void AMappedBase_IsDrawnInsideItsRegion()
    {
        var icon = Icon("""
            model M
              extends Square annotation (IconMap(extent={{0,0},{100,100}}));
            end M;
            """);

        // Dot's ellipse and Square's rectangle, both scaled by a half about the region's centre.
        Assert.Equal(2, icon.Graphics.Count);
        Assert.Equal([45, 45, 55, 55], Bounds(icon.Graphics[0]));
        Assert.Equal([0, 0, 100, 100], Bounds(icon.Graphics[1]));
        Assert.Equal([-100, -100, 100, 100], icon.CoordinateExtent);
    }

    [Fact]
    public void AMappedBaseThatPreservesItsAspectRatio_IsScaledEvenly_AndCentred()
    {
        var icon = Icon("""
            model M
              extends Square annotation (IconMap(extent={{0,0},{100,50}}));
            end M;
            """);

        Assert.Equal([25, 0, 75, 50], Bounds(icon.Graphics[1]));
    }

    [Fact]
    public void AMappedBaseThatDoesNotPreserveItsAspectRatio_FillsTheRegion()
    {
        var icon = Icon("""
            model M
              extends Loose annotation (IconMap(extent={{0,0},{100,50}}));
            end M;
            """);

        Assert.Equal([0, 0, 100, 50], Bounds(Assert.Single(icon.Graphics)));
    }

    [Fact]
    public void AMappedBaseIsNotRecordedAsLendingItsSystem_AndAnUnmappedOneIsDrawnWhereItIs()
    {
        var icon = Icon("""
            model M
              extends Loose annotation (IconMap(extent={{0,0},{100,50}}));
              extends Dot;
            end M;
            """);

        Assert.True(icon.PreserveAspectRatio);   // from Dot, the first unmapped base
        Assert.Equal([-10, -10, 10, 10], Bounds(icon.Graphics[1]));
    }

    // --- The mapping ------------------------------------------------------------------------

    private static IconData System(double[] extent, bool preserve = true)
        => new() { CoordinateExtent = extent, PreserveAspectRatio = preserve };

    /// <summary>Where a primitive's local point lands: its origin plus the point turned by its rotation.</summary>
    private static (double X, double Y) Place(GraphicsPrimitive p, double x, double y)
    {
        var r = p.Rotation * Math.PI / 180;
        return (p.Origin[0] + x * Math.Cos(r) - y * Math.Sin(r), p.Origin[1] + x * Math.Sin(r) + y * Math.Cos(r));
    }

    private static double[] Bounds(GraphicsPrimitive p)
    {
        var e = p switch
        {
            RectanglePrimitive r => r.Extent,
            EllipsePrimitive e2 => e2.Extent,
            TextPrimitive t => t.Extent,
            BitmapPrimitive b => b.Extent,
            _ => throw new InvalidOperationException(),
        };
        var corners = new[] { Place(p, e[0], e[1]), Place(p, e[2], e[3]), Place(p, e[0], e[3]), Place(p, e[2], e[1]) };
        return [.. new[] { corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y) }
            .Select(v => Math.Round(v, 6))];
    }

    // Every corner of a rectangle and every point of a line lands where mapping the drawn point does.
    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(90, 2, 1)]
    [InlineData(180, 2, 1)]
    [InlineData(270, 2, 1)]
    [InlineData(-90, 2, 1)]
    [InlineData(90, 2, -1)]
    [InlineData(90, -2, 1)]
    [InlineData(270, -2, -1)]
    [InlineData(30, 1.5, 1.5)]
    [InlineData(30, 1.5, -1.5)]
    [InlineData(30, -1.5, -1.5)]
    public void EveryPointLandsWhereTheMapPutsIt(double rotation, double sx, double sy)
    {
        var rect = new RectanglePrimitive { Origin = [10, 20], Rotation = rotation, Extent = [-10, -20, 30, 40] };
        var line = new LinePrimitive { Origin = [-5, 5], Rotation = rotation, Points = [[0, 0], [10, 0], [10, 30]] };
        double[] region = [100 - 100 * sx, 50 - 100 * sy, 100 + 100 * sx, 50 + 100 * sy];

        var mapped = GraphicsMapping.Into([rect, line], System([-100, -100, 100, 100], preserve: false), region);

        (double, double) Map((double X, double Y) p) => (100 + sx * p.X, 50 + sy * p.Y);

        var r = (RectanglePrimitive)mapped[0];
        foreach (var (x, y) in new[] { (0, 1), (2, 3), (0, 3), (2, 1) })
            AssertNear(Map(Place(rect, rect.Extent[x], rect.Extent[y])), Place(r, r.Extent[x], r.Extent[y]));

        var l = (LinePrimitive)mapped[1];
        for (var i = 0; i < line.Points.Count; i++)
            AssertNear(Map(Place(line, line.Points[i][0], line.Points[i][1])), Place(l, l.Points[i][0], l.Points[i][1]));
    }

    private static void AssertNear((double X, double Y) expected, (double X, double Y) actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Theory]
    [InlineData(1, 1, 30, 90)]
    [InlineData(-1, 1, 90, 150)]
    [InlineData(1, -1, -90, -30)]
    [InlineData(-1, -1, 210, 270)]
    public void AnArcIsMirroredWithIt(double sx, double sy, double start, double end)
    {
        var arc = new EllipsePrimitive { Extent = [-10, -10, 10, 10], StartAngle = 30, EndAngle = 90 };

        var mapped = (EllipsePrimitive)GraphicsMapping.Into(
            [arc], System([-100, -100, 100, 100]), [-100 * sx, -100 * sy, 100 * sx, 100 * sy])[0];

        Assert.Equal((start, end), (mapped.StartAngle, mapped.EndAngle));
    }

    [Fact]
    public void AWholeEllipseStaysWhole()
    {
        var mapped = (EllipsePrimitive)GraphicsMapping.Into(
            [new EllipsePrimitive()], System([-100, -100, 100, 100]), [100, 100, -100, -100])[0];

        Assert.Equal((0.0, 360.0), (mapped.StartAngle, mapped.EndAngle));
    }

    [Fact]
    public void AnArcLateInTheTurnIsStillMirrored()
    {
        var mapped = (EllipsePrimitive)GraphicsMapping.Into(
            [new EllipsePrimitive { StartAngle = 270, EndAngle = 300 }], System([-100, -100, 100, 100]), [100, -100, -100, 100])[0];

        Assert.Equal((-120.0, -90.0), (mapped.StartAngle, mapped.EndAngle));
    }

    [Fact]
    public void ABaseSystemAwayFromTheOrigin_IsMappedFromItsOwnCentre()
    {
        var rect = new RectanglePrimitive { Origin = [150, 75], Extent = [-50, -25, 50, 25] };

        var mapped = GraphicsMapping.Into([rect], System([0, 0, 200, 100], preserve: false), [-100, -50, -60, -30])[0];

        // 0..200 x 0..100 onto -100..-60 x -50..-30: a fifth each way, (100,50) to (-80,-40).
        Assert.Equal([-70, -35], mapped.Origin);
        Assert.Equal([-80, -40, -60, -30], Bounds(mapped));
    }

    [Fact]
    public void NothingToMapIsRefused()
    {
        var system = System([-100, -100, 100, 100]);
        Assert.Throws<ArgumentNullException>(() => GraphicsMapping.Into(null!, system, [0, 0, 1, 1]));
        Assert.Throws<ArgumentNullException>(() => GraphicsMapping.Into([], null!, [0, 0, 1, 1]));
        Assert.Throws<ArgumentNullException>(() => GraphicsMapping.Into([], system, null!));
    }

    [Fact]
    public void AMirroredPolygonTurnsTheOtherWay()
    {
        var polygon = new PolygonPrimitive { Rotation = 45, Points = [[0, 0], [10, 0], [0, 10]] };

        var mapped = (PolygonPrimitive)GraphicsMapping.Into(
            [polygon], System([-100, -100, 100, 100]), [-100, 100, 100, -100])[0];

        Assert.Equal(-45, mapped.Rotation);
        Assert.Equal([[0, 0], [10, 0], [0, -10]], mapped.Points);
    }

    [Fact]
    public void TextBitmapAndCornerRadiusAreScaled_AndPageLengthsAreNot()
    {
        GraphicsPrimitive[] graphics =
        [
            new TextPrimitive { Extent = [-100, -20, 100, 20], FontSize = 12 },
            new BitmapPrimitive { Extent = [-50, -50, 50, 50] },
            new RectanglePrimitive { Extent = [-50, -50, 50, 50], Radius = 10, LineThickness = 0.5 },
        ];

        var mapped = GraphicsMapping.Into(graphics, System([-100, -100, 100, 100]), [-50, -50, 50, 50]);

        Assert.Equal([-50, -10, 50, 10], ((TextPrimitive)mapped[0]).Extent);
        Assert.Equal(12, ((TextPrimitive)mapped[0]).FontSize);
        Assert.Equal([-25, -25, 25, 25], ((BitmapPrimitive)mapped[1]).Extent);
        Assert.Equal(5, ((RectanglePrimitive)mapped[2]).Radius);
        Assert.Equal(0.5, mapped[2].LineThickness);
    }

    [Fact]
    public void TheGivenPrimitivesAreNotChanged()
    {
        var rect = new RectanglePrimitive { Origin = [10, 10], Extent = [-10, -10, 10, 10] };

        GraphicsMapping.Into([rect], System([-100, -100, 100, 100]), [0, 0, 20, 20]);

        Assert.Equal([10, 10], rect.Origin);
        Assert.Equal([-10, -10, 10, 10], rect.Extent);
    }

    [Fact]
    public void AReversedBaseExtentIsReadAsTheSameSystem()
    {
        var mapped = GraphicsMapping.Into(
            [new RectanglePrimitive { Extent = [-100, -100, 100, 100] }], System([100, 100, -100, -100]), [0, 0, 100, 100]);

        Assert.Equal([0, 0, 100, 100], Bounds(mapped[0]));
    }

    [Theory]
    [InlineData(new double[] { 0, -100, 0, 100 }, new double[] { 0, 0, 100, 100 })]
    [InlineData(new double[] { -100, 5, 100, 5 }, new double[] { 0, 0, 100, 100 })]
    [InlineData(new double[] { -100, -100, 100, 100 }, new double[] { 0, 0 })]
    public void ASystemWithNoArea_OrARegionThatIsNotOne_LeavesTheGraphicsWhereTheyAre(double[] extent, double[] region)
    {
        var rect = new RectanglePrimitive { Extent = [-10, -10, 10, 10] };

        var mapped = GraphicsMapping.Into([rect], System(extent), region);

        Assert.Same(rect, Assert.Single(mapped));
    }

    [Fact]
    public void APrimitiveWithNoOriginIsMappedFromTheOrigin()
    {
        var mapped = GraphicsMapping.Into(
            [new LinePrimitive { Origin = [], Points = [[0, 0]] }], System([-100, -100, 100, 100]), [0, 0, 200, 200]);

        Assert.Equal([100, 100], mapped[0].Origin);
    }
}

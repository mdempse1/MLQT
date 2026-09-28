using ModelicaParser.Icons;
using Xunit;

namespace ModelicaParser.Tests.Icons;

/// <summary>
/// <see cref="CoordinateMap"/>: where an extends clause's <c>IconMap</c> / <c>DiagramMap</c> puts
/// what its base contributes (MLS 3.6 §18.6.3). B420 mapped the base's graphics; B436 maps its
/// components' placements and its connect lines with the same map, so a connector on an icon is
/// drawn where the base's graphics put its edge, and a line ends on it.
/// </summary>
public class CoordinateMapTests
{
    private static IconData System(double[] extent, bool preserve = true)
        => new() { CoordinateExtent = extent, PreserveAspectRatio = preserve };

    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 9);

    [Fact]
    public void Into_PutsTheBasesSystemOntoTheRegion()
    {
        var map = CoordinateMap.Into(System([-100, -100, 100, 100], preserve: false), [0, 0, 100, 50])!.Value;

        Assert.Equal((0d, 0d), map.Point(-100, -100));
        Assert.Equal((100d, 50d), map.Point(100, 100));
        Assert.Equal((50d, 25d), map.Point(0, 0));
    }

    [Fact]
    public void Into_AReversedRegionMirrors_AsHeatRecoveryChillersDoes()
    {
        // Buildings' HeatRecoveryChiller: a -100..100 base into {{-600,600},{600,-600}}, which puts
        // the ports its base draws at the bottom at the top.
        var map = CoordinateMap.Into(System([-100, -100, 100, 100]), [-600, 600, 600, -600])!.Value;

        Assert.Equal((-600d, 360d), map.Point(-100, -60));
        Assert.Equal(6, map.Sx);
        Assert.Equal(-6, map.Sy);
    }

    [Fact]
    public void Into_KeepsTheAspectRatioAndCentres_WhereTheBasePreservesIt()
    {
        var map = CoordinateMap.Into(System([-100, -100, 100, 100]), [0, 0, 200, 100])!.Value;

        Assert.Equal(0.5, map.Sx);
        Assert.Equal(0.5, map.Sy);
        Assert.Equal((100d, 50d), map.Point(0, 0));
    }

    [Fact]
    public void Into_IsNull_ForASystemOrRegionWithNoArea()
    {
        Assert.Null(CoordinateMap.Into(System([0, -100, 0, 100]), [0, 0, 10, 10]));
        Assert.Null(CoordinateMap.Into(System([-100, 0, 100, 0]), [0, 0, 10, 10]));
        Assert.Null(CoordinateMap.Into(System([-100, -100, 100, 100]), [0, 0, 10]));
        Assert.Throws<ArgumentNullException>(() => CoordinateMap.Into(null!, [0, 0, 1, 1]));
        Assert.Throws<ArgumentNullException>(() => CoordinateMap.Into(System([-1, -1, 1, 1]), null!));
    }

    [Fact]
    public void Then_ComposesTwoClauses_InnerFirst()
    {
        var inner = new CoordinateMap(2, 10, 3, 20);   // a base's base into the base
        var outer = new CoordinateMap(-1, 5, 0.5, -4); // the base into the class

        var both = inner.Then(outer);
        var (ix, iy) = inner.Point(7, 11);

        Assert.Equal(outer.Point(ix, iy), both.Point(7, 11));
        Assert.True(CoordinateMap.Identity.Then(CoordinateMap.Identity).IsIdentity);
        Assert.False(both.IsIdentity);
    }

    [Fact]
    public void Point_OfAnArray_DropsAnythingPastTheTwoCoordinates_AndCopiesAShortOne()
    {
        var map = new CoordinateMap(2, 1, 2, 1);

        Assert.Equal([5, 7], map.Point([2, 3, 99]));
        double[] shortPoint = [4];
        var copy = map.Point(shortPoint);
        Assert.Equal([4], copy);
        Assert.NotSame(shortPoint, copy);
        Assert.Throws<ArgumentNullException>(() => map.Point(null!));
    }

    /// <summary>
    /// The contract that matters: a component placed by the mapped placement draws every point of its
    /// icon exactly where the map puts that point of the component placed as the base wrote it. Asked
    /// through <see cref="DiagramSvgRenderer.ToParent(DiagramComponent, double, double)"/>, which is
    /// what the drawing and the connection router both use.
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    [InlineData(270, false)]
    [InlineData(-90, false)]
    [InlineData(90, true)]
    [InlineData(0, true)]
    public void Placement_DrawsEveryIconPointWhereTheMapPutsIt(double rotation, bool offCentreOrigin)
    {
        double[] extent = [80, -30, 120, 10];                     // a 40x40 box, as a connector's
        double[]? centre = offCentreOrigin ? [90, -20] : null;    // the transformation's origin
        var icon = System([-100, -100, 100, 100]);

        foreach (var map in new[]
                 {
                     new CoordinateMap(6, 0, -6, 0),       // HeatRecoveryChiller's vertical flip
                     new CoordinateMap(-2, 5, 2, -3),      // mirrored in x
                     new CoordinateMap(-1, 0, -1, 0),      // turned half way
                     new CoordinateMap(3, 7, 0.5, 1),      // uneven, no mirror
                     new CoordinateMap(-3, 7, 0.5, 1),     // uneven and mirrored
                 })
        {
            var written = new DiagramComponent("c", extent, rotation, icon, RotationCentre: centre);
            var (mappedExtent, mappedRotation, mappedCentre) = map.Placement(extent, rotation, centre);
            var drawn = new DiagramComponent("c", mappedExtent, mappedRotation, icon, RotationCentre: mappedCentre);

            foreach (var (x, y) in new[] { (-100d, -100d), (100d, 0d), (30d, 70d), (0d, 0d) })
            {
                var (wx, wy) = DiagramSvgRenderer.ToParent(written, x, y);
                var expected = map.Point(wx, wy);
                var actual = DiagramSvgRenderer.ToParent(drawn, x, y);
                Near(expected.X, actual.X);
                Near(expected.Y, actual.Y);
            }
        }
    }

    [Fact]
    public void Placement_AMirrorTurnsTheRotationTheOtherWay_AndMapsTheOrigin()
    {
        var map = new CoordinateMap(1, 0, -1, 0);

        var (extent, rotation, centre) = map.Placement([-10, 40, 10, 60], 90, [0, 50]);

        Assert.Equal(-90, rotation);
        Assert.Equal([0, -50], centre);
        Assert.Equal(-50, (extent[1] + extent[3]) / 2);
    }

    [Fact]
    public void Placement_OfAShortExtent_IsCopiedAndItsCentreStillMapped()
    {
        var map = new CoordinateMap(2, 0, 2, 0);

        var (extent, rotation, centre) = map.Placement([1, 2], 45, [1, 1]);
        Assert.Equal([1, 2], extent);
        Assert.Equal(45, rotation);
        Assert.Equal([2, 2], centre);

        var (_, _, none) = map.Placement([1, 2], 0, null);
        Assert.Empty(none);
        Assert.Throws<ArgumentNullException>(() => map.Placement(null!, 0, null));
    }

    [Fact]
    public void Apply_RefusesNull()
        => Assert.Throws<ArgumentNullException>(() => CoordinateMap.Identity.Apply(null!));
}

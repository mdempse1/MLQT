using MLQT.McpServer.Helpers;
using Pt = MLQT.McpServer.Helpers.DiagramGeometry.Pt;

namespace MLQT.McpServer.Tests;

/// <summary>
/// Moving one end of a line to where its connector now is, changing as few points as keep the line
/// orthogonal, so a route drawn round other components survives the move.
/// </summary>
public class LineReshaperTests
{
    // Out of the right edge of one component, down, and into the left edge of another.
    private static readonly Pt[] Route = [new(10, 0), new(20, 0), new(20, -40), new(40, -40)];

    [Fact]
    public void MovingAlongTheLead_ChangesOnlyTheEnd()
    {
        var moved = LineReshaper.MoveEnd(Route, atStart: true, new Pt(0, 0));

        Assert.Equal([new(0, 0), new(20, 0), new(20, -40), new(40, -40)], moved);
    }

    [Fact]
    public void MovingAcrossTheLead_ChangesTheEndAndTheBendAfterIt_AndNoMore()
    {
        var moved = LineReshaper.MoveEnd(Route, atStart: true, new Pt(10, 10));

        Assert.Equal([new(10, 10), new(20, 10), new(20, -40), new(40, -40)], moved);
    }

    [Fact]
    public void MovingTheLastEnd_WalksBackFromIt()
    {
        var moved = LineReshaper.MoveEnd(Route, atStart: false, new Pt(40, -80));

        Assert.Equal([new(10, 0), new(20, 0), new(20, -80), new(40, -80)], moved);
    }

    [Fact]
    public void AStraightLine_GetsADoglegInItsMiddle_RatherThanMovingTheOtherConnector()
    {
        var moved = LineReshaper.MoveEnd([new(10, 0), new(40, 0)], atStart: true, new Pt(10, 20));

        Assert.Equal([new(10, 20), new(25, 20), new(25, 0), new(40, 0)], moved);
    }

    [Fact]
    public void AVerticalStraightLine_GetsItsDoglegAcross()
    {
        var moved = LineReshaper.MoveEnd([new(0, 10), new(0, 50)], atStart: true, new Pt(20, 10));

        Assert.Equal([new(20, 10), new(20, 30), new(0, 30), new(0, 50)], moved);
    }

    [Fact]
    public void ADiagonalTheAuthorDrew_IsKept()
    {
        var moved = LineReshaper.MoveEnd([new(0, 0), new(10, 10), new(30, 10)], atStart: true, new Pt(0, 5));

        Assert.Equal([new(0, 5), new(10, 10), new(30, 10)], moved);
    }

    [Fact]
    public void AStraightLineMovedAlongItself_StaysStraight()
    {
        var moved = LineReshaper.MoveEnd([new(10, 0), new(40, 0)], atStart: true, new Pt(0, 0));

        Assert.Equal([new(0, 0), new(40, 0)], moved);
    }

    /// <summary>
    /// A zero-length stub at the fixed end was kept by the walk and joined to the moved point, which
    /// wrote <c>{70,-90},{90,-90},{90,-90},{90,-90}</c> into a file (B530).
    /// </summary>
    [Fact]
    public void AMove_LeavesNoPointRepeated()
    {
        var moved = LineReshaper.MoveEnd([new(70, -80), new(90, -80), new(90, -90), new(90, -90)], atStart: true, new Pt(70, -90));

        Assert.Equal([new(70, -90), new(90, -90)], moved);
    }

    private static readonly (Pt[] Line, Pt[] Expected)[] Tangles =
    [
        // Out 15 and straight back.
        ([new(0, -100), new(95, -100), new(95, -115), new(95, -100)], [new(0, -100), new(95, -100)]),
        // Back over the start after 3.
        ([new(-20, -40), new(-17, -40), new(-20, -40), new(-20, -52)], [new(-20, -40), new(-20, -52)]),
        // A point repeated: the point the author put on the run stays.
        ([new(-70, -84), new(-70, -87), new(-70, -87), new(-70, -90)], [new(-70, -84), new(-70, -87), new(-70, -90)]),
        // Three identical points.
        ([new(70, -90), new(90, -90), new(90, -90), new(90, -90)], [new(70, -90), new(90, -90)]),
        // A spur off a corner, which leaves the corner.
        ([new(0, 0), new(10, 0), new(10, 10), new(10, 5), new(20, 5)], [new(0, 0), new(10, 0), new(10, 5), new(20, 5)]),
        // Nothing to take out: corners and a point on a straight run are the author's.
        ([new(0, 0), new(5, 0), new(10, 0), new(10, 10)], [new(0, 0), new(5, 0), new(10, 0), new(10, 10)]),
        // Both ends on one point.
        ([new(3, 3), new(3, 3)], [new(3, 3), new(3, 3)]),
    ];

    [Fact]
    public void Untangling_TakesOutRepeatsAndSpurs_AndNothingElse()
    {
        foreach (var (line, expected) in Tangles)
            Assert.Equal(expected, LineReshaper.Untangle(line));
    }

    [Fact]
    public void ATranslation_IsAMoveThatKeepsSizeAndTurn()
    {
        var before = new DiagramGeometry.Placement([0, 0, 20, 20], 0, [10, 10]);

        Assert.Equal(new Pt(5, -3), LineReshaper.Translation(before, new([5, -3, 25, 17], 0, [15, 7])));
        Assert.Null(LineReshaper.Translation(before, new([0, 0, 40, 40], 0, [20, 20])));   // resized
        Assert.Null(LineReshaper.Translation(before, new([0, 0, 20, 20], 90, [10, 10])));  // turned
        Assert.Null(LineReshaper.Translation(before, new([20, 0, 0, 20], 0, [10, 10])));   // flipped
        Assert.Null(LineReshaper.Translation(null, before));                                // never placed
    }

    [Fact]
    public void ATranslation_IsFoundThroughFloatingPointNoise()
    {
        // 0.1 -> 0.3 and 20.1 -> 20.3 differ by 0.19999999999999998 and 0.1999999999999993: the same move.
        var before = new DiagramGeometry.Placement([0.1, 0, 20.1, 20], 0, [10.1, 10]);
        var after = new DiagramGeometry.Placement([0.3, 0, 20.3, 20], 0, [10.3, 10]);

        Assert.Equal(new Pt(0.2, 0), LineReshaper.Translation(before, after));
    }

    /// <summary>
    /// A turned component is where its extent turned about its origin puts it, and that is what
    /// moved (B528). The origin-less form turns about {0,0}; re-placing it in the canonical form,
    /// with the same extent written and the origin at the box's centre, moves it 30 left and 30 down
    /// though its extent did not move at all - read off the extent, the lines stayed where they were.
    /// </summary>
    [Fact]
    public void ATurnedComponent_MovesWhereItIsDrawn_NotWhereItsExtentIsWritten()
    {
        var unoriginated = new DiagramGeometry.Placement([-10, -40, 10, -20], 90, [0, 0]);
        var canonical = new DiagramGeometry.Placement([-10, -40, 10, -20], 90, [0, -30]);

        Assert.Equal([20, -10, 40, 10], unoriginated.Box);
        Assert.Equal([-10, -40, 10, -20], canonical.Box);
        Assert.Equal(new Pt(-30, -30), LineReshaper.Translation(unoriginated, canonical));

        // And a line's end is matched to the box it is drawn in.
        Assert.Equal(0, LineReshaper.DistanceTo(new Pt(30, 0), unoriginated));
        Assert.True(LineReshaper.DistanceTo(new Pt(0, -30), unoriginated) > 20); // inside the extent as written
    }

    [Fact]
    public void AnUnturnedComponentsBox_IsItsExtent_Sorted()
    {
        Assert.Equal([0, 0, 20, 20], new DiagramGeometry.Placement([20, 20, 0, 0], 0, [0, 0]).Box);
    }
}

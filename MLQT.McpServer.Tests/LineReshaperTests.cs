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
}

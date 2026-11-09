using Pt = MLQT.McpServer.Helpers.DiagramGeometry.Pt;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Moves one end of a connection line to where its connector now is, changing as few of the line's
/// points as will keep it orthogonal, so a route somebody drew round other components survives a move.
/// </summary>
/// <remarks>
/// <para>Re-routing a moved component's lines from scratch replaced a hand-drawn route - one taken
/// round the other components to keep lines apart - with the router's default, and an agent that
/// could not get the shape back deleted the connection and added it again. A move usually shifts one
/// end a few units; the rest of the route is still right.</para>
///
/// <para><b>The walk.</b> The end point goes to its new place. Each following point then changes the
/// one coordinate that keeps the segment before it horizontal or vertical as it was, and the walk
/// stops at the first point that already lines up - so moving a component along a horizontal lead
/// changes one point, and across it two. A diagonal segment ends the walk where it is: it is the
/// author's, and stays a diagonal. The other end is never moved, since its connector did not; where
/// the walk would have to, one dogleg goes into the last segment instead, at its middle.</para>
/// </remarks>
internal static class LineReshaper
{
    /// <summary>
    /// <paramref name="points"/> with the first point (<paramref name="atStart"/>) or the last moved to
    /// <paramref name="end"/> and the line kept orthogonal with as few changes as that takes.
    /// </summary>
    public static IReadOnlyList<Pt> MoveEnd(IReadOnlyList<Pt> points, bool atStart, Pt end)
    {
        if (points.Count < 2)
            return points;

        var old = atStart ? points.ToList() : points.Reverse().ToList();
        var moved = old.ToList();
        moved[0] = end;

        for (var i = 1; i < moved.Count; i++)
        {
            var horizontal = old[i - 1].Y == old[i].Y;
            var vertical = old[i - 1].X == old[i].X;
            var previous = moved[i - 1];

            if (horizontal && vertical)
            {
                // Two points on top of each other: keep them so.
                if (i == moved.Count - 1)
                    break; // the fixed end: a zero-length stub becomes whatever joins the two
                moved[i] = previous;
                continue;
            }

            var lined = horizontal ? moved[i].Y == previous.Y : vertical ? moved[i].X == previous.X : true;
            if (lined)
                break; // this point already lines up, or the segment is a diagonal of the author's

            if (i == moved.Count - 1)
            {
                // The far end's connector did not move: put one dogleg in this segment instead.
                var fixedEnd = moved[i];
                if (horizontal)
                {
                    var midX = (previous.X + fixedEnd.X) / 2;
                    moved.InsertRange(i, [new Pt(midX, previous.Y), new Pt(midX, fixedEnd.Y)]);
                }
                else
                {
                    var midY = (previous.Y + fixedEnd.Y) / 2;
                    moved.InsertRange(i, [new Pt(previous.X, midY), new Pt(fixedEnd.X, midY)]);
                }
                break;
            }

            moved[i] = horizontal ? moved[i] with { Y = previous.Y } : moved[i] with { X = previous.X };
        }

        if (!atStart)
            moved.Reverse();
        return moved;
    }

    /// <summary>Every point moved by the same amount: a line whose two ends moved together.</summary>
    public static IReadOnlyList<Pt> Translate(IReadOnlyList<Pt> points, Pt by)
        => [.. points.Select(p => new Pt(Tidy(p.X + by.X), Tidy(p.Y + by.Y)))];

    /// <summary>
    /// How far a component moved, when the move kept its size and turn - so each of its connectors
    /// moved by exactly that much. Null for a resize, a rotation or a flip, where they did not.
    /// </summary>
    public static Pt? Translation(DiagramGeometry.Placement? before, DiagramGeometry.Placement? after)
    {
        if (before is null || after is null || !Same(before.Rotation, after.Rotation))
            return null;
        var dx = after.Extent[0] - before.Extent[0];
        var dy = after.Extent[1] - before.Extent[1];
        // Compared within a tolerance, and the move rounded to it: 0.1 to 0.3 and 20.1 to 20.3 are the
        // same move, but not to the last bit, and taken for a resize the line went to the router's point.
        return Same(after.Extent[2] - before.Extent[2], dx) && Same(after.Extent[3] - before.Extent[3], dy)
            ? new Pt(Tidy(dx), Tidy(dy))
            : null;
    }

    private const double Tolerance = 1e-6;

    private static bool Same(double a, double b) => Math.Abs(a - b) < Tolerance;

    // A displacement to the precision a diagram is drawn at, so 0.19999999999999998 is written 0.2.
    private static double Tidy(double v) => Math.Round(v, 6);

    /// <summary>
    /// How far a point is from a component's box: zero inside it or on its edge. Which end of a line
    /// is the moved component's is read from this, since a Line need not run from the connect's first
    /// port to its second.
    /// </summary>
    public static double DistanceTo(Pt p, DiagramGeometry.Placement placement)
    {
        var e = placement.Extent;
        var (x1, x2) = (Math.Min(e[0], e[2]), Math.Max(e[0], e[2]));
        var (y1, y2) = (Math.Min(e[1], e[3]), Math.Max(e[1], e[3]));
        var dx = Math.Max(Math.Max(x1 - p.X, 0), p.X - x2);
        var dy = Math.Max(Math.Max(y1 - p.Y, 0), p.Y - y2);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

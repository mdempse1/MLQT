namespace ModelicaParser.Icons;

/// <summary>
/// How a base class's coordinate system is put into a derived class's by an extends clause's
/// <c>IconMap(extent=...)</c> / <c>DiagramMap(extent=...)</c> (MLS 3.6 §18.6.3): x' = Sx·x + Tx,
/// y' = Sy·y + Ty. A negative scale is a mirror. Maps compose, so a base two clauses up is reached
/// through one map (<see cref="Then"/>).
/// </summary>
/// <remarks>
/// <para><b>One map for everything a mapped base contributes</b> (B420, B436): its graphics
/// (<see cref="Apply"/>), the placements of its components (<see cref="Placement"/>) - which is
/// where the connectors on an icon and the components on a diagram are drawn, and so where a
/// connection line has to end - and the points of its connect lines (<see cref="Point(double[])"/>).
/// Mapping any one of them another way puts it somewhere the others are not.</para>
///
/// <para>A mirror turns a rotation the other way, and a quarter turn swaps which axis a scale applies
/// to. That is exact for every case but a non-uniform scale of something turned by an angle that is
/// not a multiple of 90 degrees, which no extent and rotation can express; that one is scaled along
/// its own axes.</para>
/// </remarks>
public readonly record struct CoordinateMap(double Sx, double Tx, double Sy, double Ty)
{
    /// <summary>The map that changes nothing: a base drawn where it is.</summary>
    public static CoordinateMap Identity { get; } = new(1, 0, 1, 0);

    /// <summary>Whether this map changes nothing.</summary>
    public bool IsIdentity => this == Identity;

    /// <summary>
    /// The map that puts <paramref name="system"/> onto <paramref name="region"/>: the base's extent
    /// onto the region, a reversed region mirroring, and - where the base preserves its aspect ratio -
    /// the same scale on both axes with the result centred in the region. Null when either has no area,
    /// which maps onto nothing.
    /// </summary>
    /// <param name="system">The base's resolved coordinate system.</param>
    /// <param name="region">The map's extent, [x1,y1,x2,y2] in the derived class's coordinates.</param>
    public static CoordinateMap? Into(IconData system, double[] region)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(region);

        var from = system.CoordinateExtent;
        var (fx1, fx2) = (Math.Min(from[0], from[2]), Math.Max(from[0], from[2]));
        var (fy1, fy2) = (Math.Min(from[1], from[3]), Math.Max(from[1], from[3]));
        if (fx2 == fx1 || fy2 == fy1 || region.Length < 4)
            return null;

        var sx = (region[2] - region[0]) / (fx2 - fx1);
        var sy = (region[3] - region[1]) / (fy2 - fy1);
        if (system.PreserveAspectRatio)
        {
            var s = Math.Min(Math.Abs(sx), Math.Abs(sy));
            sx = Math.Sign(sx) * s;
            sy = Math.Sign(sy) * s;
        }

        // The centre of the base's system goes to the centre of the region.
        var (fromCx, fromCy) = ((fx1 + fx2) / 2, (fy1 + fy2) / 2);
        var (toCx, toCy) = ((region[0] + region[2]) / 2, (region[1] + region[3]) / 2);
        return new CoordinateMap(sx, toCx - sx * fromCx, sy, toCy - sy * fromCy);
    }

    /// <summary>This map followed by <paramref name="outer"/>: a base's base reached through both clauses.</summary>
    public CoordinateMap Then(CoordinateMap outer)
        => new(outer.Sx * Sx, outer.Sx * Tx + outer.Tx, outer.Sy * Sy, outer.Sy * Ty + outer.Ty);

    /// <summary>Where a point lands.</summary>
    public (double X, double Y) Point(double x, double y) => (Sx * x + Tx, Sy * y + Ty);

    /// <summary>Where a point, as [x,y], lands; anything after the two coordinates is dropped.</summary>
    public double[] Point(double[] point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (point.Length < 2)
            return [.. point];
        var (x, y) = Point(point[0], point[1]);
        return [x, y];
    }

    private bool Mirrored => Sx * Sy < 0;

    // What the scale does to something turned by `rotation`: S·R(θ) = R(θ')·S', where a mirror
    // turns θ the other way and a quarter turn swaps the axes S applies to.
    private (double Rotation, double Ax, double Ay) Turned(double rotation)
    {
        var quarterTurn = ((rotation % 180) + 180) % 180 == 90;
        var (ax, ay) = !quarterTurn ? (Sx, Sy) : Mirrored ? (-Sy, -Sx) : (Sy, Sx);
        return (Mirrored && rotation != 0 ? -rotation : rotation, ax, ay);
    }

    /// <summary>
    /// A component's Placement as it is once mapped: the extent it is drawn in, the rotation, and the
    /// point that rotation turns about, all in the derived class's coordinates. A mirror reverses the
    /// extent, which is how a placement says a component is mirrored.
    /// </summary>
    /// <param name="extent">[x1,y1,x2,y2], absolute - any origin already added in.</param>
    /// <param name="rotation">Degrees counter-clockwise.</param>
    /// <param name="centre">The point the rotation turns about; null for the extent's centre.</param>
    public (double[] Extent, double Rotation, double[] Centre) Placement(
        double[] extent, double rotation, double[]? centre)
    {
        ArgumentNullException.ThrowIfNull(extent);
        if (extent.Length < 4)
            return ([.. extent], rotation, centre is null ? [] : Point(centre));

        var (cx, cy) = ((extent[0] + extent[2]) / 2, (extent[1] + extent[3]) / 2);
        var (hx, hy) = ((extent[2] - extent[0]) / 2, (extent[3] - extent[1]) / 2);
        var (rx, ry) = centre is { Length: >= 2 } ? (centre[0], centre[1]) : (cx, cy);
        var (turned, ax, ay) = Turned(rotation);

        // The rotation centre is a point and maps as one; the extent is turned about it, so the
        // offset of the extent's centre from it is scaled as the icon inside it is, S'.
        var (mrx, mry) = Point(rx, ry);
        var (ncx, ncy) = (mrx + ax * (cx - rx), mry + ay * (cy - ry));
        return ([ncx - ax * hx, ncy - ay * hy, ncx + ax * hx, ncy + ay * hy], turned, [mrx, mry]);
    }

    /// <summary>
    /// A primitive as it is drawn once mapped: its origin mapped as a point, its own geometry scaled.
    /// A new primitive; the one given is not changed. Line thickness, arrow size and font size are
    /// lengths on the page, not in the drawing, and are left alone, as they are for a component's icon.
    /// </summary>
    public GraphicsPrimitive Apply(GraphicsPrimitive primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var copy = primitive.ShallowCopy();

        var origin = primitive.Origin.Length >= 2 ? primitive.Origin : [0, 0];
        copy.Origin = Point(origin);

        var (rotation, ax, ay) = Turned(primitive.Rotation);
        copy.Rotation = rotation;

        switch (copy)
        {
            case RectanglePrimitive r:
                r.Extent = Scale(r.Extent, ax, ay);
                r.Radius = r.Radius * Math.Sqrt(Math.Abs(ax * ay));
                break;
            case EllipsePrimitive e:
                e.Extent = Scale(e.Extent, ax, ay);
                (e.StartAngle, e.EndAngle) = Angles(e.StartAngle, e.EndAngle, ax < 0, ay < 0);
                break;
            case TextPrimitive t:
                t.Extent = Scale(t.Extent, ax, ay);
                break;
            case BitmapPrimitive b:
                b.Extent = Scale(b.Extent, ax, ay);
                break;
            case LinePrimitive l:
                l.Points = [.. l.Points.Select(p => Scale(p, ax, ay))];
                break;
            case PolygonPrimitive p:
                p.Points = [.. p.Points.Select(q => Scale(q, ax, ay))];
                break;
        }

        return copy;
    }

    private static double[] Scale(double[] values, double ax, double ay)
    {
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
            result[i] = values[i] * (i % 2 == 0 ? ax : ay);
        return result;
    }

    // An arc runs counter-clockwise from its start angle to its end angle. Mirrored in x an angle
    // a becomes 180-a, in y it becomes -a, and either mirror reverses the direction, so the ends
    // swap; mirrored in both it is turned half way round. A whole ellipse is left whole: the
    // renderer knows one by its angles being 0 and 360.
    private static (double Start, double End) Angles(double start, double end, bool mirrorX, bool mirrorY)
        => Math.Abs(end - start) >= 360 ? (start, end) : (mirrorX, mirrorY) switch
        {
            (true, true) => (start + 180, end + 180),
            (true, false) => (180 - end, 180 - start),
            (false, true) => (-end, -start),
            _ => (start, end),
        };
}

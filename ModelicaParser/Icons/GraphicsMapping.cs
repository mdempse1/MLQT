namespace ModelicaParser.Icons;

/// <summary>
/// A base class's graphics mapped into a region of the derived class's coordinate system, as an
/// extends clause's <c>IconMap(extent=...)</c> / <c>DiagramMap(extent=...)</c> says (MLS 3.6 §18.6.3,
/// B420).
/// </summary>
/// <remarks>
/// <para>"The base class coordinate system (and contents) is mapped to the region specified by the
/// attributes in the same way as done for components": the base's extent goes onto the region, a
/// reversed region mirrors, and where the base preserves its aspect ratio the scale is the same on
/// both axes and the result is centred in the region.</para>
///
/// <para><b>The primitives are rewritten, not wrapped in a transform.</b> An icon is a flat list of
/// primitives, and every consumer of it - the renderers, the bounds a diagram is framed by, the
/// <c>%name</c> substitution - reads that list. Each primitive's origin is mapped, and its local
/// geometry is scaled: a mirror turns a rotation the other way, and a quarter turn swaps which axis a
/// non-uniform scale applies to. That is exact for every case but a non-uniform scale of a primitive
/// turned by an angle that is not a multiple of 90 degrees, which no transform of an extent and a
/// rotation can express; that one is scaled along its own axes.</para>
///
/// <para>Line thickness, arrow size and font size are lengths on the page, not in the drawing, and
/// are left alone, as they are for a component's icon.</para>
/// </remarks>
public static class GraphicsMapping
{
    /// <summary>
    /// <paramref name="graphics"/>, drawn in <paramref name="system"/>, as they are drawn once that
    /// system is mapped into <paramref name="region"/>.
    /// </summary>
    /// <param name="system">The base's resolved coordinate system: its extent and whether it
    /// preserves its aspect ratio.</param>
    /// <param name="region">The map's extent, [x1,y1,x2,y2] in the derived class's coordinates.</param>
    /// <returns>New primitives; the ones given are not changed. The graphics unchanged when the
    /// base's extent has no area, which maps onto nothing.</returns>
    public static List<GraphicsPrimitive> Into(
        IEnumerable<GraphicsPrimitive> graphics, IconData system, double[] region)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(region);

        var from = system.CoordinateExtent;
        var (fx1, fx2) = (Math.Min(from[0], from[2]), Math.Max(from[0], from[2]));
        var (fy1, fy2) = (Math.Min(from[1], from[3]), Math.Max(from[1], from[3]));
        if (fx2 == fx1 || fy2 == fy1 || region.Length < 4)
            return [.. graphics];

        var sx = (region[2] - region[0]) / (fx2 - fx1);
        var sy = (region[3] - region[1]) / (fy2 - fy1);
        if (system.PreserveAspectRatio)
        {
            var s = Math.Min(Math.Abs(sx), Math.Abs(sy));
            sx = Math.Sign(sx) * s;
            sy = Math.Sign(sy) * s;
        }

        var map = new Map(
            (fx1 + fx2) / 2, (fy1 + fy2) / 2, (region[0] + region[2]) / 2, (region[1] + region[3]) / 2, sx, sy);
        return [.. graphics.Select(map.Apply)];
    }

    private readonly record struct Map(double FromCx, double FromCy, double ToCx, double ToCy, double Sx, double Sy)
    {
        public GraphicsPrimitive Apply(GraphicsPrimitive primitive)
        {
            var copy = primitive.ShallowCopy();

            var origin = primitive.Origin.Length >= 2 ? primitive.Origin : [0, 0];
            copy.Origin = [ToCx + Sx * (origin[0] - FromCx), ToCy + Sy * (origin[1] - FromCy)];

            // Mapping a point turned about the origin: S·R(θ)·p. A mirror turns the rotation the
            // other way, S·R(θ) = R(-θ)·S, and a quarter turn swaps the axes a scale applies to.
            var mirrored = Sx * Sy < 0;
            copy.Rotation = mirrored && primitive.Rotation != 0 ? -primitive.Rotation : primitive.Rotation;
            var quarterTurn = ((primitive.Rotation % 180) + 180) % 180 == 90;
            var (ax, ay) = !quarterTurn ? (Sx, Sy) : mirrored ? (-Sy, -Sx) : (Sy, Sx);

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
}

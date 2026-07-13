using System.Text.RegularExpressions;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Computes an orthogonal (horizontal/vertical only) route for a <c>connect(a, b)</c> line that starts and
/// ends at the actual connector positions on each component, leaving each connector in the direction of the
/// edge it sits on. A connector's position is read from its Placement inside the component's type (mapped
/// through the component's own Placement in the parent, honouring rotation); when the type has no positioned
/// connector, the connector is inferred to sit on the left (an input) or right (an output) edge, else the
/// component centre. If neither endpoint's component is positioned there is nothing to draw (null).
/// </summary>
internal static class DiagramGeometry
{
    public readonly record struct Pt(double X, double Y);
    public readonly record struct Facing(double Dx, double Dy)
    {
        public static readonly Facing None = new(0, 0);
        public bool IsNone => Dx == 0 && Dy == 0;
    }

    private sealed record Placement(double[] Extent, double Rotation);

    private static readonly Regex ExtentRegex = new(
        @"extent\s*=\s*\{\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}\s*,\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}",
        RegexOptions.Compiled);
    private static readonly Regex RotationRegex = new(@"rotation\s*=\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>The orthogonal poly-line for a connection, or null when it cannot be drawn (an endpoint's
    /// component has no Placement). Points are integer diagram coordinates.</summary>
    public static IReadOnlyList<Pt>? RouteConnection(
        ILibraryDataService libraries, string classId, string classCode, string portA, string portB)
    {
        var placements = Placements(classCode);
        var a = Locate(libraries, classId, classCode, placements, portA);
        var b = Locate(libraries, classId, classCode, placements, portB);
        if (a is null || b is null)
            return null;

        return Route(a.Value.Point, a.Value.Facing, b.Value.Point, b.Value.Facing);
    }

    // --- Endpoint location -------------------------------------------------------------------------

    private static (Pt Point, Facing Facing)? Locate(
        ILibraryDataService libraries, string classId, string classCode,
        IReadOnlyDictionary<string, Placement> placements, string portRef)
    {
        var root = Segment(portRef, 0);
        if (!placements.TryGetValue(root, out var comp))
            return null; // component not positioned — cannot route to it

        var centre = Centre(comp.Extent);
        var dot = portRef.IndexOf('.');
        if (dot < 0)
            return (centre, Facing.None); // the port is the component itself (unusual for connect)

        var connectorName = portRef[(dot + 1)..].Split('.')[0];
        var (nx, ny) = ConnectorOffset(libraries, classCode, classId, root, connectorName);

        var (ox, oy) = (nx * HalfWidth(comp.Extent), ny * HalfHeight(comp.Extent));
        var (rx, ry) = Rotate(ox, oy, comp.Rotation);
        var point = new Pt(centre.X + rx, centre.Y + ry);
        return (point, EdgeFacing(nx, ny, comp.Rotation));
    }

    // Normalised connector position within the component's icon coordinate system, roughly in [-1, 1].
    private static (double Nx, double Ny) ConnectorOffset(
        ILibraryDataService libraries, string classCode, string classId, string componentName, string connectorName)
    {
        var typeText = ComponentTypeText(classCode, componentName);
        var typeNode = typeText is null ? null : TypeResolver.Resolve(libraries, classId, typeText, null);
        if (typeNode is null)
            return (0, 0);

        var member = ClassElementResolver
            .Collect(libraries, typeNode, includeProtected: false, includeInherited: true)
            .FirstOrDefault(m => m.Element.Kind == ClassElementKind.Component &&
                                 string.Equals(m.Element.Name, connectorName, StringComparison.Ordinal));
        if (member is null)
            return (0, 0);

        // Prefer the connector's actual Placement (mapped through the icon coordinate system)...
        var ownerCode = libraries.GetModelById(member.OwnerId)?.Definition.ModelicaCode;
        if (ownerCode is not null && Placements(ownerCode).TryGetValue(connectorName, out var cp))
        {
            var c = Centre(cp.Extent);
            var (icx, icy, ihw, ihh) = CoordinateSystem(typeNode.Definition.ModelicaCode);
            return ((c.X - icx) / ihw, (c.Y - icy) / ihh);
        }

        // ...otherwise infer the edge from causality (an input sits on the left, an output on the right).
        return CausalityOffset(libraries, member);
    }

    private static (double, double) CausalityOffset(ILibraryDataService libraries, ResolvedElement member)
    {
        if (string.Equals(member.Element.Causality, "input", StringComparison.Ordinal)) return (-1, 0);
        if (string.Equals(member.Element.Causality, "output", StringComparison.Ordinal)) return (1, 0);

        var type = member.Element.Type ?? string.Empty;
        if (type.Contains("Input", StringComparison.Ordinal)) return (-1, 0);
        if (type.Contains("Output", StringComparison.Ordinal)) return (1, 0);
        return (0, 0); // acausal / unknown — treat as the component centre
    }

    // Which edge the connector sits on, as an outward unit vector, rotated by the component's rotation.
    private static Facing EdgeFacing(double nx, double ny, double rotationDeg)
    {
        if (nx == 0 && ny == 0)
            return Facing.None;
        double dx, dy;
        if (Math.Abs(nx) >= Math.Abs(ny)) { dx = Math.Sign(nx); dy = 0; }
        else { dx = 0; dy = Math.Sign(ny); }
        var (rx, ry) = Rotate(dx, dy, rotationDeg);
        return new Facing(SnapUnit(rx), SnapUnit(ry));
    }

    // --- Orthogonal routing ------------------------------------------------------------------------

    private static IReadOnlyList<Pt> Route(Pt a, Facing fa, Pt b, Facing fb)
    {
        var da = Resolve(fa, a, b);
        var db = Resolve(fb, b, a);
        var stub = Math.Clamp(Distance(a, b) * 0.2, 3, 15);
        var sa = new Pt(a.X + da.Dx * stub, a.Y + da.Dy * stub);
        var sb = new Pt(b.X + db.Dx * stub, b.Y + db.Dy * stub);

        var pts = new List<Pt> { a, sa };
        var aHorizontal = da.Dy == 0;
        var bHorizontal = db.Dy == 0;
        if (aHorizontal && bHorizontal)
        {
            var mx = (sa.X + sb.X) / 2;
            pts.Add(new Pt(mx, sa.Y));
            pts.Add(new Pt(mx, sb.Y));
        }
        else if (!aHorizontal && !bHorizontal)
        {
            var my = (sa.Y + sb.Y) / 2;
            pts.Add(new Pt(sa.X, my));
            pts.Add(new Pt(sb.X, my));
        }
        else if (aHorizontal)
        {
            pts.Add(new Pt(sb.X, sa.Y)); // horizontal then vertical
        }
        else
        {
            pts.Add(new Pt(sa.X, sb.Y)); // vertical then horizontal
        }
        pts.Add(sb);
        pts.Add(b);
        return Clean(pts);
    }

    // A None facing points along the dominant axis toward the other endpoint.
    private static Facing Resolve(Facing f, Pt from, Pt to)
    {
        if (!f.IsNone)
            return f;
        return Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y)
            ? new Facing(Math.Sign(to.X - from.X) is 0 ? 1 : Math.Sign(to.X - from.X), 0)
            : new Facing(0, Math.Sign(to.Y - from.Y) is 0 ? 1 : Math.Sign(to.Y - from.Y));
    }

    // Round, drop consecutive duplicates, then collapse collinear runs so aligned stubs disappear.
    private static IReadOnlyList<Pt> Clean(List<Pt> pts)
    {
        var rounded = pts.Select(p => new Pt(Math.Round(p.X), Math.Round(p.Y))).ToList();
        var dedup = new List<Pt>();
        foreach (var p in rounded)
            if (dedup.Count == 0 || dedup[^1] != p)
                dedup.Add(p);

        var result = new List<Pt>();
        for (var i = 0; i < dedup.Count; i++)
        {
            if (i > 0 && i < dedup.Count - 1)
            {
                var (prev, cur, next) = (dedup[i - 1], dedup[i], dedup[i + 1]);
                // Drop a point only when it lies BETWEEN its neighbours on a straight run. A collinear point
                // that overshoots (a stub that reverses direction, e.g. a right-facing output whose line must
                // then head left) is kept so the line still leaves/enters the connector on its own side.
                var onVertical = prev.X == cur.X && cur.X == next.X && Between(prev.Y, cur.Y, next.Y);
                var onHorizontal = prev.Y == cur.Y && cur.Y == next.Y && Between(prev.X, cur.X, next.X);
                if (onVertical || onHorizontal)
                    continue;
            }
            result.Add(dedup[i]);
        }
        return result.Count >= 2 ? result : dedup;
    }

    // --- Parsing helpers ---------------------------------------------------------------------------

    private static Dictionary<string, Placement> Placements(string classCode)
    {
        var result = new Dictionary<string, Placement>(StringComparer.Ordinal);
        var layout = ClassBodyLocator.Analyze(classCode);
        foreach (var c in layout.Components)
        {
            if (c.DeclStart < 0 || c.DeclStop >= classCode.Length || c.DeclStop < c.DeclStart)
                continue;
            var slice = classCode[c.DeclStart..(c.DeclStop + 1)];
            var e = ExtentRegex.Match(slice);
            if (!e.Success)
                continue;
            var extent = new[] { Num(e.Groups[1].Value), Num(e.Groups[2].Value), Num(e.Groups[3].Value), Num(e.Groups[4].Value) };
            var rot = RotationRegex.Match(slice);
            result[c.Name] = new Placement(extent, rot.Success ? Num(rot.Groups[1].Value) : 0);
        }
        return result;
    }

    private static string? ComponentTypeText(string classCode, string componentName)
        => ClassBodyLocator.Analyze(classCode).Components
            .FirstOrDefault(c => string.Equals(c.Name, componentName, StringComparison.Ordinal))?.TypeText;

    // The icon coordinate system (centre + half extents); Modelica's default is {{-100,-100},{100,100}}.
    private static (double Cx, double Cy, double Hw, double Hh) CoordinateSystem(string? typeCode)
    {
        if (typeCode is not null)
        {
            var idx = typeCode.IndexOf("coordinateSystem", StringComparison.Ordinal);
            if (idx >= 0)
            {
                var m = ExtentRegex.Match(typeCode, idx);
                if (m.Success)
                {
                    var x1 = Num(m.Groups[1].Value); var y1 = Num(m.Groups[2].Value);
                    var x2 = Num(m.Groups[3].Value); var y2 = Num(m.Groups[4].Value);
                    var hw = (x2 - x1) / 2; var hh = (y2 - y1) / 2;
                    if (hw != 0 && hh != 0)
                        return ((x1 + x2) / 2, (y1 + y2) / 2, hw, hh);
                }
            }
        }
        return (0, 0, 100, 100);
    }

    // --- Small maths -------------------------------------------------------------------------------

    private static string Segment(string portRef, int i) => portRef.Split('.')[i];
    private static Pt Centre(double[] e) => new((e[0] + e[2]) / 2, (e[1] + e[3]) / 2);
    private static double HalfWidth(double[] e) => (e[2] - e[0]) / 2;
    private static double HalfHeight(double[] e) => (e[3] - e[1]) / 2;
    private static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    private static double SnapUnit(double v) => Math.Abs(v) < 0.5 ? 0 : Math.Sign(v);
    private static bool Between(double a, double m, double b) => m >= Math.Min(a, b) && m <= Math.Max(a, b);

    private static (double X, double Y) Rotate(double x, double y, double deg)
    {
        if (deg == 0)
            return (x, y);
        var r = deg * Math.PI / 180;
        var (cos, sin) = (Math.Cos(r), Math.Sin(r));
        return (x * cos - y * sin, x * sin + y * cos);
    }
}

using ModelicaGraph.Analysis;
using System.Text.RegularExpressions;
using ModelicaParser.DataTypes;
using ModelicaParser.Icons;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;
using ModelicaParser.Helpers;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Computes an orthogonal (horizontal/vertical only) route for a <c>connect(a, b)</c> line that starts and
/// ends at the actual connector positions on each component, leaving each connector in the direction of the
/// edge it sits on. A connector is where get_diagram_image draws it - its icon-layer Placement inside the
/// component's type, mapped through the component's own Placement by <see cref="DiagramSvgRenderer.PortOf"/>;
/// when the type has no positioned connector, the connector is inferred to sit on the left (an input) or
/// right (an output) edge, else the component centre. If neither endpoint's component is positioned there is nothing to draw (null).
/// </summary>
internal static class DiagramGeometry
{
    public readonly record struct Pt(double X, double Y);
    public readonly record struct Facing(double Dx, double Dy)
    {
        public static readonly Facing None = new(0, 0);
        public bool IsNone => Dx == 0 && Dy == 0;
    }

    /// <summary>A component's Placement: where its type's icon goes, and how it is turned.</summary>
    /// <param name="Extent">[x1,y1,x2,y2] in the enclosing diagram's coordinates, with any
    /// <c>origin</c> already added in — a Modelica extent is stated relative to it.</param>
    /// <param name="Rotation">Degrees counter-clockwise.</param>
    /// <param name="RotationCentre">The point the rotation turns about: the transformation's
    /// <c>origin</c> where it has one, and the extent's own centre where it does not. The two differ
    /// only for an extent that is not symmetric about its origin, which is legal and rare.</param>
    public sealed record Placement(double[] Extent, double Rotation, double[] RotationCentre);

    private static readonly Regex ExtentRegex = new(
        @"extent\s*=\s*\{\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}\s*,\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}",
        RegexOptions.Compiled);
    private static readonly Regex RotationRegex = new(@"rotation\s*=\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex OriginRegex = new(
        @"origin\s*=\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}", RegexOptions.Compiled);

    /// <summary>The orthogonal poly-line for a connection, or null when it cannot be drawn (an endpoint's
    /// component has no Placement). Points are integer diagram coordinates.
    ///
    /// <para>For one connection. A caller routing several in the same class uses one
    /// <see cref="Router"/> for all of them.</para></summary>
    public static IReadOnlyList<Pt>? RouteConnection(
        ILibraryDataService libraries, string classId, string classCode, string portA, string portB)
        => new Router(libraries, classId, classCode).Route(portA, portB);

    /// <summary>
    /// Routes the connections of one class, building each component once however many connections
    /// end on it (B392).
    ///
    /// <para>Locating an endpoint means building its component as the image draws it - the type's
    /// icon down its extends chain, its connectors, its parameter values - and collecting the class's
    /// placements and members first. Done per connection, a component with six wires was built
    /// twelve times over, and <c>ConnectionLineAnnotator</c> routes every connection in the class on
    /// each <c>add_connection</c> and <c>set_component_placement</c>. Nothing here changes while a
    /// class is being routed, so one router serves every connection in it and then is dropped.</para>
    /// </summary>
    public sealed class Router
    {
        private readonly ILibraryDataService _libraries;
        private readonly string _classId;
        private readonly string _classCode;
        private readonly Dictionary<string, DiagramComponent?> _components = new(StringComparer.Ordinal);
        private Dictionary<string, Placement>? _placements;
        private IReadOnlyList<ResolvedElement>? _members;

        /// <param name="classCode">The text to position against, which may be part-way through an edit.</param>
        /// <param name="built">Components the caller has already built exactly as
        /// <see cref="DiagramImage.ComponentOn"/> would - the image has every one of them in hand.</param>
        public Router(
            ILibraryDataService libraries, string classId, string classCode,
            IEnumerable<DiagramComponent>? built = null)
        {
            _libraries = libraries;
            _classId = classId;
            _classCode = classCode;
            foreach (var component in built ?? [])
                _components[component.Name] = component;
        }

        /// <summary>How many components this router has built itself, for the test that holds B392.</summary>
        internal int ComponentsBuilt { get; private set; }

        /// <inheritdoc cref="RouteConnection"/>
        public IReadOnlyList<Pt>? Route(string portA, string portB)
        {
            var a = Locate(portA);
            var b = Locate(portB);
            if (a is null || b is null)
                return null;

            return DiagramGeometry.Route(a.Value.Point, a.Value.Facing, b.Value.Point, b.Value.Facing);
        }

        private Dictionary<string, Placement> PlacementsOf()
            => _placements ??= Placements(_libraries, _classId, _classCode);

        private DiagramComponent? Component(string name)
        {
            if (_components.TryGetValue(name, out var known))
                return known;

            _members ??= _libraries.GetModelById(_classId) is { } node
                ? [.. DiagramImage.Members(_libraries, node)]
                : [];
            var built = DiagramImage.ComponentOn(_libraries, _classId, PlacementsOf(), name, _members);
            ComponentsBuilt++;
            _components[name] = built;
            return built;
        }

        /// <inheritdoc cref="DiagramGeometry.Locate"/>
        private (Pt Point, Facing Facing)? Locate(string portRef)
            => DiagramGeometry.Locate(_libraries, _classId, _classCode, Component(Segment(portRef, 0)), portRef);
    }

    // --- Endpoint location -------------------------------------------------------------------------

    /// <summary>
    /// Where a connection meets <paramref name="portRef"/>, and which way it leaves.
    ///
    /// <para><b>The connector is located on the component the image draws</b> (B314): the type's
    /// icon-layer placement (<c>iconTransformation</c> first), in the type's icon coordinate system,
    /// turned about the placement's <c>origin</c>. This used to be worked out here a second way - the
    /// diagram-layer placement, the first <c>coordinateSystem</c> in the type's text and the extent's
    /// centre - so a line ended where no connector was drawn, and <c>add_connection</c> wrote those
    /// points into the user's file.</para>
    /// </summary>
    private static (Pt Point, Facing Facing)? Locate(
        ILibraryDataService libraries, string classId, string classCode,
        DiagramComponent? component, string portRef)
    {
        var root = Segment(portRef, 0);
        if (component is not { } comp)
            return null; // component not positioned — cannot route to it

        var segments = ModelicaName.Segments(portRef);
        var port = segments.Count < 2
            ? DiagramSvgRenderer.PortOnEdge(comp, 0, 0) // the port is the component itself (unusual for connect)
            : DiagramSvgRenderer.PortOf(comp, segments[1])
              ?? GuessedPort(libraries, classId, classCode, comp, root, segments[1]);

        return (new Pt(port.X, port.Y), new Facing(port.FacingX, port.FacingY));
    }

    // A connector the type does not place on its icon: infer its edge from causality (an input sits
    // on the left, an output on the right), else the component centre.
    private static DiagramPort GuessedPort(
        ILibraryDataService libraries, string classId, string classCode, DiagramComponent comp,
        string componentName, string connectorName)
    {
        var typeText = ComponentTypeText(classCode, componentName);
        var typeNode = typeText is not null
            ? TypeResolver.Resolve(libraries.CombinedGraph, classId, typeText, null)
            : InheritedComponentType(libraries, classId, componentName);
        var member = typeNode is null
            ? null
            : ClassElementResolver
                .Collect(libraries.CombinedGraph, typeNode, includeProtected: false, includeInherited: true)
                .FirstOrDefault(m => m.Element.Kind == ClassElementKind.Component &&
                                     string.Equals(m.Element.Name, connectorName, StringComparison.Ordinal));

        var (nx, ny) = member is null ? (0d, 0d) : CausalityOffset(member);
        return DiagramSvgRenderer.PortOnEdge(comp, nx, ny);
    }

    // The type of a component the class inherits, resolved in the scope of the base that declares
    // it - an inherited connection (B316) names components the class's own text does not.
    private static ModelicaGraph.DataTypes.ModelNode? InheritedComponentType(
        ILibraryDataService libraries, string classId, string componentName)
    {
        if (libraries.GetModelById(classId) is not { } node)
            return null;
        var member = DiagramImage.Members(libraries, node)
            .FirstOrDefault(m => string.Equals(m.Element.Name, componentName, StringComparison.Ordinal));
        return member is null
            ? null
            : TypeResolver.Resolve(libraries.CombinedGraph, member.OwnerId, member.Element.Type, member.OwnerImports);
    }

    private static (double, double) CausalityOffset(ResolvedElement member)
    {
        if (string.Equals(member.Element.Causality, "input", StringComparison.Ordinal)) return (-1, 0);
        if (string.Equals(member.Element.Causality, "output", StringComparison.Ordinal)) return (1, 0);

        var type = member.Element.Type ?? string.Empty;
        if (type.Contains("Input", StringComparison.Ordinal)) return (-1, 0);
        if (type.Contains("Output", StringComparison.Ordinal)) return (1, 0);
        return (0, 0); // acausal / unknown — treat as the component centre
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

    /// <summary>
    /// Every component declared in <paramref name="classCode"/> that has a Placement, by name. The
    /// one reader of a placement from source: get_diagram_layout reports these, the router positions
    /// connections with them and get_diagram_image draws them, and a second regex for the same
    /// annotation is how three answers to one question start (B196).
    ///
    /// <para><b>Only what the class declares itself.</b> Use the overload taking the graph for the
    /// components a class inherits — most blocks in the Modelica Standard Library declare no
    /// connector of their own at all.</para>
    /// </summary>
    public static Dictionary<string, Placement> Placements(string classCode)
        => Placements(classCode, Layer.Diagram);

    /// <summary>
    /// Which of a Placement's two transformations to read.
    ///
    /// <para>A Placement says where a component goes on the enclosing class's <b>diagram</b>, in
    /// <c>transformation</c>, and where it goes on the enclosing class's <b>icon</b>, in
    /// <c>iconTransformation</c>. The second is what puts a connector on the outside of a block, and
    /// <b>where it is absent the first stands in for it</b> — which is how most of the Modelica
    /// Standard Library is written, so reading only the explicit ones finds almost none.</para>
    /// </summary>
    public enum Layer
    {
        /// <summary>Where the component sits on the enclosing class's diagram.</summary>
        Diagram,

        /// <summary>Where it sits on the enclosing class's icon, falling back to the diagram's.</summary>
        Icon,
    }

    /// <summary>Every component declared in the code with a placement on <paramref name="layer"/>.</summary>
    public static Dictionary<string, Placement> Placements(string classCode, Layer layer)
    {
        var result = new Dictionary<string, Placement>(StringComparer.Ordinal);
        var layout = ClassBodyLocator.Analyze(classCode);
        foreach (var c in layout.Components)
        {
            if (c.DeclStart < 0 || c.DeclStop >= classCode.Length || c.DeclStop < c.DeclStart)
                continue;
            if (ParsePlacement(classCode[c.DeclStart..(c.DeclStop + 1)], layer) is { } placement)
                result[c.Name] = placement;
        }
        return result;
    }

    /// <summary>
    /// Every component of the class that has a Placement, <b>including the ones it inherits</b>.
    ///
    /// <para>This is what a diagram is made of. <c>Modelica.Blocks.Continuous.Integrator</c> declares
    /// two optional connectors and gets its <c>u</c> and <c>y</c> from
    /// <c>Interfaces.SISO</c>; drawing only what a class declares itself left the two connectors
    /// every reader looks for off the picture entirely (B196).</para>
    ///
    /// <para><paramref name="classCode"/> is taken as given rather than read from the node, so a
    /// caller part-way through an edit positions against the text it is editing. A declaration in it
    /// shadows an inherited one of the same name, which is what Modelica means by redeclaring.</para>
    /// </summary>
    public static Dictionary<string, Placement> Placements(
        ILibraryDataService libraries, string classId, string classCode, Layer layer = Layer.Diagram)
    {
        var result = Placements(classCode, layer);

        var node = libraries.GetModelById(classId);
        if (node is null)
            return result;

        // One parse per base class rather than one per inherited component: SISO's two connectors
        // would otherwise cost two passes over the same source, and a deep chain many more.
        var byOwner = new Dictionary<string, Dictionary<string, Placement>>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, CoordinateMap>? maps = null;

        // A class's own diagram shows its protected components as well (B315); what it shows on its
        // icon, which its users see, is public only.
        foreach (var member in ClassElementResolver
                     .Collect(libraries.CombinedGraph, node, includeProtected: layer == Layer.Diagram, includeInherited: true)
                     .Where(m => m.Element.Kind == ClassElementKind.Component && m.InheritedFrom is not null))
        {
            if (result.ContainsKey(member.Element.Name))
                continue;

            if (!byOwner.TryGetValue(member.OwnerId, out var owned))
            {
                var ownerCode = libraries.GetModelById(member.OwnerId)?.Definition.ModelicaCode;
                owned = ownerCode is null ? [] : Placements(ownerCode, layer);
                byOwner[member.OwnerId] = owned;
            }

            if (!owned.TryGetValue(member.Element.Name, out var placement))
                continue;

            // Placed where the extends clause's map puts the base, with its graphics (B436): the
            // base wrote this placement in its own coordinates.
            maps ??= BaseMaps(libraries, node, layer);
            result[member.Element.Name] = maps.TryGetValue(member.OwnerId, out var map) && !map.IsIdentity
                ? Mapped(placement, map)
                : placement;
        }

        return result;
    }

    private static Placement Mapped(Placement placement, CoordinateMap map)
    {
        var (extent, rotation, centre) = map.Placement(placement.Extent, placement.Rotation, placement.RotationCentre);
        return new Placement(extent, rotation, centre);
    }

    /// <summary>
    /// Where each of the class's bases, by id, is drawn on <paramref name="layer"/>: the map its
    /// coordinates go through into the class's, composed down the extends chain from each clause's
    /// <c>IconMap</c> / <c>DiagramMap</c> extent (MLS 3.6 §18.6.3). A base with no map is the
    /// identity, and one reached twice keeps the first way it was reached.
    ///
    /// <para><b>The one answer to where a base's contents go</b> (B436). Its components' placements
    /// are mapped with it here, which puts the connectors on an icon and the components on a diagram
    /// where the base's graphics are drawn (B420); the connection router ends lines on those same
    /// components, and the image maps a base's own connect lines with it.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, CoordinateMap> BaseMaps(
        ILibraryDataService libraries, ModelicaGraph.DataTypes.ModelNode node, Layer layer)
    {
        var maps = new Dictionary<string, CoordinateMap>(StringComparer.Ordinal);
        var keyword = layer == Layer.Diagram ? "DiagramMap" : "IconMap";
        Walk(node, CoordinateMap.Identity, 0);
        return maps;

        void Walk(ModelicaGraph.DataTypes.ModelNode current, CoordinateMap toClass, int depth)
        {
            if (depth > DiagramImage.MaxInheritanceDepth)
                return;

            // Reading the annotation parses the class, so only a class that mentions a map is read.
            var own = current.Definition.ModelicaCode?.Contains(keyword, StringComparison.Ordinal) == true
                ? DiagramImage.LayerOf(current, layer)
                : null;

            foreach (var (written, baseNode) in ClassElementResolver.DirectBases(libraries.CombinedGraph, current))
            {
                if (baseNode.Id == node.Id || maps.ContainsKey(baseNode.Id))
                    continue;

                var map = own?.MapFor(written)?.Region is { } region
                          && CoordinateMap.Into(DiagramImage.SystemOf(libraries, baseNode, layer), region) is { } into
                    ? into.Then(toClass)
                    : toClass;
                maps[baseNode.Id] = map;
                Walk(baseNode, map, depth + 1);
            }
        }
    }

    /// <summary>
    /// The <c>transformation(...)</c> of a declaration's Placement, or null when it has none.
    ///
    /// <para><b>The transformation, not the whole annotation.</b> A Placement may also carry an
    /// <c>iconTransformation</c>, which says where the component sits in the enclosing class's
    /// <em>icon</em> — a different question, and searching the declaration for the first
    /// <c>extent=</c> answers whichever one happens to be written first.</para>
    ///
    /// <para><b>The extent is relative to <c>origin</c></b>, which defaults to the centre of the
    /// coordinate system. Reading the extent and ignoring the origin puts every component that uses
    /// one in the middle of the diagram: MSL writes an optional connector as
    /// <c>extent={{-20,-20},{20,20}}, rotation=90, origin={60,-120}</c>, and without the origin that
    /// is a 40x40 box at the centre rather than a connector on the bottom edge.</para>
    /// </summary>
    private static Placement? ParsePlacement(string declaration, Layer layer)
    {
        var transformation =
            (layer == Layer.Icon ? TransformationArguments(declaration, "iconTransformation") : null)
            ?? TransformationArguments(declaration, "transformation")
            ?? declaration;

        var e = ExtentRegex.Match(transformation);
        if (!e.Success)
            return null;

        var extent = new[]
        {
            Num(e.Groups[1].Value), Num(e.Groups[2].Value),
            Num(e.Groups[3].Value), Num(e.Groups[4].Value),
        };

        var rotation = RotationRegex.Match(transformation);
        var origin = OriginRegex.Match(transformation);
        var (ox, oy) = origin.Success
            ? (Num(origin.Groups[1].Value), Num(origin.Groups[2].Value))
            : (0d, 0d);

        double[] absolute = [extent[0] + ox, extent[1] + oy, extent[2] + ox, extent[3] + oy];
        double[] centre = origin.Success ? [ox, oy] : [(absolute[0] + absolute[2]) / 2, (absolute[1] + absolute[3]) / 2];

        return new Placement(absolute, rotation.Success ? Num(rotation.Groups[1].Value) : 0, centre);
    }

    /// <summary>
    /// The text between the parentheses of <c>transformation(</c>, matched by depth so a nested
    /// <c>extent={{..},{..}}</c> cannot end it early. Null when the declaration has no
    /// <c>transformation</c> — deliberately not matching <c>iconTransformation</c>, which ends in the
    /// same characters.
    /// </summary>
    private static string? TransformationArguments(string declaration, string keyword)
    {
        var search = 0;
        while (true)
        {
            var start = declaration.IndexOf(keyword, search, StringComparison.Ordinal);
            if (start < 0)
                return null;
            search = start + 1;

            if (start > 0 && (char.IsLetterOrDigit(declaration[start - 1]) || declaration[start - 1] == '_'))
                continue;   // iconTransformation when transformation was asked for, and the like

            var open = start + keyword.Length;
            while (open < declaration.Length && char.IsWhiteSpace(declaration[open]))
                open++;
            if (open >= declaration.Length || declaration[open] != '(')
                continue;

            var depth = 0;
            for (var i = open; i < declaration.Length; i++)
            {
                if (declaration[i] == '(')
                    depth++;
                else if (declaration[i] == ')' && --depth == 0)
                    return declaration[(open + 1)..i];
            }

            return declaration[(open + 1)..];   // unbalanced; take what there is
        }
    }

    private static string? ComponentTypeText(string classCode, string componentName)
        => ClassBodyLocator.Analyze(classCode).Components
            .FirstOrDefault(c => string.Equals(c.Name, componentName, StringComparison.Ordinal))?.TypeText;

    // --- Small maths -------------------------------------------------------------------------------

    private static string Segment(string portRef, int i) => ModelicaName.Segments(portRef)[i];
    private static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    private static bool Between(double a, double m, double b) => m >= Math.Min(a, b) && m <= Math.Max(a, b);
}

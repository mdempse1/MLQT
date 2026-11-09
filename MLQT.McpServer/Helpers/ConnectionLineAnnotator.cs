using System.Globalization;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;
using Pt = MLQT.McpServer.Helpers.DiagramGeometry.Pt;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Adds and refreshes the graphical <c>Line</c> annotation on <c>connect(a, b)</c> equations so a model
/// whose components have been positioned actually shows its connections on the diagram layer. The line is
/// routed orthogonally between the two connectors' positions (see <see cref="DiagramGeometry"/>).
/// <para><b>Only the connects an edit touches are changed</b>: the one add_connection adds, and those
/// ending on the component set_component_placement moves - whose lines keep their route and have only
/// their end moved (<see cref="FollowMove(ILibraryDataService, string, string, string, string)"/>).
/// Every other line is left exactly as written
/// - a hand-drawn library's, or one an agent has already adjusted from the default route - because
/// re-routing the whole class on each edit threw that work away, colours and thickness included.
/// Re-routing a line that has one replaces only its <c>points</c>; its colour, thickness and anything
/// else it says are kept. An identical route is left as-is to avoid churn. Purely offset-based text
/// splicing so nothing else in the class is disturbed.</para>
/// </summary>
internal static class ConnectionLineAnnotator
{
    /// <summary>
    /// Route the connections <paramref name="touches"/> selects whose endpoints are both positioned,
    /// colouring a new line by its connectors' domain (resolved through the graph). Returns the code
    /// unchanged when nothing selected is routable.
    /// </summary>
    public static string Annotate(
        ILibraryDataService libraries, string classId, string classCode, Func<ConnectClause, bool> touches)
    {
        // One router for the whole class: each component is built once, not once per connection
        // ending on it (B392).
        var router = new DiagramGeometry.Router(libraries, classId, classCode);
        return Annotate(
            classCode,
            router.Route,
            (portA, portB) => ConnectorColor.Resolve(libraries, classId, portA)
                              ?? ConnectorColor.Resolve(libraries, classId, portB),
            touches);
    }

    /// <summary>
    /// Bring the lines of <paramref name="componentName"/>'s connects to where it now is, after
    /// set_component_placement moved it from where <paramref name="oldCode"/> had it: each line that has
    /// points keeps its route and has only its end on the component moved (<see cref="LineReshaper"/>);
    /// one with none is routed. Every other line is left as written.
    /// </summary>
    public static string FollowMove(
        ILibraryDataService libraries, string classId, string oldCode, string newCode, string componentName)
    {
        var router = new DiagramGeometry.Router(libraries, classId, newCode);
        return FollowMove(
            oldCode, newCode, componentName,
            router.Route,
            (portA, portB) => ConnectorColor.Resolve(libraries, classId, portA)
                              ?? ConnectorColor.Resolve(libraries, classId, portB));
    }

    /// <summary>
    /// Core of <see cref="FollowMove(ILibraryDataService, string, string, string, string)"/>.
    /// </summary>
    /// <remarks>
    /// <para>A <b>pure move</b> - same size, same turn - carries each connector by exactly the
    /// component's displacement, so a line's end goes to its old point plus that: no router, so it
    /// keeps the point the author put on the connector, and works for a connector the router cannot
    /// place (it put some of VehicleInterfaces' at the component's centre). A resize or a rotation
    /// moves connectors relative to the component, so there the router says where the end goes, and a
    /// line it cannot place is left alone rather than guessed. A line with both ends on the component
    /// moves whole.</para>
    /// </remarks>
    public static string FollowMove(
        string oldCode, string newCode, string componentName,
        Func<string, string, IReadOnlyList<Pt>?> routeFor,
        Func<string, string, string?> colorFor)
    {
        var before = DiagramGeometry.Placements(oldCode).GetValueOrDefault(componentName);
        var after = DiagramGeometry.Placements(newCode).GetValueOrDefault(componentName);
        var shift = LineReshaper.Translation(before, after);

        var composition = ModelicaParserHelper.Parse(newCode)?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        var edits = new List<(int Start, int Length, string Text)>();

        foreach (var (equation, clause) in ConnectClauses.WithEquations(composition))
        {
            var onA = IsOn(clause.PortA, componentName);
            var onB = IsOn(clause.PortB, componentName);
            if (!onA && !onB)
                continue;

            var existing = equation.comment()?.annotation() is { } annotation ? FindLineArgument(annotation) : null;
            var (kind, drawn) = existing is null ? (PointsKind.None, null) : PointsOf(newCode, existing);

            // A line written some way other than literal numbers - points=pts, an expression - is the
            // author's to change: it is left as it is, never replaced by a default route.
            if (kind == PointsKind.Other)
                continue;

            if (drawn is not { Points.Count: >= 2 })
            {
                // Nothing drawn to keep: route it, as a connect of a newly placed component is - except
                // in a for, if or when, where one route cannot be every index's or branch's line and the
                // tool never adds one (ConnectClause.IsNested).
                if (!clause.IsNested
                    && routeFor(clause.PortA, clause.PortB) is { Count: >= 2 } route
                    && PlanEdit(newCode, equation, route, () => colorFor(clause.PortA, clause.PortB)) is { } routed)
                    edits.Add(routed);
                continue;
            }

            // A nested connect's line is the author's, drawn for every index or branch at once; a pure
            // move carries its end by exactly the move, which is right for all of them, so it follows.
            // Anything needing the router is left alone, as such a line is never routed.
            if (clause.IsNested && shift is null)
                continue;

            var points = drawn.Points;
            IReadOnlyList<Pt>? reshaped;
            if (onA && onB)
            {
                reshaped = shift is { } both ? LineReshaper.Translate(points, both)
                    : routeFor(clause.PortA, clause.PortB) is { Count: >= 2 } whole ? whole : null;
            }
            else
            {
                // Which end of the line is the moved component's: the one nearer where it was. A Line
                // need not run from the connect's first port to its second; where the box cannot say,
                // the connect's order does.
                var atStart = onA;
                if (before is not null)
                {
                    var (start, end) = (LineReshaper.DistanceTo(points[0], before), LineReshaper.DistanceTo(points[^1], before));
                    if (start != end)
                        atStart = start < end;
                }

                var from = atStart ? points[0] : points[^1];
                Pt? to = shift is { } by
                    ? LineReshaper.Translate([from], by)[0]
                    : routeFor(clause.PortA, clause.PortB) is { Count: >= 2 } route ? (onA ? route[0] : route[^1]) : null;
                reshaped = to is { } target ? LineReshaper.MoveEnd(points, atStart, target) : null;
            }

            if (reshaped is not null)
                edits.AddRange(PointEdits(newCode, drawn, reshaped));
        }

        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
            newCode = newCode[..start] + text + newCode[(start + length)..];
        return newCode;
    }

    private enum PointsKind
    {
        /// <summary>The Line has no <c>points</c>: there is nothing drawn to keep.</summary>
        None,

        /// <summary>Literal number pairs, read into <see cref="DrawnPoints"/>.</summary>
        Literal,

        /// <summary>Anything else - <c>points=pts</c>, an expression - which is the author's and left alone.</summary>
        Other,
    }

    /// <summary>One point of a Line as written: its value, where its <c>{x,y}</c> is, and its two numbers' text.</summary>
    private sealed record DrawnPoint(Pt Value, int Start, int Stop, string X, string Y);

    /// <summary>A Line's literal points, and the text between its first two, which is how it separates them.</summary>
    private sealed record DrawnPoints(IReadOnlyList<DrawnPoint> Drawn, string Separator)
    {
        public IReadOnlyList<Pt> Points { get; } = [.. Drawn.Select(d => d.Value)];
    }

    /// <summary>A Line's points, if they are literal numbers.</summary>
    private static (PointsKind Kind, DrawnPoints? Drawn) PointsOf(string code, modelicaParser.ArgumentContext line)
    {
        var lineMod = line.element_modification_or_replaceable()?.element_modification()
            ?.modification()?.class_modification();
        if (lineMod is null)
            return (PointsKind.Other, null); // `Line` written as something other than Line(...)

        foreach (var arg in lineMod.argument_list()?.argument() ?? [])
        {
            var mod = arg.element_modification_or_replaceable()?.element_modification();
            if (!string.Equals(mod?.name()?.GetText(), "points", StringComparison.Ordinal))
                continue;
            if (mod!.modification() is not { } value)
                return (PointsKind.Other, null);

            var start = value.Start.StartIndex;
            var text = code[start..(value.Stop.StopIndex + 1)];
            var pairs = PointPair.Matches(text);

            // Literal only when the expression is pairs of numbers and nothing else: what is left once
            // the pairs are taken out must be the braces and commas around them.
            if (pairs.Count == 0 || !OnlyPunctuation.IsMatch(PointPair.Replace(text, string.Empty)))
                return (PointsKind.Other, null);

            var drawn = pairs.Select(m => new DrawnPoint(
                new Pt(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                       double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)),
                start + m.Index, start + m.Index + m.Length, m.Groups[1].Value, m.Groups[2].Value)).ToList();
            var separator = drawn.Count >= 2 ? code[drawn[0].Stop..drawn[1].Start] : ",";
            return (PointsKind.Literal, new DrawnPoints(drawn, separator));
        }

        return (PointsKind.None, null);
    }

    // A Modelica number: 10, 10., .5, 1.5e-3, signed or not.
    private const string Number = @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?";

    private static readonly System.Text.RegularExpressions.Regex PointPair = new(
        $@"\{{\s*({Number})\s*,\s*({Number})\s*\}}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex OnlyPunctuation = new(
        @"^\s*=?[\s{},]*$", System.Text.RegularExpressions.RegexOptions.Compiled); // the modification starts at its `=`

    /// <summary>
    /// The edits that turn a Line's points into <paramref name="reshaped"/>, touching only what changed:
    /// a point that did not move keeps its text exactly (<c>-40.0</c> stays <c>-40.0</c>), a moved
    /// point keeps the text of a coordinate that did not change, and points a dogleg adds go in between
    /// the ones around them, written with the line's own separator. Nothing changed, no edit.
    /// </summary>
    private static IEnumerable<(int Start, int Length, string Text)> PointEdits(
        string code, DrawnPoints line, IReadOnlyList<Pt> reshaped)
    {
        var drawn = line.Drawn;
        var old = line.Points;

        // The runs at each end that did not change; what lies between them is what is rewritten.
        var front = 0;
        while (front < old.Count && front < reshaped.Count && old[front] == reshaped[front])
            front++;
        var back = 0;
        while (back < old.Count - front && back < reshaped.Count - front
               && old[old.Count - 1 - back] == reshaped[reshaped.Count - 1 - back])
            back++;

        if (front == old.Count && old.Count == reshaped.Count)
            yield break; // the same line

        // As many points as before: each changed one is replaced where it stands.
        if (old.Count == reshaped.Count)
        {
            for (var i = front; i < old.Count - back; i++)
                if (old[i] != reshaped[i])
                    yield return (drawn[i].Start, drawn[i].Stop - drawn[i].Start, Write(reshaped[i], drawn[i]));
            yield break;
        }

        // A different number: the changed middle is written again, between the runs that were kept.
        var middle = Enumerable.Range(front, reshaped.Count - back - front)
            .Select(i => Write(reshaped[i], i < old.Count ? drawn[i] : null));
        var text = string.Join(line.Separator, middle);

        if (old.Count - back > front)
        {
            // Some old points in the middle: replace them, first to last.
            var (first, last) = (drawn[front], drawn[old.Count - back - 1]);
            yield return (first.Start, last.Stop - first.Start, text);
        }
        else if (text.Length > 0)
        {
            // Only new points: after the last of the front run, or before the first point if none is.
            yield return front > 0
                ? (drawn[front - 1].Stop, 0, line.Separator + text)
                : (drawn[0].Start, 0, text + line.Separator);
        }
    }

    // A point as written: a coordinate equal to the one it replaces keeps that one's text.
    private static string Write(Pt p, DrawnPoint? was) =>
        $"{{{(was is not null && was.Value.X == p.X ? was.X : K(p.X))},{(was is not null && was.Value.Y == p.Y ? was.Y : K(p.Y))}}}";

    private static string K(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// The connect joining <paramref name="portA"/> and <paramref name="portB"/>, either way round. White
    /// space is not compared: a clause's ports are read with <c>GetText()</c>, which drops it, so
    /// <c>g[1, 2].p</c> as the caller wrote it is <c>g[1,2].p</c> in the clause, and the connect just
    /// added was not found and got no line.
    /// </summary>
    public static Func<ConnectClause, bool> Joining(string portA, string portB)
    {
        var (a, b) = (Compact(portA), Compact(portB));
        return c =>
        {
            var (ca, cb) = (Compact(c.PortA), Compact(c.PortB));
            return (ca == a && cb == b) || (ca == b && cb == a);
        };
    }

    private static string Compact(string port) => string.Concat(port.Where(ch => !char.IsWhiteSpace(ch)));

    /// <summary>Every connect with an end on <paramref name="componentName"/>: one of its connectors,
    /// an element of it (<c>g[1].p</c>), or the component itself when it is a connector.</summary>
    public static Func<ConnectClause, bool> EndingOn(string componentName) =>
        c => IsOn(c.PortA, componentName) || IsOn(c.PortB, componentName);

    private static bool IsOn(string port, string componentName)
    {
        var head = ModelicaName.Segments(port).FirstOrDefault() ?? string.Empty;
        if (head.IndexOf('[') is var at and >= 0 && !head.StartsWith('\''))
            head = head[..at];
        return string.Equals(head.Trim(), componentName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Core routine. <paramref name="routeFor"/> returns the poly-line for a connection or null to skip it;
    /// <paramref name="colorFor"/> returns a Modelica colour literal (e.g. "{0,0,127}") or null;
    /// <paramref name="touches"/> picks the connects to route, every other one being left as written
    /// (null routes every one).
    /// </summary>
    public static string Annotate(
        string classCode,
        Func<string, string, IReadOnlyList<Pt>?> routeFor,
        Func<string, string, string?> colorFor,
        Func<ConnectClause, bool>? touches = null)
    {
        var composition = ModelicaParserHelper.Parse(classCode)?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();

        var edits = new List<(int Start, int Length, string Text)>();

        foreach (var (equation, clause) in ConnectClauses.WithEquations(composition))
        {
            // A nested connect is the user's, not the tool's: one Line in a loop body cannot be each
            // index's wiring, and one in an if branch is drawn whether or not the branch applies.
            if (clause.IsNested)
                continue;
            if (touches is not null && !touches(clause))
                continue;
            if (routeFor(clause.PortA, clause.PortB) is not { Count: >= 2 } route)
                continue;

            if (PlanEdit(classCode, equation, route, () => colorFor(clause.PortA, clause.PortB)) is { } edit)
                edits.Add(edit);
        }

        // Apply right-to-left so earlier offsets stay valid.
        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
            classCode = classCode[..start] + text + classCode[(start + length)..];
        return classCode;
    }

    private static (int Start, int Length, string Text)? PlanEdit(
        string code, modelicaParser.EquationContext equation, IReadOnlyList<Pt> route, Func<string?> color)
    {
        var annotation = equation.comment()?.annotation();
        if (annotation is null)
        {
            // No annotation yet: append one after the connect clause (before the terminating ';').
            var after = equation.Stop.StopIndex + 1;
            return (after, 0, $" annotation ({BuildLine(route, color())})");
        }

        var classMod = annotation.class_modification();
        if (classMod is null)
            return null;

        if (FindLineArgument(annotation) is { } existing)
            return PlanPoints(code, existing, Points(route));

        // Annotation present but no Line: add one as an argument.
        var line = BuildLine(route, color());
        if (classMod.argument_list() is { } list && list.argument().Length > 0)
            return (classMod.Stop.StartIndex, 0, $", {line}"); // before the closing ')'
        return (classMod.Start.StartIndex + 1, 0, line);       // empty 'annotation ()'
    }

    /// <summary>
    /// Re-route a Line that exists by replacing its <c>points</c> alone: its colour, thickness, arrows
    /// and smoothing are the author's, and rebuilding the whole Line dropped them.
    /// </summary>
    private static (int Start, int Length, string Text)? PlanPoints(
        string code, modelicaParser.ArgumentContext line, string points)
    {
        var lineMod = line.element_modification_or_replaceable()?.element_modification()
            ?.modification()?.class_modification();
        if (lineMod is null)
            return null; // `Line` written some way that is not Line(...): not ours to rewrite

        foreach (var arg in lineMod.argument_list()?.argument() ?? [])
        {
            var mod = arg.element_modification_or_replaceable()?.element_modification();
            if (!string.Equals(mod?.name()?.GetText(), "points", StringComparison.Ordinal))
                continue;
            var span = code[mod!.Start.StartIndex..(mod.Stop.StopIndex + 1)];
            if (string.Equals(span, points, StringComparison.Ordinal))
                return null; // already the current route — leave it, no churn
            return (mod.Start.StartIndex, mod.Stop.StopIndex - mod.Start.StartIndex + 1, points);
        }

        // A Line with no points: give it them first.
        return lineMod.argument_list() is { } args && args.argument().Length > 0
            ? (lineMod.Start.StartIndex + 1, 0, points + ", ")
            : (lineMod.Start.StartIndex + 1, 0, points);
    }

    private static modelicaParser.ArgumentContext? FindLineArgument(modelicaParser.AnnotationContext annotation)
    {
        var list = annotation.class_modification()?.argument_list();
        if (list is null)
            return null;
        foreach (var arg in list.argument())
        {
            var name = arg.element_modification_or_replaceable()?.element_modification()?.name()?.GetText();
            if (string.Equals(name, "Line", StringComparison.Ordinal))
                return arg;
        }
        return null;
    }

    private static string BuildLine(IReadOnlyList<Pt> route, string? color)
    {
        var points = Points(route);
        return color is null ? $"Line({points})" : $"Line({points}, color={color})";
    }

    private static string Points(IReadOnlyList<Pt> route) =>
        $"points={{{string.Join(",", route.Select(p => $"{{{R(p.X)},{R(p.Y)}}}"))}}}";

    private static string R(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
}

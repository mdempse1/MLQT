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
/// <para><b>Only the connects an edit touches are routed</b>: the one add_connection adds, and those
/// ending on the component set_component_placement moves. Every other line is left exactly as written
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

    /// <summary>The connect joining <paramref name="portA"/> and <paramref name="portB"/>, either way round.</summary>
    public static Func<ConnectClause, bool> Joining(string portA, string portB) =>
        c => (c.PortA == portA && c.PortB == portB) || (c.PortA == portB && c.PortB == portA);

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

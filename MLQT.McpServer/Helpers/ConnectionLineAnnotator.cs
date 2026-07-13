using System.Globalization;
using System.Text.RegularExpressions;
using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Adds and refreshes the graphical <c>Line</c> annotation on <c>connect(a, b)</c> equations so a model
/// whose components have been positioned actually shows its connections on the diagram layer. A straight
/// line is routed between the two components' placement centres. Only connects whose BOTH endpoints
/// resolve to a component with a <c>Placement</c> are touched; an existing simple two-point line is
/// refreshed (so lines track a moved component), but a hand-routed multi-point line is left alone.
/// Purely offset-based text splicing so nothing else in the class is disturbed.
/// </summary>
internal static class ConnectionLineAnnotator
{
    private static readonly Regex ExtentRegex = new(
        @"extent\s*=\s*\{\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}\s*,\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}",
        RegexOptions.Compiled);

    // Counts coordinate pairs {x,y}; a 3-tuple colour like {0,0,127} does not match (it has three numbers).
    private static readonly Regex PointRegex = new(
        @"\{\s*-?\d+(?:\.\d+)?\s*,\s*-?\d+(?:\.\d+)?\s*\}", RegexOptions.Compiled);

    /// <summary>
    /// Convenience overload: annotate every eligible connection in <paramref name="classCode"/>, colouring
    /// each line by its connectors' domain (resolved through the graph). Returns the code unchanged when no
    /// component is positioned.
    /// </summary>
    public static string Annotate(ILibraryDataService libraries, string classId, string classCode)
    {
        var centres = PlacementCentres(classCode);
        if (centres.Count == 0)
            return classCode;
        return Annotate(classCode, centres,
            (portA, portB) => ConnectorColor.Resolve(libraries, classId, portA)
                              ?? ConnectorColor.Resolve(libraries, classId, portB));
    }

    /// <summary>The centre point of each component that carries a Placement, keyed by component name.</summary>
    public static Dictionary<string, (double X, double Y)> PlacementCentres(string classCode)
    {
        var centres = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var layout = ClassBodyLocator.Analyze(classCode);
        foreach (var c in layout.Components)
        {
            if (c.DeclStart < 0 || c.DeclStop >= classCode.Length || c.DeclStop < c.DeclStart)
                continue;
            var slice = classCode[c.DeclStart..(c.DeclStop + 1)];
            var m = ExtentRegex.Match(slice);
            if (!m.Success)
                continue;
            var x1 = Num(m.Groups[1].Value); var y1 = Num(m.Groups[2].Value);
            var x2 = Num(m.Groups[3].Value); var y2 = Num(m.Groups[4].Value);
            centres[c.Name] = ((x1 + x2) / 2, (y1 + y2) / 2);
        }
        return centres;
    }

    /// <summary>
    /// Return <paramref name="classCode"/> with connection lines added/refreshed. <paramref name="colorFor"/>
    /// maps a pair of ports to a Modelica colour literal (e.g. "{0,0,127}") or null for the default colour.
    /// </summary>
    public static string Annotate(
        string classCode,
        IReadOnlyDictionary<string, (double X, double Y)> centres,
        Func<string, string, string?> colorFor)
    {
        var composition = ModelicaParserHelper.Parse(classCode)?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.children is null)
            return classCode;

        var edits = new List<(int Start, int Length, string Text)>();

        foreach (var section in composition.children.OfType<modelicaParser.Equation_sectionContext>())
        foreach (var eoc in section.equation_or_comment())
        {
            var equation = eoc.equation();
            if (equation?.connect_clause() is not { } connect)
                continue;
            var refs = connect.component_reference();
            if (refs.Length < 2)
                continue;

            var portA = refs[0].GetText();
            var portB = refs[1].GetText();
            if (!centres.TryGetValue(Root(portA), out var a) || !centres.TryGetValue(Root(portB), out var b))
                continue;

            var line = BuildLine(a, b, colorFor(portA, portB));
            if (PlanEdit(classCode, equation, connect, line) is { } edit)
                edits.Add(edit);
        }

        // Apply right-to-left so earlier offsets stay valid.
        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
            classCode = classCode[..start] + text + classCode[(start + length)..];
        return classCode;
    }

    private static (int Start, int Length, string Text)? PlanEdit(
        string code, modelicaParser.EquationContext equation, modelicaParser.Connect_clauseContext connect, string line)
    {
        var annotation = equation.comment()?.annotation();
        if (annotation is null)
        {
            // No annotation yet: append one after the connect clause (before the terminating ';').
            var after = equation.Stop.StopIndex + 1;
            return (after, 0, $" annotation ({line})");
        }

        var classMod = annotation.class_modification();
        if (classMod is null)
            return null;

        var existingLine = FindLineArgument(annotation);
        if (existingLine is not null)
        {
            var span = code[existingLine.Start.StartIndex..(existingLine.Stop.StopIndex + 1)];
            if (PointRegex.Matches(span).Count != 2)
                return null; // hand-routed (multi-point) line — leave it untouched
            return (existingLine.Start.StartIndex, existingLine.Stop.StopIndex - existingLine.Start.StartIndex + 1, line);
        }

        // Annotation present but no Line: add one as an argument.
        if (classMod.argument_list() is { } list && list.argument().Length > 0)
            return (classMod.Stop.StartIndex, 0, $", {line}"); // before the closing ')'
        return (classMod.Start.StartIndex + 1, 0, line);       // empty 'annotation ()'
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

    private static string BuildLine((double X, double Y) a, (double X, double Y) b, string? color)
    {
        var points = $"points={{{{{R(a.X)},{R(a.Y)}}},{{{R(b.X)},{R(b.Y)}}}}}";
        return color is null ? $"Line({points})" : $"Line({points}, color={color})";
    }

    private static string Root(string portRef)
    {
        var dot = portRef.IndexOf('.');
        return dot < 0 ? portRef : portRef[..dot];
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private static string R(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
}

using System.Globalization;
using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;
using Pt = MLQT.McpServer.Helpers.DiagramGeometry.Pt;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Adds and refreshes the graphical <c>Line</c> annotation on <c>connect(a, b)</c> equations so a model
/// whose components have been positioned actually shows its connections on the diagram layer. The line is
/// routed orthogonally between the two connectors' positions (see <see cref="DiagramGeometry"/>). The line
/// is tool-managed: it is (re)generated to the current route whenever a component is connected or moved, so
/// it always tracks the components; an identical line is left as-is to avoid churn. Purely offset-based
/// text splicing so nothing else in the class is disturbed.
/// </summary>
internal static class ConnectionLineAnnotator
{
    /// <summary>
    /// Annotate every connection whose endpoints are both positioned, colouring each line by its connectors'
    /// domain (resolved through the graph). Returns the code unchanged when nothing is routable.
    /// </summary>
    public static string Annotate(ILibraryDataService libraries, string classId, string classCode)
        => Annotate(
            classCode,
            (portA, portB) => DiagramGeometry.RouteConnection(libraries, classId, classCode, portA, portB),
            (portA, portB) => ConnectorColor.Resolve(libraries, classId, portA)
                              ?? ConnectorColor.Resolve(libraries, classId, portB));

    /// <summary>
    /// Core routine. <paramref name="routeFor"/> returns the poly-line for a connection or null to skip it;
    /// <paramref name="colorFor"/> returns a Modelica colour literal (e.g. "{0,0,127}") or null.
    /// </summary>
    public static string Annotate(
        string classCode,
        Func<string, string, IReadOnlyList<Pt>?> routeFor,
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
            if (routeFor(portA, portB) is not { Count: >= 2 } route)
                continue;

            var line = BuildLine(route, colorFor(portA, portB));
            if (PlanEdit(classCode, equation, line) is { } edit)
                edits.Add(edit);
        }

        // Apply right-to-left so earlier offsets stay valid.
        foreach (var (start, length, text) in edits.OrderByDescending(e => e.Start))
            classCode = classCode[..start] + text + classCode[(start + length)..];
        return classCode;
    }

    private static (int Start, int Length, string Text)? PlanEdit(
        string code, modelicaParser.EquationContext equation, string line)
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

        if (FindLineArgument(annotation) is { } existing)
        {
            var span = code[existing.Start.StartIndex..(existing.Stop.StopIndex + 1)];
            if (string.Equals(span, line, StringComparison.Ordinal))
                return null; // already the current route — leave it, no churn
            return (existing.Start.StartIndex, existing.Stop.StopIndex - existing.Start.StartIndex + 1, line);
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

    private static string BuildLine(IReadOnlyList<Pt> route, string? color)
    {
        var pts = string.Join(",", route.Select(p => $"{{{R(p.X)},{R(p.Y)}}}"));
        var points = $"points={{{pts}}}";
        return color is null ? $"Line({points})" : $"Line({points}, color={color})";
    }

    private static string R(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
}

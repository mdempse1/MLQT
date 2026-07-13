using Antlr4.Runtime.Tree;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Analyses a single class's source to find the spans a surgical edit needs: where to append a public
/// element, an equation or a statement, where the class closes, and the spans of its components and
/// connections. Only the outermost class is examined (nested classes have their own bodies). Purely
/// syntactic and offset-based, so callers can splice text precisely.
/// </summary>
public static class ClassBodyLocator
{
    public static ClassBodyLayout Analyze(string classCode)
    {
        var cls = ModelicaParserHelper.Parse(classCode)?.class_definition()?.FirstOrDefault();
        var longSpec = cls?.class_specifier()?.long_class_specifier();
        var composition = longSpec?.composition();
        if (composition?.children is null)
            return ClassBodyLayout.NotFound;

        var bodyEnd = FindEndOffset(longSpec!) ?? (composition.Stop?.StopIndex + 1 ?? classCode.Length);
        var indent = "  ";

        int? publicListEnd = null;
        int? firstPublicElement = null;
        int? protectedListEnd = null;
        int? lastEquationEnd = null;
        int? lastAlgorithmEnd = null;
        int? trailingAnnotationStart = null;
        var components = new List<ClassBodyComponent>();
        var connections = new List<ClassBodyConnection>();

        var sawFirstList = false;
        var currentPublic = true;
        foreach (var child in composition.children)
        {
            switch (child)
            {
                case ITerminalNode t when t.GetText() == "public":
                    currentPublic = true;
                    break;
                case ITerminalNode t when t.GetText() == "protected":
                    currentPublic = false;
                    break;

                case modelicaParser.Element_listContext list:
                    var isPublic = !sawFirstList || currentPublic; // the first element_list is public
                    sawFirstList = true;
                    foreach (var element in list.element())
                        CaptureComponent(element, components, ref indent);
                    if (isPublic && list.element().Length > 0 && list.Stop is not null)
                    {
                        publicListEnd = list.Stop.StopIndex + 1; // after the last element's ';'
                        firstPublicElement ??= list.element()[0].Start.StartIndex;
                    }
                    else if (!isPublic && list.element().Length > 0 && list.Stop is not null)
                    {
                        protectedListEnd = list.Stop.StopIndex + 1; // append target for a protected element
                    }
                    break;

                case modelicaParser.Equation_sectionContext eq:
                    if (eq.Stop is not null)
                        lastEquationEnd = eq.Stop.StopIndex + 1;
                    CollectConnections(eq, connections);
                    break;

                case modelicaParser.Algorithm_sectionContext alg:
                    if (alg.Stop is not null)
                        lastAlgorithmEnd = alg.Stop.StopIndex + 1;
                    break;

                // The trailing class annotation is a direct child of composition after the first
                // element_list; a leading annotation appears before it (and the external-clause
                // annotation is nested, not a direct child).
                case modelicaParser.AnnotationContext ann when sawFirstList:
                    trailingAnnotationStart = ann.Start.StartIndex;
                    break;
            }
        }

        // New elements and new equation/algorithm sections must be inserted before the class annotation,
        // which the grammar requires to be the last thing in the composition. When one is present, the
        // insertion boundary is its start, not the 'end' keyword (inserting after it would not parse).
        var insertBoundary = trailingAnnotationStart is int annStart && annStart < bodyEnd ? annStart : bodyEnd;

        return new ClassBodyLayout(
            Found: true,
            PublicAppendOffset: publicListEnd ?? insertBoundary,
            FirstPublicElementOffset: firstPublicElement,
            ProtectedAppendOffset: protectedListEnd,
            EquationAppendOffset: lastEquationEnd,
            AlgorithmAppendOffset: lastAlgorithmEnd,
            BodyEndOffset: insertBoundary,
            Indent: indent,
            Components: components,
            Connections: connections);
    }

    private static void CaptureComponent(
        modelicaParser.ElementContext element, List<ClassBodyComponent> components, ref string indent)
    {
        var cc = element.component_clause();
        var list = cc?.component_list();
        if (cc is null || list is null)
            return;

        // Detect indentation from the first captured component's column.
        if (components.Count == 0 && element.Start.Column > 0)
            indent = new string(' ', element.Start.Column);

        var type = cc.type_specifier()?.GetText()?.Trim() ?? string.Empty;
        var decls = list.component_declaration();
        var sole = decls.Length == 1;

        foreach (var decl in decls)
        {
            var declaration = decl.declaration();
            var name = declaration?.IDENT()?.GetText();
            if (declaration is null || string.IsNullOrEmpty(name))
                continue;

            int? modStart = null, modStop = null;
            if (declaration.modification() is { } mod)
            {
                modStart = mod.Start.StartIndex;
                modStop = mod.Stop.StopIndex;
            }

            components.Add(new ClassBodyComponent(
                name, type,
                decl.Start.StartIndex, decl.Stop.StopIndex,
                cc.Start.StartIndex, cc.Stop.StopIndex,
                sole, modStart, modStop,
                BindingInsertOffset: declaration.Stop.StopIndex + 1));
        }
    }

    private static void CollectConnections(IParseTree node, List<ClassBodyConnection> connections)
    {
        if (node is modelicaParser.Connect_clauseContext connect)
        {
            var refs = connect.component_reference();
            if (refs.Length >= 2)
                connections.Add(new ClassBodyConnection(
                    refs[0].GetText(), refs[1].GetText(),
                    connect.Start.StartIndex, connect.Stop.StopIndex));
            return;
        }

        for (var i = 0; i < node.ChildCount; i++)
            CollectConnections(node.GetChild(i), connections);
    }

    private static int? FindEndOffset(modelicaParser.Long_class_specifierContext longSpec)
    {
        for (var i = 0; i < longSpec.ChildCount; i++)
            if (longSpec.GetChild(i) is ITerminalNode t && t.GetText() == "end")
                return t.Symbol.StartIndex;
        return null;
    }
}

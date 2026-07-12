using ModelicaParser.Helpers;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Small shared parse-navigation helpers over a single class's source: locate the outermost class, its
/// long specifier, a component declaration by name, and quote a Modelica string. Used by the tools that
/// surgically edit descriptions, documentation and placements.
/// </summary>
internal static class ModelicaNav
{
    public static modelicaParser.Class_definitionContext? OuterClass(string classCode)
        => ModelicaParserHelper.Parse(classCode)?.class_definition()?.FirstOrDefault();

    public static modelicaParser.Long_class_specifierContext? LongSpec(string classCode)
        => OuterClass(classCode)?.class_specifier()?.long_class_specifier();

    /// <summary>Find a component declaration by name in the outermost class, or null.</summary>
    public static modelicaParser.Component_declarationContext? FindComponent(string classCode, string name)
    {
        var composition = LongSpec(classCode)?.composition();
        if (composition?.element_list() is not { } lists)
            return null;

        foreach (var list in lists)
            foreach (var element in list.element())
            {
                var componentList = element.component_clause()?.component_list();
                if (componentList is null)
                    continue;
                foreach (var decl in componentList.component_declaration())
                    if (decl.declaration()?.IDENT()?.GetText() == name)
                        return decl;
            }
        return null;
    }

    /// <summary>Quote a plain string as a Modelica string literal (escaping backslashes and quotes).</summary>
    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}

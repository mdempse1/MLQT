using Antlr4.Runtime.Tree;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Extracts the structural interface (elements) of a single Modelica class from its parse tree. Only
/// the outermost class is walked; nested classes are listed as <see cref="ClassElementKind.Class"/>
/// elements but not recursed into. The extraction is purely syntactic — it reports declared type text
/// and prefixes and does no cross-class resolution (whether a type is a connector, whether an extends
/// resolves, etc. is layered on top by callers that have the dependency graph).
/// </summary>
public static class ClassInterfaceExtractor
{
    /// <summary>Extract the interface of the first (outermost) class in a parsed stored_definition.</summary>
    public static ClassInterface Extract(modelicaParser.Stored_definitionContext? stored)
    {
        var cls = stored?.class_definition()?.FirstOrDefault();
        return cls is null ? new ClassInterface() : ExtractFromClass(cls);
    }

    /// <summary>Parse <paramref name="modelicaCode"/> and extract the first class's interface.</summary>
    public static ClassInterface ExtractFromCode(string modelicaCode)
        => Extract(ModelicaParserHelper.Parse(modelicaCode));

    /// <summary>Extract the interface of a specific class definition context.</summary>
    public static ClassInterface ExtractFromClass(modelicaParser.Class_definitionContext cls)
    {
        var elements = new List<ClassElement>();
        var longSpec = cls.class_specifier()?.long_class_specifier();
        string? description = null;

        if (longSpec is not null)
        {
            description = ReadStringComment(longSpec.string_comment());
            if (longSpec.composition() is { } composition)
                CollectComposition(composition, elements);
        }

        return new ClassInterface { Description = description, Elements = elements };
    }

    private static void CollectComposition(modelicaParser.CompositionContext composition, List<ClassElement> elements)
    {
        // The first element_list is implicitly public; subsequent ones are introduced by a
        // 'public'/'protected' keyword. Walk children in order so each list is tagged with its section.
        if (composition.children is null)
            return;

        var isPublic = true;
        foreach (var child in composition.children)
        {
            switch (child)
            {
                case ITerminalNode t when t.GetText() == "public":
                    isPublic = true;
                    break;
                case ITerminalNode t when t.GetText() == "protected":
                    isPublic = false;
                    break;
                case modelicaParser.Element_listContext list:
                    CollectElementList(list, isPublic, elements);
                    break;
            }
        }
    }

    // Walk an element_list's children (element_list : (c_comment | element ';')*), so // and /* */
    // comments are captured and attached to the element they precede (as the renderer treats them).
    private static void CollectElementList(modelicaParser.Element_listContext list, bool isPublic, List<ClassElement> elements)
    {
        if (list.children is null)
            return;

        List<string>? pending = null;
        foreach (var child in list.children)
        {
            switch (child)
            {
                case modelicaParser.C_commentContext comment:
                    (pending ??= new List<string>()).Add(comment.GetText().Trim());
                    break;

                case modelicaParser.ElementContext element:
                    var before = elements.Count;
                    CollectElement(element, isPublic, elements);
                    if (pending is not null && elements.Count > before)
                    {
                        elements[before] = elements[before] with { LeadingComments = pending };
                        pending = null;
                    }
                    break;
            }
        }
    }

    private static void CollectElement(modelicaParser.ElementContext element, bool isPublic, List<ClassElement> elements)
    {
        var prefixes = ReadElementPrefixes(element);

        if (element.import_clause() is { } import)
        {
            elements.Add(new ClassElement
            {
                Kind = ClassElementKind.Import,
                Name = ReadImport(import),
                IsPublic = isPublic,
                Prefixes = prefixes,
                Line = element.Start.Line
            });
        }
        else if (element.extends_clause() is { } ext)
        {
            var baseType = ext.type_specifier()?.GetText()?.Trim() ?? string.Empty;
            elements.Add(new ClassElement
            {
                Kind = ClassElementKind.Extends,
                Name = baseType,
                Type = baseType,
                Modifications = ExtractExtendsModifications(ext),
                IsPublic = isPublic,
                Prefixes = prefixes,
                Line = element.Start.Line
            });
        }
        else if (element.class_definition() is { } nested)
        {
            var spec = nested.class_specifier();
            elements.Add(new ClassElement
            {
                Kind = ClassElementKind.Class,
                Name = ClassName(spec),
                ClassType = GetClassType(nested.class_prefixes()),
                Description = ReadStringComment(spec?.long_class_specifier()?.string_comment()),
                IsPublic = isPublic,
                Prefixes = prefixes,
                Line = element.Start.Line
            });
        }
        else if (element.component_clause() is { } componentClause)
        {
            CollectComponents(componentClause, isPublic, prefixes, elements);
        }
    }

    private static void CollectComponents(
        modelicaParser.Component_clauseContext cc, bool isPublic, IReadOnlyList<string> prefixes, List<ClassElement> elements)
    {
        var (variability, causality, connection) = ReadTypePrefix(cc.type_prefix());
        var type = cc.type_specifier()?.GetText()?.Trim();
        var list = cc.component_list();
        if (list is null)
            return;

        // One component_clause can declare several comma-separated components sharing the same type/prefix.
        foreach (var decl in list.component_declaration())
        {
            var declaration = decl.declaration();
            var name = declaration?.IDENT()?.GetText();
            if (string.IsNullOrEmpty(name))
                continue;

            elements.Add(new ClassElement
            {
                Kind = ClassElementKind.Component,
                Name = name,
                Type = type,
                Variability = variability,
                Causality = causality,
                Connection = connection,
                DefaultValue = ReadModification(declaration!.modification()),
                Description = ReadStringComment(decl.comment()?.string_comment()),
                IsPublic = isPublic,
                Prefixes = prefixes,
                Line = decl.Start.Line
            });
        }
    }

    // The scalar modifications on an extends clause: extends Base(k = 5, T = 2) -> {k:5, T:2}. Nested
    // modifications (e.g. sub(x = 5)) and redeclarations are not scalar defaults and are omitted.
    private static IReadOnlyDictionary<string, string>? ExtractExtendsModifications(
        modelicaParser.Extends_clauseContext ext)
    {
        var list = ext.class_or_inheritence_modification()?.argument_or_inheritence_list();
        if (list is null)
            return null;

        Dictionary<string, string>? mods = null;
        foreach (var arg in list.argument())
        {
            var em = arg.element_modification_or_replaceable()?.element_modification();
            var name = em?.name()?.GetText();
            if (string.IsNullOrEmpty(name))
                continue;
            var value = ScalarModificationValue(em!.modification());
            if (value is null)
                continue;
            (mods ??= new Dictionary<string, string>(StringComparer.Ordinal))[name] = value;
        }
        return mods;
    }

    private static string? ScalarModificationValue(modelicaParser.ModificationContext? mod)
    {
        if (mod is null)
            return null;
        var text = mod.GetText().Trim();
        if (text.StartsWith("(", StringComparison.Ordinal)) // class_modification, not a scalar binding
            return null;
        if (text.StartsWith(":=", StringComparison.Ordinal))
            text = text[2..].Trim();
        else if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].Trim();
        else
            return null;
        return text.Length == 0 ? null : text;
    }

    private static (string? variability, string? causality, string? connection) ReadTypePrefix(
        modelicaParser.Type_prefixContext? tp)
    {
        var text = tp?.GetText();
        if (string.IsNullOrEmpty(text))
            return (null, null, null);

        string? variability = text.Contains("discrete") ? "discrete"
            : text.Contains("parameter") ? "parameter"
            : text.Contains("constant") ? "constant"
            : null;
        string? causality = text.Contains("input") ? "input"
            : text.Contains("output") ? "output"
            : null;
        string? connection = text.Contains("flow") ? "flow"
            : text.Contains("stream") ? "stream"
            : null;
        return (variability, causality, connection);
    }

    private static string? ReadModification(modelicaParser.ModificationContext? mod)
    {
        if (mod is null)
            return null;
        var text = mod.GetText().Trim();
        if (text.StartsWith(":=", StringComparison.Ordinal))
            text = text[2..].Trim();
        else if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].Trim();
        return text.Length == 0 ? null : text;
    }

    private static IReadOnlyList<string> ReadElementPrefixes(modelicaParser.ElementContext element)
    {
        List<string>? prefixes = null;
        for (var i = 0; i < element.ChildCount; i++)
        {
            if (element.GetChild(i) is ITerminalNode t &&
                t.GetText() is "replaceable" or "redeclare" or "final" or "inner" or "outer")
            {
                (prefixes ??= new List<string>()).Add(t.GetText());
            }
        }
        return prefixes ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private static string ReadImport(modelicaParser.Import_clauseContext import)
    {
        var name = import.name()?.GetText()?.Trim() ?? string.Empty;
        if (import.IDENT() is { } alias)
            return $"{alias.GetText()} = {name}";
        if (import.import_list() is { } list)
            return $"{name}.{{{list.GetText()}}}";

        // Plain or wildcard ('name.*'); the '.*' is not a sub-rule, so detect a '*' terminal child.
        for (var i = 0; i < import.ChildCount; i++)
            if (import.GetChild(i) is ITerminalNode t && t.GetText().Contains('*'))
                return name + ".*";
        return name;
    }

    private static string ClassName(modelicaParser.Class_specifierContext? spec)
    {
        if (spec is null)
            return string.Empty;
        if (spec.long_class_specifier() is { } l && l.IDENT().Length > 0)
            return l.IDENT(0).GetText();
        if (spec.short_class_specifier() is { } s)
            return s.IDENT().GetText();
        if (spec.der_class_specifier() is { } d && d.IDENT().Length > 0)
            return d.IDENT(0).GetText();
        return string.Empty;
    }

    private static string? ReadStringComment(modelicaParser.String_commentContext? sc)
    {
        var strings = sc?.STRING();
        if (strings is null || strings.Length == 0)
            return null;
        var joined = string.Concat(strings.Select(s => Unquote(s.GetText())));
        return joined.Length == 0 ? null : joined;
    }

    private static string Unquote(string s)
        => s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1] : s;

    private static string GetClassType(modelicaParser.Class_prefixesContext? cp)
    {
        var text = cp?.GetText() ?? string.Empty;
        if (text.Contains("model")) return "model";
        if (text.Contains("function")) return "function";
        if (text.Contains("block")) return "block";
        if (text.Contains("connector")) return "connector";
        if (text.Contains("record")) return "record";
        if (text.Contains("type")) return "type";
        if (text.Contains("package")) return "package";
        return "class";
    }
}

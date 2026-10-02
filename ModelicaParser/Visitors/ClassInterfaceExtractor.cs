using System.Text;
using Antlr4.Runtime;
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
        var spec = cls.class_specifier();

        if (spec?.long_class_specifier()?.composition() is { } composition)
            CollectComposition(composition, elements);

        // `model R2 = Resistor(R = 2)`: no elements of its own, so what it is comes from its base.
        // The enumeration form has no type specifier and is not one of these.
        var shortClass = spec?.short_class_specifier();
        var shortBase = shortClass?.type_specifier()?.GetText();

        return new ClassInterface
        {
            Description = ClassDescription(spec),
            Elements = elements,
            ShortClassBase = shortBase,
            ShortClassModifications = shortBase is null ? null : ScalarModifications(shortClass!.class_modification())
        };
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
                Description = ClassDescription(spec),
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
                DefaultValue = ReadBinding(declaration!.modification()),
                TypeModification = ReadTypeModification(declaration.modification()),
                Modifications = ScalarModifications(declaration.modification()?.class_modification()),
                Condition = SourceText(decl.condition_attribute()?.expression()),
                Description = ReadStringComment(decl.comment()?.string_comment()),
                IsPublic = isPublic,
                Prefixes = prefixes,
                Line = decl.Start.Line
            });
        }
    }

    // The scalar modifications on an extends clause: extends Base(k = 5, T = 2) -> {k:5, T:2}, read as
    // ScalarModifications reads any other.
    private static IReadOnlyDictionary<string, string>? ExtractExtendsModifications(
        modelicaParser.Extends_clauseContext ext)
    {
        var list = ext.class_or_inheritence_modification()?.argument_or_inheritence_list();
        if (list is null)
            return null;

        Dictionary<string, string>? mods = null;
        AddScalarModifications(list.argument(), prefix: "", ref mods);
        return mods;
    }

    /// <summary>
    /// A context's tokens <b>with the spaces between them</b>: one space wherever the source had
    /// whitespace or a comment between two tokens, and none where it had none.
    ///
    /// <para><c>GetText()</c> concatenates the token texts and so loses the spaces between them,
    /// which is harmless for a name or a number and destroys an expression: the condition
    /// <c>use_reset and use_set</c> comes back as <c>use_resetanduse_set</c>, a single identifier
    /// that resolves to nothing. It read as one more undecidable condition rather than as a bug,
    /// which is how it survived until a connector that should have gone stayed on the picture.
    /// Values had the same defect after conditions were fixed (B317): <c>k = pulse.y and step.y</c>
    /// read as <c>pulse.yandstep.y</c>, which a label then showed as <b>y</b>, and
    /// <c>get_class_interface</c> reported a default of <c>ifathen1else2</c>.</para>
    ///
    /// <para><b>Rebuilt from the tokens rather than cut from the source</b>, so a value written over
    /// three lines, or with a comment inside it, comes back as one line a reader can use.</para>
    /// </summary>
    private static string? SourceText(ParserRuleContext? context)
    {
        if (context?.Start is null || context.Stop is null)
            return null;

        var text = new StringBuilder();
        IToken? previous = null;
        AppendTokens(context, text, ref previous);

        return text.Length > 0 ? text.ToString() : null;
    }

    private static void AppendTokens(IParseTree node, StringBuilder text, ref IToken? previous)
    {
        if (node is ITerminalNode terminal)
        {
            var token = terminal.Symbol;
            // A comment is a token of its own in this grammar, and it is no part of the value.
            if (token.Type is TokenConstants.EOF or modelicaParser.COMMENT or modelicaParser.LINE_COMMENT)
                return;
            if (previous is not null && token.StartIndex > previous.StopIndex + 1)
                text.Append(' ');
            text.Append(token.Text);
            previous = token;
            return;
        }

        for (var i = 0; i < node.ChildCount; i++)
            AppendTokens(node.GetChild(i), text, ref previous);
    }

    /// <summary>
    /// The scalar arguments of a modification, each keyed by the path it reaches:
    /// <c>(J = 1, flange_a(phi = 0), flange_b.phi = 2)</c> yields {J:1, flange_a.phi:0,
    /// flange_b.phi:2}. A nested modification and a dotted one are two spellings of the same thing
    /// (MLS §7.2), so they are read into the same key; reading only the dotted spelling made the
    /// nested one - the commoner - a value nobody saw.
    /// </summary>
    private static IReadOnlyDictionary<string, string>? ScalarModifications(
        modelicaParser.Class_modificationContext? classMod)
    {
        var list = classMod?.argument_list();
        if (list is null)
            return null;

        Dictionary<string, string>? mods = null;
        AddScalarModifications(list.argument(), prefix: "", ref mods);
        return mods;
    }

    private static void AddScalarModifications(
        IEnumerable<modelicaParser.ArgumentContext> arguments, string prefix, ref Dictionary<string, string>? mods)
    {
        foreach (var arg in arguments)
        {
            // A redeclaration replaces a type; it gives nothing a value.
            var em = arg.element_modification_or_replaceable()?.element_modification();
            var name = em?.name()?.GetText();
            if (string.IsNullOrEmpty(name))
                continue;

            var key = prefix + name;
            var modification = em!.modification();
            if (modification?.class_modification()?.argument_list() is { } nested)
                AddScalarModifications(nested.argument(), key + ".", ref mods);
            if (ScalarModificationValue(modification) is { } value)
                (mods ??= new Dictionary<string, string>(StringComparer.Ordinal))[key] = value;
        }
    }

    private static string? ScalarModificationValue(modelicaParser.ModificationContext? mod)
    {
        // A class_modification, even one with a binding after it, is not a scalar value.
        if (mod is null || mod.class_modification() is not null)
            return null;
        return SourceText(mod.modification_expression());
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

    /// <summary>
    /// The value the component defaults to — the binding expression, without its <c>=</c>/<c>:=</c>.
    ///
    /// <para>A declaration can carry a type modification and a binding at once
    /// (<c>parameter SI.Length L(min = 0) = 1</c>, one in eight parameters in the Modelica Standard
    /// Library), and the two are separate things: <c>min = 0</c> constrains the type, <c>1</c> is the
    /// value. Reading them off the grammar rather than splitting the text is what keeps a caller that
    /// asks for the default from being handed <c>(min=0)=1</c>.</para>
    /// </summary>
    private static string? ReadBinding(modelicaParser.ModificationContext? mod)
        => SourceText(mod?.modification_expression());

    /// <summary>
    /// The modification applied to the component's type, e.g. <c>(min = 0)</c> or <c>(k = 2)</c>. It
    /// sets attributes on the type or on a sub-component; it is not a value the component takes.
    /// </summary>
    private static string? ReadTypeModification(modelicaParser.ModificationContext? mod)
    {
        var text = mod?.class_modification()?.GetText()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
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

    /// <summary>
    /// The class's own description string, in whichever of the three forms declares it.
    ///
    /// <para>A long class definition carries it after the name; a short one (<c>type Gain = Real "a
    /// gain"</c>) and a der one have no composition, so theirs lives in the trailing comment. Reading
    /// only the long form scores a described type as undocumented in the coverage metrics and hands an
    /// agent <c>description: null</c> over MCP, while the description rule — which does read all three
    /// — correctly says nothing is missing. That disagreement is what this exists to prevent.</para>
    /// </summary>
    private static string? ClassDescription(modelicaParser.Class_specifierContext? spec)
    {
        if (spec is null)
            return null;
        if (spec.long_class_specifier() is { } lng)
            return ReadStringComment(lng.string_comment());
        if (spec.short_class_specifier() is { } sht)
            return ReadStringComment(sht.comment()?.string_comment());
        if (spec.der_class_specifier() is { } der)
            return ReadStringComment(der.comment()?.string_comment());
        return null;
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

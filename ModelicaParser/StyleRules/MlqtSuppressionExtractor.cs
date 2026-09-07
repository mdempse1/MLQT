using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Collects <c>__MLQT(...)</c> vendor-annotation suppression directives from a parse tree:
/// class-level and component-level <c>suppress</c> lists, <c>spelling</c> word lists, and
/// <c>preserveOrder</c> / <c>format=false</c> formatting opt-outs. Extends <see cref="VisitorWithModelNameTracking"/> so
/// directives are keyed by the same fully qualified model name that findings carry (and so nested
/// standalone classes — checked independently — are skipped the same way).
/// </summary>
public sealed class MlqtSuppressionExtractor : VisitorWithModelNameTracking
{
    private readonly Dictionary<string, HashSet<string>> _classLevel = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), HashSet<string>> _componentLevel = new();
    private readonly HashSet<string> _preserveFormatting = new(StringComparer.Ordinal);
    // Case-insensitive, as the repository's accepted spellings are: a word accepted in one casing is
    // accepted in any.
    private readonly Dictionary<string, HashSet<string>> _spellingWords = new(StringComparer.Ordinal);

    public MlqtSuppressionExtractor(string basePackage = "") : base(basePackage) { }

    public SuppressionSet Build() => new(_classLevel, _componentLevel, _preserveFormatting, _spellingWords);

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        foreach (var annotation in context.annotation())
            ReadMlqt(annotation, component: null);
        return base.VisitComposition(context);
    }

    // Short/der class definitions (e.g. `type Length = Real(unit="m")`) have no composition — their
    // class-level annotation lives in the trailing comment. Read it as a class-level directive.
    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        if (context.comment()?.annotation() is { } annotation)
            ReadMlqt(annotation, component: null);
        return base.VisitShort_class_specifier(context);
    }

    public override object? VisitDer_class_specifier([NotNull] modelicaParser.Der_class_specifierContext context)
    {
        if (context.comment()?.annotation() is { } annotation)
            ReadMlqt(annotation, component: null);
        return base.VisitDer_class_specifier(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        var name = context.declaration()?.IDENT()?.GetText();
        var annotation = context.comment()?.annotation();
        if (name is not null && annotation is not null)
            ReadMlqt(annotation, component: StripQuotes(name));
        return base.VisitComponent_declaration(context);
    }

    private void ReadMlqt(modelicaParser.AnnotationContext annotation, string? component)
    {
        var args = annotation.class_modification()?.argument_list();
        if (args is null) return;

        foreach (var arg in args.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod?.name()?.GetText() != "__MLQT")
                continue;

            var inner = elemMod.modification()?.class_modification()?.argument_list();
            if (inner is null)
                continue;

            foreach (var innerArg in inner.argument())
            {
                var m = innerArg.element_modification_or_replaceable()?.element_modification();
                var key = m?.name()?.GetText();
                if (m is null || key is null) continue;

                var value = m.modification()?.modification_expression()?.GetText();
                switch (key)
                {
                    case "suppress":
                        AddTokens(component, ParseList(value));
                        break;
                    // Recorded against the class even when written on a component: a spelling finding
                    // names no element, so a component-scoped word list would match nothing and would
                    // read as the annotation being ignored.
                    case "spelling":
                        AddSpellingWords(ParseList(value));
                        break;
                    case "preserveOrder":
                        if (component is null && IsTrue(value)) MarkPreserveFormatting();
                        break;
                    case "format":
                        if (component is null && IsFalse(value)) MarkPreserveFormatting();
                        break;
                }
            }
        }
    }

    // `preserveOrder=true` / `format=false` at the class level: record it for the formatter (so the
    // renderer can skip reordering — see Phase 5b), and suppress the ordering/formatting rules so the
    // checker doesn't flag the deliberate layout.
    private void MarkPreserveFormatting()
    {
        _preserveFormatting.Add(CurrentModelName);
        AddTokens(component: null, FormattingRuleIds);
    }

    private static readonly string[] FormattingRuleIds =
    [
        RuleIds.ImportStatementsFirst, RuleIds.ExtendsAtTop,
        RuleIds.InitialEqAlgoFirst, RuleIds.InitialEqAlgoLast,
        RuleIds.OneOfEachSection, RuleIds.DontMixEquationAndAlgorithm,
        RuleIds.DontMixConnections
    ];

    private void AddSpellingWords(IEnumerable<string> words)
    {
        if (!_spellingWords.TryGetValue(CurrentModelName, out var set))
            _spellingWords[CurrentModelName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in words)
            set.Add(word);
    }

    private void AddTokens(string? component, IEnumerable<string> tokens)
    {
        HashSet<string> set;
        if (component is null)
        {
            if (!_classLevel.TryGetValue(CurrentModelName, out set!))
                _classLevel[CurrentModelName] = set = new HashSet<string>(StringComparer.Ordinal);
        }
        else
        {
            var key = (CurrentModelName, component);
            if (!_componentLevel.TryGetValue(key, out set!))
                _componentLevel[key] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (var token in tokens)
            set.Add(token);
    }

    private static IEnumerable<string> ParseList(string? quoted)
        => string.IsNullOrEmpty(quoted)
            ? []
            : StripQuotes(quoted).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsTrue(string? v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    private static bool IsFalse(string? v) => string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

    private static string StripQuotes(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }
}

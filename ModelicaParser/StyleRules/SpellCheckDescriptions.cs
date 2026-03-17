using Antlr4.Runtime.Misc;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that spell checks description strings on classes and components.
/// Collects component/variable names per class scope so that references to
/// local identifiers in descriptions are not flagged as misspellings.
/// </summary>
public class SpellCheckDescriptions : VisitorWithModelNameTracking
{
    private readonly SpellChecker _spellChecker;
    private readonly IReadOnlySet<string>? _knownModelNames;
    private readonly Stack<HashSet<string>> _scopedNames = new();

    public SpellCheckDescriptions(
        SpellChecker spellChecker,
        IReadOnlySet<string>? knownModelNames = null,
        string basePackage = "")
        : base(basePackage)
    {
        _spellChecker = spellChecker;
        _knownModelNames = knownModelNames;
    }

    protected override void OnClassEntered()
    {
        _scopedNames.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    protected override void OnClassExited()
    {
        if (_scopedNames.Count > 0)
            _scopedNames.Pop();
    }

    public override object? VisitLong_class_specifier([NotNull] modelicaParser.Long_class_specifierContext context)
    {
        CheckStringComment(context.string_comment(), context.Start.Line);
        return base.VisitLong_class_specifier(context);
    }

    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        var stringComment = context.comment()?.string_comment();
        CheckStringComment(stringComment, context.Start.Line);
        return base.VisitShort_class_specifier(context);
    }

    public override object? VisitDer_class_specifier([NotNull] modelicaParser.Der_class_specifierContext context)
    {
        var stringComment = context.comment()?.string_comment();
        CheckStringComment(stringComment, context.Start.Line);
        return base.VisitDer_class_specifier(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        // Collect the component name into the current scope
        var declaration = context.declaration();
        if (declaration?.IDENT() != null && _scopedNames.Count > 0)
        {
            _scopedNames.Peek().Add(declaration.IDENT().GetText());
        }

        // Spell check the component's description string
        var stringComment = context.comment()?.string_comment();
        CheckStringComment(stringComment, context.Start.Line);

        return base.VisitComponent_declaration(context);
    }

    private void CheckStringComment(modelicaParser.String_commentContext? stringComment, int fallbackLine)
    {
        if (stringComment == null)
            return;

        var strings = stringComment.STRING();
        if (strings == null || strings.Length == 0)
            return;

        // Build context words: scoped component names + known model names
        var contextWords = BuildContextWords();

        foreach (var stringToken in strings)
        {
            var text = TextExtractor.StripQuotes(stringToken.GetText());
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var startLine = stringToken.Symbol.Line;

            foreach (var (word, charOffset) in TextExtractor.TokenizeToWords(text))
            {
                if (TextExtractor.ShouldSkipWord(word))
                    continue;

                if (!_spellChecker.IsCorrect(word, contextWords))
                {
                    var lineNumber = startLine + TextExtractor.CountNewlinesBefore(text, charOffset);
                    AddViolation(lineNumber, $"Misspelled word '{word}' in description");
                }
            }
        }
    }

    private HashSet<string>? BuildContextWords()
    {
        var hasScoped = _scopedNames.Count > 0 && _scopedNames.Peek().Count > 0;
        var hasModelNames = _knownModelNames != null && _knownModelNames.Count > 0;

        if (!hasScoped && !hasModelNames)
            return null;

        var context = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (hasScoped)
        {
            foreach (var scope in _scopedNames)
            {
                foreach (var name in scope)
                    context.Add(name);
            }
        }

        if (_knownModelNames != null)
        {
            foreach (var name in _knownModelNames)
                context.Add(name);
        }

        return context;
    }
}

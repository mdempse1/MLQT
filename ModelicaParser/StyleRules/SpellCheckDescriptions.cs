using Antlr4.Runtime.Misc;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that spell checks description strings on classes and components. The names in scope —
/// the class's own elements and those it inherits — are supplied by
/// <see cref="SpellCheckVisitorBase"/> so that references to them are not flagged as misspellings.
/// </summary>
public class SpellCheckDescriptions : SpellCheckVisitorBase
{
    public SpellCheckDescriptions(
        SpellChecker spellChecker,
        IReadOnlySet<string>? knownModelNames = null,
        string basePackage = "",
        Func<string, IReadOnlySet<string>>? inheritedElementNames = null)
        : base(spellChecker, knownModelNames, basePackage, inheritedElementNames)
    {
    }

    protected override void OnClassScopeReady(modelicaParser.Long_class_specifierContext context)
        => CheckStringComment(context.string_comment());

    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        CheckStringComment(context.comment()?.string_comment());
        return base.VisitShort_class_specifier(context);
    }

    public override object? VisitDer_class_specifier([NotNull] modelicaParser.Der_class_specifierContext context)
    {
        CheckStringComment(context.comment()?.string_comment());
        return base.VisitDer_class_specifier(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        // The class's own components are collected when the class is entered; this covers the ones
        // declared somewhere the class scope scan does not reach, such as inside a nested class this
        // visitor still walks.
        AddNameToScope(context.declaration()?.IDENT()?.GetText());

        CheckStringComment(context.comment()?.string_comment());

        return base.VisitComponent_declaration(context);
    }

    private void CheckStringComment(modelicaParser.String_commentContext? stringComment)
    {
        if (stringComment == null)
            return;

        var strings = stringComment.STRING();
        if (strings == null || strings.Length == 0)
            return;

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

                if (!IsSpelledCorrectly(word))
                {
                    var lineNumber = startLine + TextExtractor.CountNewlinesBefore(text, charOffset);
                    AddFinding(lineNumber, SpellingMessage.For(word, SpellingMessage.InDescription),
                        RuleIds.SpellingDescription, discriminator: word);
                }
            }
        }
    }
}

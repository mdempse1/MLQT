using Antlr4.Runtime.Misc;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that spell checks Documentation annotation strings (info and revisions).
/// Strips HTML before checking, and collects component/variable names per class scope
/// so that references to local identifiers in documentation are not flagged.
/// </summary>
public class SpellCheckDocumentation : VisitorWithModelNameTracking
{
    private readonly SpellChecker _spellChecker;
    private readonly IReadOnlySet<string>? _knownModelNames;
    private readonly Stack<HashSet<string>> _scopedNames = new();

    public SpellCheckDocumentation(
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

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        // Collect component names into the current scope
        var declaration = context.declaration();
        if (declaration?.IDENT() != null && _scopedNames.Count > 0)
        {
            _scopedNames.Peek().Add(declaration.IDENT().GetText());
        }

        return base.VisitComponent_declaration(context);
    }

    public override object? VisitAnnotation([NotNull] modelicaParser.AnnotationContext context)
    {
        var classMod = context.class_modification();
        if (classMod == null)
            return base.VisitAnnotation(context);

        var argList = classMod.argument_list();
        if (argList == null)
            return base.VisitAnnotation(context);

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null)
                continue;

            var name = elemMod.name()?.GetText();
            if (name != "Documentation")
                continue;

            // Found Documentation annotation — extract info and revisions
            var modification = elemMod.modification();
            if (modification?.class_modification() != null)
            {
                CheckDocumentationParameters(modification.class_modification(), context.Start.Line);
            }
        }

        return base.VisitAnnotation(context);
    }

    private void CheckDocumentationParameters(
        modelicaParser.Class_modificationContext docMod, int fallbackLine)
    {
        var argList = docMod.argument_list();
        if (argList == null)
            return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null)
                continue;

            var paramName = elemMod.name()?.GetText();
            if (paramName != "info" && paramName != "revisions")
                continue;

            // Get the string value from the modification
            var modification = elemMod.modification();
            if (modification?.modification_expression()?.expression() == null)
                continue;

            // Extract STRING token(s) from the expression
            var strings = FindStringTokens(modification.modification_expression().expression());
            foreach (var stringToken in strings)
            {
                var text = TextExtractor.StripQuotes(stringToken.GetText());
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var startLine = stringToken.Symbol.Line;

                // Strip HTML tags and decode entities, preserving newlines for line counting
                var plainText = TextExtractor.StripHtmlPreservingNewlines(text);
                if (string.IsNullOrWhiteSpace(plainText))
                    continue;

                var contextWords = BuildContextWords();
                var label = paramName == "info" ? "documentation info" : "documentation revisions";

                foreach (var (word, charOffset) in TextExtractor.TokenizeToWords(plainText))
                {
                    if (TextExtractor.ShouldSkipWord(word))
                        continue;

                    if (!_spellChecker.IsCorrect(word, contextWords))
                    {
                        var lineNumber = startLine + TextExtractor.CountNewlinesBefore(plainText, charOffset);
                        AddViolation(lineNumber, $"Misspelled word '{word}' in {label}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Recursively finds all STRING terminal tokens within an expression context.
    /// </summary>
    private static List<Antlr4.Runtime.Tree.ITerminalNode> FindStringTokens(
        Antlr4.Runtime.ParserRuleContext context)
    {
        var result = new List<Antlr4.Runtime.Tree.ITerminalNode>();
        CollectStringTokens(context, result);
        return result;
    }

    private static void CollectStringTokens(
        Antlr4.Runtime.Tree.IParseTree tree,
        List<Antlr4.Runtime.Tree.ITerminalNode> result)
    {
        if (tree is Antlr4.Runtime.Tree.ITerminalNode terminal &&
            terminal.Symbol.Type == modelicaParser.STRING)
        {
            result.Add(terminal);
            return;
        }

        for (int i = 0; i < tree.ChildCount; i++)
        {
            CollectStringTokens(tree.GetChild(i), result);
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

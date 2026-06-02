using System.Text;
using System.Text.RegularExpressions;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;

namespace ModelicaParser.SpellChecking;

/// <summary>
/// Applies spelling corrections to Modelica source code. Replaces whole-word, case-sensitive
/// occurrences of a misspelled word with a correction, but only inside the same text the spell
/// checker inspects: class/component description strings and Documentation info/revisions strings.
///
/// For Documentation strings (which contain HTML) the replacement is restricted to prose: words
/// inside HTML tags (so hyperlink <c>href</c>s are never altered) and inside <c>code</c>/<c>pre</c>
/// blocks are left untouched. Identifiers, keywords, numbers and ordinary string literals are never
/// modified because only the spell-checked string tokens are considered.
/// </summary>
public static class SpellingCorrector
{
    // Matches whole HTML/XML tags (e.g. <a href="...">, </a>). Used to protect tag markup —
    // including link href attributes — from word replacement in documentation strings.
    private static readonly Regex HtmlTagRegex =
        new(@"<[^>]+>", RegexOptions.Compiled);

    // Matches entire <code>...</code> / <pre>...</pre> blocks (code samples, not prose).
    private static readonly Regex CodeBlockRegex =
        new(@"<(code|pre)[^>]*>.*?</\1>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Replaces whole-word, case-sensitive occurrences of <paramref name="oldWord"/> with
    /// <paramref name="newWord"/> inside description and Documentation strings of the supplied
    /// Modelica source. Returns the corrected (LF-normalized) code and the number of replacements
    /// made. If nothing matched the original code is returned unchanged with a count of zero.
    /// </summary>
    public static (string correctedCode, int replacements) ReplaceWordInStrings(
        string modelicaCode, string oldWord, string newWord)
    {
        if (string.IsNullOrEmpty(modelicaCode) || string.IsNullOrEmpty(oldWord) || newWord is null)
            return (modelicaCode, 0);

        // Parse on the same preprocessed text we will rebuild from, so token offsets line up.
        var code = ModelicaParserHelper.PreprocessCode(modelicaCode);
        var (tree, _) = ModelicaParserHelper.ParseWithErrors(modelicaCode);

        var collector = new StringTokenCollector();
        collector.Visit(tree);

        var wordRegex = BuildWordRegex(oldWord);
        var edits = new List<(int start, int stop, string replacement)>();
        int total = 0;

        foreach (var token in collector.DescriptionStrings)
        {
            var original = token.Symbol.Text;
            var replaced = wordRegex.Replace(original, _ => { total++; return newWord; });
            if (!ReferenceEquals(replaced, original) && replaced != original)
                edits.Add((token.Symbol.StartIndex, token.Symbol.StopIndex, replaced));
        }

        foreach (var token in collector.DocumentationStrings)
        {
            var original = token.Symbol.Text;
            var replaced = ReplaceInHtmlProse(original, wordRegex, newWord, ref total);
            if (replaced != original)
                edits.Add((token.Symbol.StartIndex, token.Symbol.StopIndex, replaced));
        }

        if (edits.Count == 0)
            return (code, 0);

        var sb = new StringBuilder(code);
        foreach (var (start, stop, replacement) in edits.OrderByDescending(e => e.start))
        {
            sb.Remove(start, stop - start + 1);
            sb.Insert(start, replacement);
        }

        return (sb.ToString(), total);
    }

    /// <summary>
    /// Builds a case-sensitive whole-word matcher whose boundaries mirror
    /// <see cref="TextExtractor.TokenizeToWords"/> (word characters are letters, digits,
    /// apostrophes and underscores), so a correction never matches a substring of a larger token.
    /// </summary>
    private static Regex BuildWordRegex(string word) =>
        new(@"(?<![\p{L}\p{N}'_])" + Regex.Escape(word) + @"(?![\p{L}\p{N}'_])",
            RegexOptions.Compiled);

    /// <summary>
    /// Replaces matches of <paramref name="wordRegex"/> in an HTML documentation string, skipping
    /// any occurrence that falls inside an HTML tag (protecting link hrefs and other attributes) or
    /// inside a code/pre block (protecting code samples).
    /// </summary>
    private static string ReplaceInHtmlProse(string text, Regex wordRegex, string newWord, ref int total)
    {
        var protectedSpans = new List<(int start, int end)>();
        foreach (Match m in CodeBlockRegex.Matches(text))
            protectedSpans.Add((m.Index, m.Index + m.Length));
        foreach (Match m in HtmlTagRegex.Matches(text))
            protectedSpans.Add((m.Index, m.Index + m.Length));

        int localCount = 0;
        var result = wordRegex.Replace(text, match =>
        {
            foreach (var (start, end) in protectedSpans)
            {
                if (match.Index >= start && match.Index < end)
                    return match.Value; // inside a protected span — leave unchanged
            }
            localCount++;
            return newWord;
        });

        total += localCount;
        return result;
    }

    /// <summary>
    /// Collects the STRING tokens the spell checker inspects, separated into class/component
    /// description strings and Documentation info/revisions strings, across the whole parse tree.
    /// </summary>
    private sealed class StringTokenCollector : modelicaBaseVisitor<object?>
    {
        public List<ITerminalNode> DescriptionStrings { get; } = new();
        public List<ITerminalNode> DocumentationStrings { get; } = new();

        public override object? VisitLong_class_specifier(
            [NotNull] modelicaParser.Long_class_specifierContext context)
        {
            CollectDescription(context.string_comment());
            return base.VisitLong_class_specifier(context);
        }

        public override object? VisitShort_class_specifier(
            [NotNull] modelicaParser.Short_class_specifierContext context)
        {
            CollectDescription(context.comment()?.string_comment());
            return base.VisitShort_class_specifier(context);
        }

        public override object? VisitDer_class_specifier(
            [NotNull] modelicaParser.Der_class_specifierContext context)
        {
            CollectDescription(context.comment()?.string_comment());
            return base.VisitDer_class_specifier(context);
        }

        public override object? VisitComponent_declaration(
            [NotNull] modelicaParser.Component_declarationContext context)
        {
            CollectDescription(context.comment()?.string_comment());
            return base.VisitComponent_declaration(context);
        }

        public override object? VisitAnnotation([NotNull] modelicaParser.AnnotationContext context)
        {
            var argList = context.class_modification()?.argument_list();
            if (argList == null)
                return base.VisitAnnotation(context);

            foreach (var arg in argList.argument())
            {
                var elemMod = arg.element_modification_or_replaceable()?.element_modification();
                if (elemMod?.name()?.GetText() != "Documentation")
                    continue;

                var docMod = elemMod.modification()?.class_modification();
                if (docMod != null)
                    CollectDocumentation(docMod);
            }

            return base.VisitAnnotation(context);
        }

        private void CollectDescription(modelicaParser.String_commentContext? stringComment)
        {
            var strings = stringComment?.STRING();
            if (strings == null)
                return;
            foreach (var token in strings)
                DescriptionStrings.Add(token);
        }

        private void CollectDocumentation(modelicaParser.Class_modificationContext docMod)
        {
            var argList = docMod.argument_list();
            if (argList == null)
                return;

            foreach (var arg in argList.argument())
            {
                var elemMod = arg.element_modification_or_replaceable()?.element_modification();
                var paramName = elemMod?.name()?.GetText();
                if (paramName != "info" && paramName != "revisions")
                    continue;

                var expression = elemMod!.modification()?.modification_expression()?.expression();
                if (expression == null)
                    continue;

                CollectStringTokens(expression, DocumentationStrings);
            }
        }

        private static void CollectStringTokens(IParseTree tree, List<ITerminalNode> result)
        {
            if (tree is ITerminalNode terminal && terminal.Symbol.Type == modelicaParser.STRING)
            {
                result.Add(terminal);
                return;
            }

            for (int i = 0; i < tree.ChildCount; i++)
                CollectStringTokens(tree.GetChild(i), result);
        }
    }
}

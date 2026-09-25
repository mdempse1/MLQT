namespace MLQT.Shared.Components;

public partial class CodeViewer
{
    /// <summary>
    /// Lines of code with markup tags (from ModelicaSyntaxVisitor with renderForCodeEditor=true)
    /// </summary>
    [Parameter]
    public List<string> Lines { get; set; } = new();

    /// <summary>
    /// Maximum height of the code viewer in pixels (optional)
    /// </summary>
    [Parameter]
    public string Height { get; set; } = string.Empty;

    /// <summary>
    /// Words flagged as misspelled in the current model. Any whole-word, case-sensitive
    /// occurrence inside a string or comment is wrapped in a <c>.code-misspell</c> span so it
    /// can be underlined and right-clicked for correction.
    /// </summary>
    [Parameter]
    public IReadOnlyCollection<string>? MisspelledWords { get; set; }

    /// <summary>
    /// Text the user is searching the code for. Every occurrence is wrapped in a
    /// <c>.code-search-match</c> span so it stands out (B176). Case-insensitive, because nobody
    /// searching code for <c>der</c> means only lower-case <c>der</c>.
    /// </summary>
    [Parameter]
    public string? SearchTerm { get; set; }

    private List<string> _htmlLines = new();
    private List<string>? _lastLines;
    private IReadOnlyCollection<string>? _lastWords;
    private string? _lastSearch;

    private static readonly System.Text.RegularExpressions.Regex _tagRegex =
        new(@"<(KEYWORD|IDENT|NAME|TYPE|OPERATOR|NUMBER|STRING|COMMENT|FUNCTION|LINENUMBER)>(.*?)</\1>",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    protected override void OnParametersSet()
    {
        // Re-process when the lines reference changes, the misspelled-word set changes, or the
        // search term does.
        if (Lines != _lastLines || !ReferenceEquals(MisspelledWords, _lastWords) || SearchTerm != _lastSearch)
        {
            _lastLines = Lines;
            _lastWords = MisspelledWords;
            _lastSearch = SearchTerm;
            _htmlLines = ToHtml(Lines, MisspelledWords, SearchTerm);
        }
    }

    /// <summary>
    /// The rendered form of a syntax-highlighted source listing: the markup tags the highlighter
    /// emits become spans, everything between them is HTML-encoded, and misspelled words inside
    /// strings and comments are wrapped so they can be right-clicked.
    /// </summary>
    /// <remarks>
    /// The two halves are composed here so there is one entry point to hold to a test. The encoding
    /// is the part that matters: the input is Modelica source, which contains angle brackets and
    /// ampersands in ordinary strings and comments, and it is interpolated into markup the browser
    /// then parses.
    /// </remarks>
    internal static List<string> ToHtml(
        List<string>? lines, IReadOnlyCollection<string>? misspelledWords, string? searchTerm = null)
        => ConvertLinesToHtml(lines, BuildMisspellMatcher(misspelledWords),
            string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm);

    /// <summary>
    /// The text of one highlighter line <b>as the user reads it</b>: what is inside a tag is the
    /// source verbatim, and what is outside one has been encoded by the highlighter, so it is decoded.
    ///
    /// <para>The two halves have to be treated differently, and treating them alike is wrong both
    /// ways: decoding a tag's content turns a documentation string's literal <c>&amp;lt;</c> — which
    /// is on screen as <c>&amp;lt;</c> — into <c>&lt;</c>, and not decoding what is outside leaves
    /// <c>&amp;amp;</c> in text the user sees as <c>&amp;</c>. The page's line search reads this, so
    /// the lines it finds and the occurrences tinted here agree about what the text says (B340).</para>
    /// </summary>
    internal static string VisibleText(string markupLine)
    {
        var text = new System.Text.StringBuilder(markupLine.Length);
        var last = 0;
        foreach (System.Text.RegularExpressions.Match match in _tagRegex.Matches(markupLine))
        {
            text.Append(System.Net.WebUtility.HtmlDecode(markupLine[last..match.Index]));
            text.Append(match.Groups[2].Value);
            last = match.Index + match.Length;
        }
        text.Append(System.Net.WebUtility.HtmlDecode(markupLine[last..]));
        return text.ToString();
    }

    /// <summary>
    /// A compiled whole-word matcher for the misspelled-word set. It runs over a token's <b>raw</b>
    /// text, so what it matches is the word itself and goes into <c>data-word</c> unchanged.
    /// </summary>
    private sealed record MisspellMatcher(System.Text.RegularExpressions.Regex Regex);

    private static MisspellMatcher? BuildMisspellMatcher(IReadOnlyCollection<string>? words)
    {
        if (words == null || words.Count == 0)
            return null;

        var alternatives = words
            .Where(w => !string.IsNullOrEmpty(w))
            .Distinct(StringComparer.Ordinal)
            .Select(System.Text.RegularExpressions.Regex.Escape)
            .ToList();

        if (alternatives.Count == 0)
            return null;

        // Whole-word, case-sensitive. Boundaries mirror TextExtractor.TokenizeToWords
        // (word chars = letters, digits, apostrophes, underscores).
        var pattern = @"(?<![\p{L}\p{N}'_])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}'_])";
        var regex = new System.Text.RegularExpressions.Regex(
            pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
        return new MisspellMatcher(regex);
    }

    /// <summary>
    /// Pre-converts all markup lines to HTML in one pass.
    ///
    /// <para><b>Every match is made on the text, never on the HTML</b> (B340). This used to encode
    /// each token, wrap its misspellings, and then run the search over the result — so a search for
    /// <c>q</c>, <c>u</c>, <c>o</c> or <c>t</c>, the first keystroke of many searches, split every
    /// <c>&amp;quot;</c> and each quote showed as literal text; <c>lt</c> and <c>amp</c> broke
    /// documentation; and <c>class</c>, <c>span</c> or <c>word</c> matched inside the misspelling
    /// markup and broke the tag. Both are now found in the raw text and the spans laid around encoded
    /// pieces of it, so neither can land inside an entity or a tag.</para>
    ///
    /// <para><b>What this cannot highlight</b>, and it is a real limit rather than an oversight: a
    /// match that spans two tokens. The text is coloured token by token, so <c>der(y)</c> is a
    /// FUNCTION, an OPERATOR and an IDENT in three separate spans, and a highlight across them would
    /// have to be three spans too. The <em>line</em> is still found and still scrolled to — the page
    /// searches the plain text — so the match is reachable, just not tinted.</para>
    /// </summary>
    private static List<string> ConvertLinesToHtml(
        List<string>? lines, MisspellMatcher? misspell, string? search = null)
    {
        if (lines == null || lines.Count == 0)
            return new List<string>();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int width = Math.Max(3, lines.Count.ToString().Length);
        var result = new List<string>(lines.Count);

        for (int lineNumber = 1; lineNumber <= lines.Count; lineNumber++)
        {
            var line = lines[lineNumber - 1];
            var lineNumberText = lineNumber.ToString().PadLeft(width);

            if (string.IsNullOrEmpty(line))
            {
                result.Add($"<span class=\"code-linenumber\">{lineNumberText}</span>&nbsp;");
                continue;
            }

            // The line's own indentation is carried through as ordinary spaces; .code-line is styled
            // white-space: pre, so the browser keeps them. A block here used to convert leading
            // spaces to non-breaking ones and never ran: it counted them on this string, which
            // always starts with the line-number prefix, so the count was always zero.
            //
            // The gutter is never searched: a search for "1" would otherwise light it up rather than
            // the code.
            var html = new System.Text.StringBuilder(line.Length * 2);
            html.Append("<span class=\"code-linenumber\">").Append(lineNumberText).Append("</span>  ");

            var last = 0;
            foreach (System.Text.RegularExpressions.Match match in _tagRegex.Matches(line))
            {
                // Between tags the highlighter has already encoded the text; decode it so it is
                // searched as it reads, and encode it again on the way out.
                AppendText(html, System.Net.WebUtility.HtmlDecode(line[last..match.Index]), null, search);

                var tagType = match.Groups[1].Value.ToLowerInvariant();
                html.Append("<span class=\"code-").Append(tagType).Append("\">");

                // Underline misspelled words inside strings and comments so they can be
                // right-clicked. Other token types (identifiers, keywords) are never spell-checked.
                var words = tagType is "string" or "comment" ? misspell : null;
                AppendText(html, match.Groups[2].Value, words, tagType == "linenumber" ? null : search);
                html.Append("</span>");

                last = match.Index + match.Length;
            }
            AppendText(html, System.Net.WebUtility.HtmlDecode(line[last..]), null, search);

            result.Add(html.ToString());
        }

        MLQT.Services.LoggingService.Debug("CodeViewer",
            $"ConvertLinesToHtml: {lines.Count} lines in {sw.ElapsedMilliseconds}ms");

        return result;
    }

    /// <summary>
    /// Appends <paramref name="text"/> — raw, as the user reads it — HTML-encoded, with each
    /// misspelled word wrapped for right-clicking and each occurrence of the search term tinted.
    /// A search match straddling the edge of a misspelled word is tinted in two pieces, one either
    /// side, so the spans nest and the word keeps its own.
    /// </summary>
    private static void AppendText(
        System.Text.StringBuilder html, string text, MisspellMatcher? misspell, string? search)
    {
        if (text.Length == 0)
            return;

        var searchRanges = SearchRanges(text, search);

        if (misspell is null)
        {
            AppendSearched(html, text, 0, text.Length, searchRanges);
            return;
        }

        var position = 0;
        foreach (System.Text.RegularExpressions.Match word in misspell.Regex.Matches(text))
        {
            AppendSearched(html, text, position, word.Index, searchRanges);
            html.Append("<span class=\"code-misspell\" data-word=\"")
                .Append(System.Web.HttpUtility.HtmlAttributeEncode(word.Value))
                .Append("\">");
            AppendSearched(html, text, word.Index, word.Index + word.Length, searchRanges);
            html.Append("</span>");
            position = word.Index + word.Length;
        }
        AppendSearched(html, text, position, text.Length, searchRanges);
    }

    /// <summary>Every non-overlapping occurrence of the term, ignoring case, as (start, end).</summary>
    private static List<(int Start, int End)> SearchRanges(string text, string? search)
    {
        var ranges = new List<(int Start, int End)>();
        if (string.IsNullOrEmpty(search))
            return ranges;

        var at = text.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        while (at >= 0)
        {
            ranges.Add((at, at + search.Length));
            at = text.IndexOf(search, at + search.Length, StringComparison.OrdinalIgnoreCase);
        }
        return ranges;
    }

    /// <summary>
    /// Appends <c>text[start..end]</c> encoded, with the parts of it inside a search range tinted.
    /// </summary>
    private static void AppendSearched(
        System.Text.StringBuilder html, string text, int start, int end, List<(int Start, int End)> ranges)
    {
        var position = start;
        foreach (var (rangeStart, rangeEnd) in ranges)
        {
            var from = Math.Max(rangeStart, start);
            var to = Math.Min(rangeEnd, end);
            if (from >= to)
                continue;

            html.Append(System.Web.HttpUtility.HtmlEncode(text[position..from]));
            html.Append("<span class=\"code-search-match\">")
                .Append(System.Web.HttpUtility.HtmlEncode(text[from..to]))
                .Append("</span>");
            position = to;
        }
        html.Append(System.Web.HttpUtility.HtmlEncode(text[position..end]));
    }
}

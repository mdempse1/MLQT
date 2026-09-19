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
        => ConvertLinesToHtml(lines, BuildMisspellMatcher(misspelledWords), BuildSearchMatcher(searchTerm));

    /// <summary>
    /// A matcher for the search term, or null when there is nothing to find.
    ///
    /// <para>Built against the <b>HTML-encoded</b> form of the term, because that is what the
    /// content has been turned into by the time this runs — searching for <c>&lt;html&gt;</c> in a
    /// documentation string has to find <c>&amp;lt;html&amp;gt;</c>. The misspell matcher above is
    /// built the same way for the same reason.</para>
    ///
    /// <para><b>What this cannot highlight</b>, and it is a real limit rather than an oversight: a
    /// match that spans two tokens. The text is coloured token by token, so <c>der(y)</c> is a
    /// FUNCTION, an OPERATOR and an IDENT in three separate spans, and a highlight across them would
    /// have to be three spans too. The <em>line</em> is still found and still scrolled to — the page
    /// searches the plain text — so the match is reachable, just not tinted.</para>
    /// </summary>
    private static System.Text.RegularExpressions.Regex? BuildSearchMatcher(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return null;

        var encoded = System.Web.HttpUtility.HtmlEncode(searchTerm);
        return new System.Text.RegularExpressions.Regex(
            System.Text.RegularExpressions.Regex.Escape(encoded),
            System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Holds a compiled whole-word matcher for the misspelled-word set plus a map from the
    /// HTML-encoded match text back to the original word (used for the <c>data-word</c> attribute).
    /// </summary>
    private sealed record MisspellMatcher(
        System.Text.RegularExpressions.Regex Regex,
        Dictionary<string, string> EncodedToRaw);

    private static MisspellMatcher? BuildMisspellMatcher(IReadOnlyCollection<string>? words)
    {
        if (words == null || words.Count == 0)
            return null;

        var encodedToRaw = new Dictionary<string, string>();
        var alternatives = new List<string>();
        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(word))
                continue;
            // Words are wrapped inside already-HTML-encoded content, so match the encoded form.
            var encoded = System.Web.HttpUtility.HtmlEncode(word);
            if (encodedToRaw.TryAdd(encoded, word))
                alternatives.Add(System.Text.RegularExpressions.Regex.Escape(encoded));
        }

        if (alternatives.Count == 0)
            return null;

        // Whole-word, case-sensitive. Boundaries mirror TextExtractor.TokenizeToWords
        // (word chars = letters, digits, apostrophes, underscores).
        var pattern = @"(?<![\p{L}\p{N}'_])(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}'_])";
        var regex = new System.Text.RegularExpressions.Regex(
            pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
        return new MisspellMatcher(regex, encodedToRaw);
    }

    /// <summary>
    /// Pre-converts all markup lines to HTML in one pass.
    /// </summary>
    private static List<string> ConvertLinesToHtml(
        List<string>? lines, MisspellMatcher? misspell, System.Text.RegularExpressions.Regex? search = null)
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
            var html = $"<LINENUMBER>{lineNumberText}</LINENUMBER>  " + line;

            // Replace markup tags with HTML spans using compiled regex
            html = _tagRegex.Replace(html, match =>
            {
                var tagType = match.Groups[1].Value.ToLower();
                var content = System.Web.HttpUtility.HtmlEncode(match.Groups[2].Value);

                // Underline misspelled words inside strings and comments so they can be
                // right-clicked. Other token types (identifiers, keywords) are never spell-checked.
                if (misspell != null && (tagType == "string" || tagType == "comment"))
                {
                    content = misspell.Regex.Replace(content, m =>
                    {
                        var raw = misspell.EncodedToRaw.TryGetValue(m.Value, out var w) ? w : m.Value;
                        var attr = System.Web.HttpUtility.HtmlAttributeEncode(raw);
                        return $"<span class=\"code-misspell\" data-word=\"{attr}\">{m.Value}</span>";
                    });
                }

                // Search matches are marked in every token type, and not in the line number: a
                // search for "1" would otherwise light up the gutter rather than the code.
                if (search != null && tagType != "linenumber")
                    content = search.Replace(content, m => $"<span class=\"code-search-match\">{m.Value}</span>");

                return $"<span class=\"code-{tagType}\">{content}</span>";
            });

            result.Add(html);
        }

        MLQT.Services.LoggingService.Debug("CodeViewer",
            $"ConvertLinesToHtml: {lines.Count} lines in {sw.ElapsedMilliseconds}ms");

        return result;
    }
}

namespace MLQT.McpServer.Helpers;

/// <summary>
/// What <c>mlqt_find</c>'s text search looks for: every word of the query, in any order, and a
/// <c>"quoted phrase"</c> as one term written as it is.
/// </summary>
/// <remarks>
/// The whole query was one substring, so <c>heat transfer coefficient</c> found only that phrase, and an
/// agent looking for a class by what it does had to guess its documentation's wording, or search once per
/// word and intersect the answers itself. Each term is still matched as a substring, ignoring case, so
/// <c>transfer</c> finds "transferred" as it always did.
/// </remarks>
internal sealed class TextQuery
{
    /// <summary>The terms, each a word or a quoted phrase, in the order written, without repeats.</summary>
    public IReadOnlyList<string> Terms { get; }

    private TextQuery(IReadOnlyList<string> terms) => Terms = terms;

    /// <summary>
    /// The terms of <paramref name="query"/>: runs of anything but white space, and the text between two
    /// double quotes as one. A quote left open runs to the end. Quotes with nothing in them are no term.
    /// </summary>
    public static TextQuery Parse(string query)
    {
        var terms = new List<string>();
        var i = 0;
        while (i < query.Length)
        {
            if (char.IsWhiteSpace(query[i]))
            {
                i++;
                continue;
            }
            if (query[i] == '"')
            {
                var close = query.IndexOf('"', i + 1);
                var end = close < 0 ? query.Length : close;
                Add(query[(i + 1)..end]);
                i = end + 1;
                continue;
            }
            var start = i;
            while (i < query.Length && !char.IsWhiteSpace(query[i]) && query[i] != '"')
                i++;
            Add(query[start..i]);
        }
        return new TextQuery(terms);

        void Add(string term)
        {
            term = term.Trim();
            if (term.Length > 0 && !terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                terms.Add(term);
        }
    }

    /// <summary>Whether <paramref name="text"/> holds every term, ignoring case.</summary>
    public bool AllIn(string? text) =>
        !string.IsNullOrEmpty(text) && Terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether every term is in one or other of <paramref name="texts"/>.</summary>
    public bool AllInAny(params string?[] texts) =>
        Terms.All(t => texts.Any(x => !string.IsNullOrEmpty(x) && x.Contains(t, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Where the first term found in <paramref name="text"/> starts, or -1.</summary>
    public int FirstIndexIn(string text) =>
        Terms.Select(t => text.IndexOf(t, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
}

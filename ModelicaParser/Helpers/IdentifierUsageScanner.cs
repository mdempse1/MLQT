namespace ModelicaParser.Helpers;

/// <summary>
/// Answers "does this source mention this name?" without parsing.
///
/// <c>MLQT.Unused.Import</c> has to ask that of every class nested below the one declaring an import,
/// which in a library is most of the library. Parsing them all to answer a yes/no question about a
/// handful of names would cost far more than the rule is worth, so this scans the raw text instead.
///
/// The scan deliberately over-counts: a name inside a comment, a string, or a documentation block
/// reads as a use. That is the safe direction — the rule under-reports rather than claiming an import
/// is unused when it is not. Matches inside an <c>import</c> clause are the one exclusion, because
/// counting those would let every import mark itself, and its enclosing scope's import, used.
/// </summary>
public static class IdentifierUsageScanner
{
    /// <summary>True if <paramref name="identifier"/> appears in <paramref name="source"/> as a whole
    /// word outside an import clause.</summary>
    public static bool Mentions(string? source, string identifier)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(identifier))
            return false;

        var from = 0;
        while (from <= source.Length - identifier.Length)
        {
            var at = source.IndexOf(identifier, from, StringComparison.Ordinal);
            if (at < 0)
                return false;

            from = at + identifier.Length;
            if (IsWholeWord(source, at, identifier.Length) && !IsInImportClause(source, at))
                return true;
        }

        return false;
    }

    private static bool IsWholeWord(string source, int at, int length)
    {
        if (at > 0 && IsIdentifierChar(source[at - 1]))
            return false;
        var end = at + length;
        return end >= source.Length || !IsIdentifierChar(source[end]);
    }

    // A Modelica identifier is a letter/underscore followed by letters, digits and underscores; a
    // quoted identifier is delimited by ' and is matched here on its inner text (which is how the
    // extractor reports the alias), so the quote must not count as a word character.
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Whether the match at <paramref name="at"/> sits in an import clause. Walks back to the start of
    /// the statement — the previous <c>;</c> or line break, whichever is nearer — and looks for the
    /// keyword. An import broken across lines therefore reads as ordinary code, which under-reports
    /// rather than over-reports and is the direction this rule errs in everywhere else.
    /// </summary>
    private static bool IsInImportClause(string source, int at)
    {
        var start = at;
        while (start > 0 && source[start - 1] is not (';' or '\n' or '\r'))
            start--;

        while (start < at && char.IsWhiteSpace(source[start]))
            start++;

        const string keyword = "import";
        return at - start >= keyword.Length
               && string.CompareOrdinal(source, start, keyword, 0, keyword.Length) == 0
               && !IsIdentifierChar(source[start + keyword.Length]);
    }
}

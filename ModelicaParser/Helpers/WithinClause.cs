namespace ModelicaParser.Helpers;

/// <summary>
/// Adds and removes the leading <c>within ...;</c> clause of a Modelica source string.
///
/// <para>A within clause belongs to a <em>file</em>, not to a class. The convention across MLQT is
/// that a <c>ModelNode</c>'s stored <c>ModelicaCode</c> carries no within clause — that is what
/// <c>ModelExtractorVisitor</c> produces when a file is loaded — while the text written to a
/// <c>.mo</c> file must carry one, so the file re-parses with the right package context and its
/// classes keep their hierarchical IDs. The clause is therefore added when rendering to disk and
/// removed again before the rendered text is stored back on a node.</para>
///
/// <para>Keeping the two directions in one place is deliberate. When each caller rolled its own,
/// the versions drifted: some guarded against a clause that was already there and some did not, so
/// whether a model's stored code carried one depended on which paths had run. A formatter that
/// assumed it did not then wrote a second clause into every file it touched.</para>
/// </summary>
public static class WithinClause
{
    private const string Keyword = "within";

    /// <summary>
    /// Returns <paramref name="source"/> with a leading within clause, adding
    /// <c>within <paramref name="parent"/>;</c> only if it does not already carry one. A null or
    /// empty <paramref name="parent"/> produces the bare <c>within;</c> of a top-level library.
    /// </summary>
    public static string Ensure(string source, string? parent)
    {
        if (StartsWithClause(source, out _))
            return source;

        return !string.IsNullOrEmpty(parent)
            ? string.Concat(Keyword, " ", parent, ";\n", source)
            : string.Concat(Keyword, ";\n", source);
    }

    /// <summary>
    /// Returns <paramref name="source"/> without its leading within clause, and unchanged if it has
    /// none. Any newline immediately following the clause goes with it, so the class that followed
    /// becomes line 1 and stored line numbers stay put.
    /// </summary>
    public static string Strip(string source)
    {
        if (!StartsWithClause(source, out var keywordEnd))
            return source;

        // The clause runs to the first semicolon; a Modelica name cannot contain one.
        var semicolon = source.IndexOf(';', keywordEnd);
        if (semicolon < 0)
            return source;

        var rest = semicolon + 1;
        if (rest < source.Length && source[rest] == '\r')
            rest++;
        if (rest < source.Length && source[rest] == '\n')
            rest++;

        return source[rest..];
    }

    /// <summary>
    /// Returns <paramref name="source"/> with its leading within clause replaced by
    /// <c>within <paramref name="parent"/>;</c>, adding one if it had none.
    /// <para>
    /// Use this, not <see cref="Ensure"/>, wherever the caller decides which package the text
    /// belongs to — creating a class under a known parent, or moving one to a new destination. The
    /// clause must then name that destination: keeping a clause the source happened to arrive with
    /// would file the class under the wrong package, and appending a second one is a syntax error.
    /// <see cref="Ensure"/> is for the other case, where the text's own clause is the authority and
    /// is only supplied when absent.
    /// </para>
    /// </summary>
    public static string Set(string source, string? parent) => Ensure(Strip(source), parent);

    /// <summary>
    /// True when <paramref name="source"/> opens with a within clause. Leading whitespace is
    /// ignored, and an identifier that merely starts with the keyword (<c>withinTolerance</c>) is
    /// not one.
    /// </summary>
    public static bool Has(string source) => StartsWithClause(source, out _);

    /// <summary>
    /// True when <paramref name="source"/> opens with a within clause, ignoring any leading
    /// whitespace — rendered and hand-edited text can start with a blank line, and reading that as
    /// "no clause" is what produced duplicates. <paramref name="keywordEnd"/> is the index just past
    /// the <c>within</c> keyword.
    /// </summary>
    private static bool StartsWithClause(string source, out int keywordEnd)
    {
        var start = 0;
        while (start < source.Length && char.IsWhiteSpace(source[start]))
            start++;

        keywordEnd = start + Keyword.Length;

        if (string.CompareOrdinal(source, start, Keyword, 0, Keyword.Length) != 0)
            return false;

        // "withinTolerance" starts with the keyword but is an identifier, not a clause.
        return keywordEnd >= source.Length
            || (!char.IsLetterOrDigit(source[keywordEnd]) && source[keywordEnd] != '_');
    }
}

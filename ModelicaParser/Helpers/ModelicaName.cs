namespace ModelicaParser.Helpers;

/// <summary>
/// Taking a fully-qualified Modelica name apart: <c>Modelica.Blocks.Sources.Ramp</c> is the class
/// <c>Ramp</c> in the package <c>Modelica.Blocks.Sources</c>, from the library <c>Modelica</c>.
///
/// <para>Three lines of string arithmetic, written out at every site that needed them — the graph
/// analyses, the checker, the metrics, the dashboard — each with its own answer for the name that has
/// no dot in it. That is the sort of thing that stays right until one copy is edited: they must agree,
/// because a base package is what the suppression extractor and every rule visitor are told the class
/// sits in, and a wrong one silently changes which annotations are read.</para>
/// </summary>
/// <para><b>A quoted identifier may contain a dot</b> - <c>Lib.'a.b'.C</c> is three segments, not four -
/// so nothing here, or anywhere else, takes a name apart with <c>Split('.')</c> or
/// <c>LastIndexOf('.')</c>. <see cref="Segments"/> is the one splitter, and every other method is built
/// on <see cref="LastSeparator"/>/<see cref="FirstSeparator"/>, which skip the dots inside quotes.</para>
public static class ModelicaName
{
    /// <summary>
    /// The segments of a dotted name, a quoted identifier kept whole (<c>'a.b'</c>, with its quotes and
    /// any backslash escape in it). Empty for an empty name; a leading dot is not a segment.
    /// </summary>
    public static IReadOnlyList<string> Segments(string? name)
    {
        var segments = new List<string>();
        if (string.IsNullOrEmpty(name))
            return segments;

        var start = name[0] == '.' ? 1 : 0;
        var quoted = false;
        for (var i = start; i < name.Length; i++)
        {
            var c = name[i];
            if (quoted)
            {
                if (c == '\\')
                    i++;
                else if (c == '\'')
                    quoted = false;
            }
            else if (c == '\'')
                quoted = true;
            else if (c == '.')
            {
                segments.Add(name[start..i]);
                start = i + 1;
            }
        }
        segments.Add(name[start..]);
        return segments;
    }

    /// <summary>The segments of <paramref name="name"/> joined back with dots.</summary>
    public static string Join(IEnumerable<string> segments) => string.Join('.', segments);

    /// <summary>
    /// The index of the last dot that separates segments - not one inside a quoted identifier - or -1.
    /// </summary>
    public static int LastSeparator(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return -1;
        var last = -1;
        var quoted = false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (quoted)
            {
                if (c == '\\')
                    i++;
                else if (c == '\'')
                    quoted = false;
            }
            else if (c == '\'')
                quoted = true;
            else if (c == '.')
                last = i;
        }
        return last;
    }

    /// <summary>
    /// The index of the first dot that separates segments, after any leading dot, or -1.
    /// </summary>
    public static int FirstSeparator(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return -1;
        var quoted = false;
        for (var i = name[0] == '.' ? 1 : 0; i < name.Length; i++)
        {
            var c = name[i];
            if (quoted)
            {
                if (c == '\\')
                    i++;
                else if (c == '\'')
                    quoted = false;
            }
            else if (c == '\'')
                quoted = true;
            else if (c == '.')
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Every enclosing name of <paramref name="fullName"/>, innermost first: <c>A.B.C</c> gives
    /// <c>A.B</c>, <c>A</c>. Not the name itself.
    /// </summary>
    public static IEnumerable<string> EnclosingNamesOf(string? fullName)
    {
        var name = EnclosingPackageOf(fullName);
        while (name.Length > 0)
        {
            yield return name;
            name = EnclosingPackageOf(name);
        }
    }

    /// <summary>
    /// The package a class sits in — everything before the last dot — or empty for a top-level name.
    ///
    /// <para>Empty rather than null on purpose: it is passed straight to
    /// <c>VisitorWithModelNameTracking</c>, whose whole constructor surface defaults it to <c>""</c>
    /// to mean "this class is not inside anything".</para>
    /// </summary>
    public static string EnclosingPackageOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var lastDot = LastSeparator(fullName);
        return lastDot > 0 ? fullName[..lastDot] : string.Empty;
    }

    /// <summary>The class's own name — everything after the last dot, or the whole name if there is none.</summary>
    public static string LeafOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var lastDot = LastSeparator(fullName);
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    /// <summary>
    /// True when <paramref name="id"/> is <paramref name="root"/> itself or a class nested inside
    /// it. A namesake that merely starts with the same letters is not.
    /// </summary>
    /// <remarks>
    /// <para><b>The separator is the whole of it (B274).</b> This question was written out by hand
    /// fourteen times across six files, always as
    /// <c>id == root || id.StartsWith(root + ".", StringComparison.Ordinal)</c> — and four of those
    /// copies decide what an MCP tool <b>moves or deletes from a user's repository</b>. Mutation
    /// testing emptied the <c>"."</c> in each of them with no test objecting, which makes
    /// <c>Suspensions</c> a prefix of <c>SuspensionsExtra</c> and sweeps an unrelated library into
    /// the operation. A Modelica name is dot-separated: <c>A.B</c> is inside <c>A</c> and <c>AB</c>
    /// is not, and the only thing between those two readings is a one-character string that reads
    /// like punctuation.</para>
    /// </remarks>
    public static bool IsInSubtree(string id, string root) =>
        string.Equals(id, root, StringComparison.Ordinal) || IsStrictlyInside(id, root);

    /// <summary>
    /// True when <paramref name="id"/> is nested inside <paramref name="root"/> — the subtree
    /// without its own root.
    /// </summary>
    public static bool IsStrictlyInside(string id, string root) =>
        id.Length > root.Length
        && id[root.Length] == '.'
        && id.StartsWith(root, StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="id"/> with its <paramref name="oldRoot"/> prefix replaced by
    /// <paramref name="newRoot"/>, or null when it is not in that subtree at all.
    /// </summary>
    /// <remarks>
    /// Paired with <see cref="IsInSubtree"/> deliberately: every caller that re-roots an id first
    /// asks whether it should, and keeping the two together means the substring arithmetic cannot
    /// be applied to a name the test would have rejected.
    /// </remarks>
    public static string? ReRoot(string id, string oldRoot, string newRoot) =>
        IsInSubtree(id, oldRoot) ? newRoot + id[oldRoot.Length..] : null;

    /// <summary>
    /// The library a class belongs to — the first segment, which is the top-level package Modelica
    /// resolves everything else against.
    /// </summary>
    public static string RootLibraryOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var firstDot = FirstSeparator(fullName);
        return firstDot > 0 ? fullName[..firstDot] : fullName;
    }

    /// <summary>
    /// True for a non-empty name of one segment - a top-level class, or a component named directly
    /// rather than through another. <c>'a.b'</c> is one; <c>a.b</c> is not.
    /// </summary>
    public static bool IsSimple(string? name) => !string.IsNullOrEmpty(name) && LastSeparator(name) < 0;

    /// <summary>
    /// A long name shortened for a narrow column: its first two segments and its last, with the middle
    /// elided - <c>Modelica.Fluid....Pipe</c> - or the name itself when it is no longer than
    /// <paramref name="maxLength"/> or has no middle to leave out.
    /// </summary>
    public static string Abbreviated(string name, int maxLength = 40)
    {
        var segments = Segments(name);
        return name.Length <= maxLength || segments.Count <= 2
            ? name
            : $"{segments[0]}.{segments[1]}....{segments[^1]}";
    }
}

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
public static class ModelicaName
{
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

        var lastDot = fullName.LastIndexOf('.');
        return lastDot > 0 ? fullName[..lastDot] : string.Empty;
    }

    /// <summary>The class's own name — everything after the last dot, or the whole name if there is none.</summary>
    public static string LeafOf(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return string.Empty;

        var lastDot = fullName.LastIndexOf('.');
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

        var firstDot = fullName.IndexOf('.');
        return firstDot > 0 ? fullName[..firstDot] : fullName;
    }
}

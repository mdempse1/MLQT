namespace ModelicaGraph;

/// <summary>
/// "Is this file inside that directory?", asked of file-system paths.
///
/// <para><b>A bare <c>StartsWith</c> is the wrong answer to it</b>: <c>…/Lib</c> is a prefix of
/// <c>…/LibExtra/Other.mo</c>, so a change in a sibling directory reads as a change in the
/// repository. The question is the path-shaped twin of <c>ModelicaName.IsInSubtree</c> for class
/// names, and it had the same history - a private correct copy in <c>GraphBuilder</c> and a wrong
/// one written freshly beside it in <c>VcsTools</c> (B323).</para>
/// </summary>
public static class PathContainment
{
    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="root"/> or lies beneath it. Both are
    /// resolved to full paths first; the comparison ignores case except on Linux.
    /// </summary>
    public static bool IsWithin(string path, string root)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            return false;

        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        var normalisedRoot = Path.TrimEndingDirectorySeparator(Full(root));
        var normalisedPath = Path.TrimEndingDirectorySeparator(Full(path));

        return normalisedPath.StartsWith(normalisedRoot, comparison)
               && (normalisedPath.Length == normalisedRoot.Length
                   || normalisedPath[normalisedRoot.Length] == Path.DirectorySeparatorChar
                   || normalisedPath[normalisedRoot.Length] == Path.AltDirectorySeparatorChar
                   // A root that is itself a drive or filesystem root ends in its separator.
                   || Path.EndsInDirectorySeparator(normalisedRoot));
    }

    private static string Full(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        { return path; }
    }
}

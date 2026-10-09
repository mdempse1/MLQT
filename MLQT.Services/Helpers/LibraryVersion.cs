namespace MLQT.Services.Helpers;

/// <summary>
/// Which version a library is, when more than one thing says.
/// </summary>
/// <remarks>
/// <para><b>The top-level package's <c>version</c> annotation is the answer.</b> It is what the
/// library says about itself, what a <c>uses</c> annotation elsewhere is checked against, and the one
/// statement that travels with the classes. A version in the directory name (<c>Modelica 4.0.0</c>,
/// the language's versioned-directory convention) or the one a read-only source states should agree
/// with it; where they do not, the annotation wins and the disagreement is reported, because a
/// directory renamed by hand, or a source describing another release, is the thing that is wrong.</para>
///
/// <para>An encrypted library has no annotation anyone can read, so for it the claim — its directory
/// name, then <c>libraryinfo.mos</c> — is all there is.</para>
/// </remarks>
internal static class LibraryVersion
{
    /// <summary>
    /// The version in a library's directory or file name, or null when it carries none: the suffix
    /// after the last space, when it looks like a version (<c>Battery 2.9.0</c>, <c>Lib 1.0.mo</c>).
    /// </summary>
    public static string? FromPathName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (name.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            name = name[..^".mo".Length];

        return EncryptedLibraryDetector.SplitVersionedDirectoryName(name).Version;
    }

    /// <summary>
    /// The library's version, and a description of the disagreement when there is one.
    /// </summary>
    /// <param name="annotated">The top-level package's <c>version</c> annotation, or null.</param>
    /// <param name="claimed">What else says: the directory name, or the read-only source. Null if nothing.</param>
    public static (string? Version, string? Conflict) Resolve(string? annotated, string? claimed)
    {
        if (string.IsNullOrWhiteSpace(annotated))
            return (string.IsNullOrWhiteSpace(claimed) ? null : claimed, null);

        var conflict = !string.IsNullOrWhiteSpace(claimed) && !string.Equals(annotated, claimed, StringComparison.Ordinal)
            ? $"its top-level package's version annotation says {annotated}, which is used, but {claimed} is claimed for it"
            : null;
        return (annotated, conflict);
    }
}

using MLQT.Services.DataTypes;

namespace MLQT.Services;

/// <summary>
/// Whether a discovered reference library is worth loading, or is one already in the graph.
/// </summary>
/// <remarks>
/// <para><b>MLQT offers two ways to say "load this for reference", and nothing stopped a user using
/// both for the same directory.</b> A repository can be marked reference-only, and a path can be
/// listed under reference libraries in settings — and a real configuration had all three of its
/// reference paths (<c>...Dymola/Modelica</c>, <c>...Dymola/Modelica/Library</c> and an
/// <c>ExternData</c> checkout) already registered as reference-only repositories. Every library under
/// them was therefore discovered, parsed and indexed twice: <b>159 loads of 97 distinct
/// libraries</b>, with the standard library, <c>ExternData</c> and sixty others loaded a second time
/// (2026-09-08, phase 7b-5).</para>
///
/// <para>The graph survived it — <c>DirectedGraph.AddNode</c> keys on the class id, so a class loaded
/// twice is one node — which is exactly why nobody noticed. What did not survive it is everything
/// counted per <i>library</i> rather than per class: two entries in the browser tree for the same
/// library, and a model total that read <b>118,612 against a true 77,860</b>, which is the number the
/// deferred-analysis threshold is compared against. Plus the parse, paid twice.</para>
///
/// <para>Extracted rather than left inline because it is a decision with three cases and a preference
/// order, and because the one case that <i>was</i> handled had been written as a condition inside the
/// loading loop where nothing could reach it.</para>
/// </remarks>
public static class ReferenceLibraryRules
{
    /// <summary>Windows paths differ only by case; POSIX paths do not.</summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Normalize(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch { return path; }
    }

    /// <summary>
    /// Why this library should not be loaded, or null to load it.
    /// </summary>
    /// <param name="libraryPath">The directory or file <see cref="LibraryDiscovery"/> turned up.</param>
    /// <param name="isEncrypted">Whether that path holds an encrypted library.</param>
    /// <param name="encryptedName">Its name, when the vendor documentation gives one.</param>
    /// <param name="loaded">Everything already in the graph, in load order.</param>
    /// <param name="useEncryptedDocumentation">The user's setting for reconstructing encrypted libraries.</param>
    /// <returns>A reason to log, or null.</returns>
    /// <remarks>
    /// The reason is returned rather than logged so the decision can be tested by asking it, and so
    /// the log line reads the same wherever it is written.
    /// </remarks>
    public static string? ReasonToSkip(
        string libraryPath,
        bool isEncrypted,
        string? encryptedName,
        IReadOnlyList<LoadedLibrary> loaded,
        bool useEncryptedDocumentation)
    {
        // Whether to reconstruct an encrypted library at all is a policy question the user answers in
        // settings; how to load one is not this decision's business.
        if (isEncrypted && !useEncryptedDocumentation)
            return "encrypted library documentation is turned off";

        // The same directory, already loaded — by a reference-only repository, or by another
        // reference path that overlaps this one. Checked first because it is exact: there is no
        // judgement in "this is the same folder", and it is the case that actually occurs.
        var path = Normalize(libraryPath);
        var samePath = loaded.FirstOrDefault(l => PathComparer.Equals(Normalize(l.SourcePath), path));
        if (samePath is not null)
            return $"already loaded as '{samePath.Name}'";

        // The same library from somewhere else. A tool's library folder ships the encrypted build of
        // libraries a user may also have checked out as source, and two copies of one library cannot
        // both be in the graph — they share every class id.
        if (encryptedName is not { Length: > 0 })
            return null;

        var sameName = loaded.FirstOrDefault(l => string.Equals(l.Name, encryptedName, StringComparison.Ordinal));
        if (sameName is null)
            return null;

        // Readable source always beats classes reconstructed from vendor documentation, so a readable
        // candidate is loaded even when an encrypted copy got there first and lets AddNode replace the
        // stubs. Every other combination is a second copy of something already present.
        if (!isEncrypted && sameName.SourceType == LibrarySourceType.EncryptedDirectory)
            return null;

        return $"'{encryptedName}' is already loaded from {sameName.SourcePath}";
    }
}

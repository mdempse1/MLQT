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
    /// The name to compare a discovered reference library by, for
    /// <see cref="ReasonToSkip"/>'s <c>encryptedName</c>: the library's own name, read from the
    /// library, with its folder or file name as the fallback for a readable one.
    /// </summary>
    /// <remarks>
    /// <para><b>Here because the name was being taken from the path, and a tool's library folder
    /// names almost nothing after the package it holds.</b> <c>Modelica 4.1.0</c> carries the
    /// release, and <c>Complex.mo</c>, <c>ObsoleteModelica4.mo</c> and the rest are single-file
    /// libraries with an extension. None of them matched a library already loaded from source, so a
    /// reference path at a tool's library folder loaded a <i>second readable copy</i> of six of
    /// MSL's eight libraries beside the user's checkout — and only <c>ModelicaReference</c>, whose
    /// folder happens to be named after its package, was skipped (2026-11-04).</para>
    ///
    /// <para><b>And here rather than in the loading loop</b>, which is this class's whole point: the
    /// one case that was handled before it was extracted had been written where nothing could reach
    /// it, and resolving the name inline put a second part of the same decision back there.
    /// <see cref="RepositoryService"/>'s discovery has always read the real name — which is why it
    /// does not have this bug — through the same
    /// <see cref="LibraryDiscovery.ReadLibraryName"/>.</para>
    ///
    /// <para>An encrypted library whose name cannot be read still gets null, so it is loaded and
    /// found to contribute nothing rather than guessed at; a readable one falls back to the path's
    /// name, which is all there was before.</para>
    /// </remarks>
    /// <param name="libraryPath">The directory or file <see cref="LibraryDiscovery"/> turned up.</param>
    /// <param name="isEncrypted">Whether that path holds an encrypted library.</param>
    public static string? CandidateName(string libraryPath, bool isEncrypted) =>
        LibraryDiscovery.ReadLibraryName(libraryPath)
        ?? (isEncrypted ? null : Path.GetFileName(libraryPath));

    /// <summary>
    /// Why this library should not be loaded, or null to load it.
    /// </summary>
    /// <param name="libraryPath">The directory or file <see cref="LibraryDiscovery"/> turned up.</param>
    /// <param name="isEncrypted">Whether that path holds an encrypted library.</param>
    /// <param name="encryptedName">
    /// The candidate library's name, or null when it cannot be read — in which case it matches
    /// nothing and is loaded. The parameter is named for the encrypted case it was written for, but
    /// it is the name of whatever kind of library this is, and it must be the <b>library's</b> name
    /// rather than its folder's: <see cref="LibraryDiscovery.ReadLibraryName"/> answers both kinds.
    /// Passed a path's file name instead, a readable library in a versioned directory
    /// (<c>Modelica 4.1.0</c>) or a single file (<c>Complex.mo</c>) matched no loaded library and a
    /// second copy of it was loaded.
    /// </param>
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

        var sameName = loaded.FirstOrDefault(l => LibraryPrecedence.SameLibrary(l.Name, encryptedName));
        if (sameName is null)
            return null;

        // Readable source always beats a read-only copy, so a readable candidate is loaded even when a
        // read-only copy got there first - and that copy is then retired whole as the source registers
        // (LibraryPrecedence), not merged with it class by class. An encrypted candidate
        // against a supplied copy is left to the same rule too, because it is not always the supplied
        // copy that stays: one describing another release gives way to the build installed. Every
        // other combination is a second copy of something already present.
        if (!isEncrypted && sameName.IsReadOnly)
            return null;
        if (isEncrypted && sameName.ReadOnlySource == ModelicaGraph.ReadOnlySourceKind.Supplied)
            return null;

        return $"'{encryptedName}' is already loaded from {sameName.SourcePath}";
    }
}

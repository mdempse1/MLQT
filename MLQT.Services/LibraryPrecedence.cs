using MLQT.Services.DataTypes;
using ModelicaGraph;

namespace MLQT.Services;

/// <summary>
/// A read-only library is never loaded beside a copy of the same library that outranks it. The
/// higher copy wins, and it wins <b>whole</b>: the lower contributes nothing at all, not the classes
/// the higher happens to lack.
/// </summary>
/// <remarks>
/// <para><b>The rank</b> is <see cref="ReadOnlySources.Precedence"/>: readable source, then a
/// supplied library, then one recovered from documentation. Its first case — readable source
/// supersedes an encrypted build (B268) — was once the whole rule, and the class was named after it:
/// <see cref="SourceSupersedesEncrypted"/> remains, obsolete, for code built against that name.</para>
///
/// <para><b>One exception, for the version.</b> A supplied library describes one release. When a
/// library recovered from documentation of the same name is loaded too, that is the release
/// actually installed, and a supplied library naming a different version describes something
/// else: the recovered copy stays and the supplied one goes. Where either version is unknown the
/// rank decides, because nothing distinguishes the two.</para>
///
/// <para><b>And one for two supplied copies</b>, which rank equally: the first stays and the second
/// is not loaded. Both would place their classes under the same in-memory root, so keeping both
/// would leave either one's removal taking the other's files with it.</para>
///
/// <para><b>Why the whole library and not class by class (B268).</b> The two copies are routinely
/// different releases — a tool's library folder ships whatever the tool was built with, and the
/// checkout is whatever the user is working on. Merging them per class kept a stub for every class
/// the newer source had deleted, so the tree showed vendor classes the checkout does not have, and
/// a reference to a deleted class resolved against its stub instead of being reported as broken.
/// Per-class merging also left both libraries' indexes claiming the same ids, which is what
/// <c>TotalModelCount</c>, <c>DictionaryScope</c> and <c>LibraryOwnership</c> each had to be taught
/// to read around.</para>
///
/// <para><b>Two places apply it, deliberately.</b> <c>RepositoryService.LoadLibrariesAsync</c> asks
/// <see cref="ReadableSourceFor"/> before loading, from the names discovery already has, so the
/// encrypted build is never read at all — that is the ordinary case and the one worth being cheap.
/// <c>LibraryDataService</c> asks <see cref="Retires"/> as each library is registered, which is what
/// makes the rule hold for every other route in: a reference-library path, a library added
/// mid-session, a folder whose name is not the library's. Whichever copy registers second sees the
/// first, so no order of arrival leaves both in place.</para>
/// </remarks>
public static class LibraryPrecedence
{
    /// <summary>
    /// Whether two library names are the same library: the top-level package name, compared exactly.
    /// Modelica names are case-sensitive, and a name that could not be read matches nothing — an
    /// encrypted library whose name is unknown is loaded, as it always was.
    /// </summary>
    public static bool SameLibrary(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Where readable source for an encrypted library is, or null when there is none.
    /// </summary>
    /// <param name="encryptedName">The encrypted library's name.</param>
    /// <param name="readable">Every readable library known to the project, by name and location —
    /// loaded or only discovered.</param>
    public static string? ReadableSourceFor(
        string? encryptedName, IEnumerable<(string Name, string Location)> readable)
    {
        foreach (var (name, location) in readable)
        {
            if (SameLibrary(encryptedName, name))
                return location;
        }

        return null;
    }

    /// <summary>
    /// The libraries that must go now that <paramref name="arriving"/> is here: every copy of it that
    /// it outranks, or <paramref name="arriving"/> itself when a copy already loaded outranks it.
    /// Empty when nothing is superseded.
    /// </summary>
    /// <param name="arriving">The library being registered.</param>
    /// <param name="loaded">The libraries already registered, not including it.</param>
    public static IReadOnlyList<LoadedLibrary> Retires(LoadedLibrary arriving, IEnumerable<LoadedLibrary> loaded) =>
        RetirementsFor(arriving, loaded).Select(r => r.Retired).ToList();

    /// <summary>
    /// <see cref="Retires"/>, with the copy that retires each one: <paramref name="arriving"/> for
    /// those it outranks, or the loaded copy that keeps its place against it.
    /// </summary>
    internal static IReadOnlyList<(LoadedLibrary Retired, LoadedLibrary By)> RetirementsFor(
        LoadedLibrary arriving, IEnumerable<LoadedLibrary> loaded)
    {
        var sameName = loaded.Where(l => !ReferenceEquals(l, arriving) && SameLibrary(l.Name, arriving.Name)).ToList();
        if (sameName.FirstOrDefault(l => KeepsItsPlace(l, arriving)) is { } keeper)
            return [(arriving, keeper)];

        return sameName.Where(l => Outranks(arriving, l)).Select(l => (l, arriving)).ToList();
    }

    /// <summary>
    /// The loaded copy of <paramref name="library"/>'s library that keeps its place against it, or
    /// null when none does. Asked before a read-only library is read, so one that would be retired
    /// the moment it registered costs nothing.
    /// </summary>
    internal static LoadedLibrary? OutrankedBy(LoadedLibrary library, IEnumerable<LoadedLibrary> loaded) =>
        loaded.FirstOrDefault(l => !ReferenceEquals(l, library)
                                   && SameLibrary(l.Name, library.Name)
                                   && KeepsItsPlace(l, library));

    /// <summary>
    /// Whether <paramref name="loaded"/> stays when <paramref name="arriving"/>, a copy of the same
    /// library, comes in: it outranks it, or both are supplied and the first one in stays.
    /// </summary>
    private static bool KeepsItsPlace(LoadedLibrary loaded, LoadedLibrary arriving) =>
        Outranks(loaded, arriving)
        || (loaded.ReadOnlySource == ReadOnlySourceKind.Supplied && arriving.ReadOnlySource == ReadOnlySourceKind.Supplied);

    /// <summary>
    /// Whether <paramref name="winner"/> takes the place of <paramref name="loser"/>, two copies of the
    /// same library: by rank, except that a recovered copy keeps its place against a supplied one
    /// describing a different version.
    /// </summary>
    internal static bool Outranks(LoadedLibrary winner, LoadedLibrary loser)
    {
        if (VersionsDisagree(winner, loser))
        {
            return winner.ReadOnlySource == ReadOnlySourceKind.RecoveredFromDocumentation
                   && loser.ReadOnlySource == ReadOnlySourceKind.Supplied;
        }

        return ReadOnlySources.Precedence(winner.ReadOnlySource) > ReadOnlySources.Precedence(loser.ReadOnlySource);
    }

    /// <summary>
    /// Whether a supplied copy and a recovered copy name different versions. Only between those two:
    /// readable source outranks either whatever its version, because it is the user's own.
    /// </summary>
    internal static bool VersionsDisagree(LoadedLibrary a, LoadedLibrary b)
    {
        var kinds = new[] { a.ReadOnlySource, b.ReadOnlySource };
        return kinds.Contains(ReadOnlySourceKind.Supplied)
               && kinds.Contains(ReadOnlySourceKind.RecoveredFromDocumentation)
               && a.Version is { Length: > 0 }
               && b.Version is { Length: > 0 }
               && !string.Equals(a.Version, b.Version, StringComparison.Ordinal);
    }
}

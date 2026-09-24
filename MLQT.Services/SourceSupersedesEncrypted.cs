using MLQT.Services.DataTypes;

namespace MLQT.Services;

/// <summary>
/// An encrypted library is never loaded beside readable source for the same library. The source
/// wins, and it wins <b>whole</b>: the encrypted build contributes nothing at all, not the classes the
/// source happens to lack.
/// </summary>
/// <remarks>
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
public static class SourceSupersedesEncrypted
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
    /// The libraries that must go now that <paramref name="arriving"/> is here: every encrypted copy
    /// of it when it is readable, or <paramref name="arriving"/> itself when it is encrypted and a
    /// readable copy is already loaded. Empty when nothing is superseded.
    /// </summary>
    /// <param name="arriving">The library being registered.</param>
    /// <param name="loaded">The libraries already registered, not including it.</param>
    public static IReadOnlyList<LoadedLibrary> Retires(LoadedLibrary arriving, IEnumerable<LoadedLibrary> loaded)
    {
        if (IsEncrypted(arriving))
        {
            return loaded.Any(l => !IsEncrypted(l) && SameLibrary(l.Name, arriving.Name))
                ? [arriving]
                : [];
        }

        return loaded.Where(l => IsEncrypted(l) && SameLibrary(l.Name, arriving.Name)).ToList();
    }

    private static bool IsEncrypted(LoadedLibrary library) =>
        library.SourceType == LibrarySourceType.EncryptedDirectory;
}

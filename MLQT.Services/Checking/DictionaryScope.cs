using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Works out which repository's word list applies to a class.
///
/// <para>Every surface has to answer this the same way or the accepted spellings diverge again, one
/// level down from where they diverged before: the desktop app writing a word into one repository
/// while the CLI reads it from another is no better than the machine-wide list this replaced.</para>
/// </summary>
public static class DictionaryScope
{
    /// <summary>
    /// The repository root whose word list governs <paramref name="modelId"/>, or null when the class
    /// belongs to no repository — a library loaded on its own, or one recovered from a vendor's
    /// documentation. Null means there are no accepted words, not that some other list applies.
    /// </summary>
    public static string? RootForModel(
        ILibraryDataService libraries, IRepositoryService repositories, string modelId) =>
        RepositoryForModel(libraries, repositories, modelId)?.LocalPath;

    /// <summary>
    /// The repository a class belongs to, by the same rule as <see cref="RootForModel"/> — which is
    /// the point of it being here: the repository also carries the spell-check languages, and a
    /// caller that resolved those separately could end up using one repository's languages against
    /// another's words.
    /// </summary>
    public static Repository? RepositoryForModel(
        ILibraryDataService libraries, IRepositoryService repositories, string modelId)
    {
        // A class can be in more than one loaded library — a library checked out in a repository and
        // the vendor's copy of the same library loaded for reference. Take the first that resolves to
        // a repository rather than the first that contains the id: whichever happens to be loaded
        // first should not decide whether the user can accept a word.
        foreach (var library in libraries.Libraries)
        {
            if (!library.ModelIds.Contains(modelId))
                continue;

            var repository = RepositoryFor(repositories, library);
            if (repository is not null)
                return repository;
        }

        return null;
    }

    private static Repository? RepositoryFor(IRepositoryService repositories, LoadedLibrary library)
    {
        if (library.SourceType == LibrarySourceType.EncryptedDirectory)
            return null;   // reconstructed from documentation; never checked, never spelled

        if (library.RepositoryId is not { Length: > 0 } repositoryId)
            return null;

        var repository = repositories.GetRepository(repositoryId);
        return string.IsNullOrEmpty(repository?.LocalPath) ? null : repository;
    }

    /// <summary>
    /// The repository root a library's words live under.
    ///
    /// <para>A library loaded through a repository uses that repository's working copy, so several
    /// libraries in one checkout share one list — the list belongs to the repository, which is what
    /// gets committed. A library loaded on its own has no repository and therefore no list: writing
    /// one beside it would put words somewhere no CI run would ever look.</para>
    /// </summary>
    public static string? RootForLibrary(IRepositoryService repositories, LoadedLibrary library) =>
        RepositoryFor(repositories, library)?.LocalPath;
}

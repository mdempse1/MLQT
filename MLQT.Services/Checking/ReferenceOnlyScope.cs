using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Which classes belong to code the user has no say over.
///
/// <para>A repository marked reference only is loaded so that references into it resolve, and left
/// alone otherwise: not checked, not measured, not written to. Every surface asks here so that what
/// counts as the user's own code is decided once — a class reported on by one part of the app and
/// excluded by another is the sort of difference nobody can explain from the outside.</para>
/// </summary>
public static class ReferenceOnlyScope
{
    /// <summary>
    /// The ids of the classes that exist only in reference-only repositories. Empty when there are
    /// none, which is the usual case and costs nothing to ask about.
    ///
    /// <para>A class the user also has elsewhere is not one of them, however many reference copies of
    /// it are loaded. The same library really does turn up twice — a tool ships the encrypted build of
    /// a library the user has checked out as source — and the graph resolves that collision in favour
    /// of the readable source. Excluding the id because a vendor's copy also claims it would hide the
    /// user's own class from everything that asks here: it would go unchecked, unmeasured, and missing
    /// from the Coverage scope list, with a vendor's copy nobody can see as the only explanation.</para>
    /// </summary>
    public static HashSet<string> ModelIds(
        ILibraryDataService libraries, IRepositoryService repositories)
    {
        var referenceOnly = repositories.Repositories
            .Where(r => r.IsReferenceOnly)
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (referenceOnly.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var alsoTheUsers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var library in libraries.Libraries)
        {
            if (library.RepositoryId is { Length: > 0 } id && referenceOnly.Contains(id))
                excluded.UnionWith(library.ModelIds);
            else
                alsoTheUsers.UnionWith(library.ModelIds);
        }

        excluded.ExceptWith(alsoTheUsers);
        return excluded;
    }
}

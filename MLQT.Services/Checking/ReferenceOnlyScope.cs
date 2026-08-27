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
    /// The ids of every class in a reference-only repository. Empty when there are none, which is the
    /// usual case and costs nothing to ask about.
    /// </summary>
    public static HashSet<string> ModelIds(
        ILibraryDataService libraries, IRepositoryService repositories)
    {
        var excluded = repositories.Repositories
            .Where(r => r.IsReferenceOnly)
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (excluded.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        return libraries.Libraries
            .Where(l => l.RepositoryId is { Length: > 0 } id && excluded.Contains(id))
            .SelectMany(l => l.ModelIds)
            .ToHashSet(StringComparer.Ordinal);
    }
}

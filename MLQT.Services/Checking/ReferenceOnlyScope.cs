using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Which classes belong to code the user has no say over.
///
/// <para>Loaded so that references into it resolve, and left alone otherwise: not checked, not
/// measured, not written to. Every surface asks here so that what counts as the user's own code is
/// decided once — a class reported on by one part of the app and excluded by another is the sort of
/// difference nobody can explain from the outside.</para>
///
/// <para><b>Two ways of saying it, and both are asked here.</b> A repository can be marked
/// <see cref="Repository.IsReferenceOnly"/>, and a library can be loaded from
/// <b>Settings → Reference Libraries</b> with no repository at all
/// (<see cref="LoadedLibrary.IsReferenceOnly"/>). The second was not asked anywhere: an
/// <em>encrypted</em> reference library was covered by <c>ModelNode.IsExternalStub</c>, and a
/// <em>readable</em> one — which the reference folder holds by design, since a tool's library folder
/// ships MSL as source — fell through both, so the Metrics tab counted a vendor's library in its Size
/// census, offered its packages as scopes and wrote a trend snapshot about it. Adding a third
/// mechanism without asking who consumes the existing ones is how the last three of these
/// happened.</para>
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

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var alsoTheUsers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var library in libraries.Libraries)
        {
            if (IsReference(library, referenceOnly))
                excluded.UnionWith(library.ModelIds);
            else
                alsoTheUsers.UnionWith(library.ModelIds);
        }

        excluded.ExceptWith(alsoTheUsers);
        return excluded;
    }

    /// <summary>
    /// Whether a library is loaded only for reference — by its own flag, or by the repository it came
    /// from. Public because the question is also asked per library rather than per class: the metrics
    /// history is written per library, and a snapshot describing a vendor's code belongs in no file.
    /// </summary>
    public static bool IsReference(LoadedLibrary library, IRepositoryService repositories) =>
        IsReference(library, repositories.Repositories
            .Where(r => r.IsReferenceOnly)
            .Select(r => r.Id)
            .ToHashSet(StringComparer.Ordinal));

    private static bool IsReference(LoadedLibrary library, IReadOnlySet<string> referenceOnlyRepositoryIds) =>
        library.IsReferenceOnly
        || (library.RepositoryId is { Length: > 0 } id && referenceOnlyRepositoryIds.Contains(id));
}

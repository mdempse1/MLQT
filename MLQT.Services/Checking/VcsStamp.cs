using RevisionControl;
using RevisionControl.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// The revision a generated file was produced from, so a baseline or a metrics point can be matched
/// back to a commit. Both fields are null when the path is not inside a working copy (a plain
/// directory of <c>.mo</c> files, an extracted archive, a shallow CI checkout with no VCS metadata),
/// which is not an error — the file is still valid, it just cannot name a revision.
/// </summary>
public sealed record VcsStamp(string? Revision, string? Branch)
{
    public static readonly VcsStamp None = new(null, null);

    public bool IsKnown => Revision is not null;
}

/// <summary>
/// Locates the version control system a path sits in. Selection is "first system whose working-copy
/// root contains the path", Git before SVN.
///
/// <para>This is <b>the</b> rule, not a copy of one. It used to describe itself as "the same rule
/// <c>RepositoryService</c> uses" while being a second implementation of it — and the two did not
/// quite agree on the order of the walk-up and the validity check. A comment is not a mechanism;
/// <c>RepositoryService</c> now calls this, passing its own systems so its tests keep their
/// substitutes, and there is one answer to which VCS owns a directory.</para>
/// </summary>
public static class VcsLocator
{
    /// <summary>The VCS owning <paramref name="path"/> and its working-copy root, or (null, null).</summary>
    public static (IRevisionControlSystem? Vcs, string? Root) Find(string path) =>
        Find(path, new GitRevisionControlSystem(), new SvnRevisionControlSystem());

    /// <summary>
    /// The same, over systems the caller already holds. <c>RepositoryService</c> keeps one Git and one
    /// SVN instance for the life of the app and hands them here rather than paying for new ones per
    /// call — and so that a test can pass substitutes.
    /// </summary>
    /// <param name="systems">Tried in order, so the first is preferred where a path could be both.</param>
    public static (IRevisionControlSystem? Vcs, string? Root) Find(
        string path, params IRevisionControlSystem[] systems)
    {
        foreach (var system in systems)
        {
            var candidate = system.FindRepositoryRoot(path);
            if (candidate is not null && system.IsValidRepository(candidate))
                return (system, candidate);
        }

        return (null, null);
    }

    /// <summary>
    /// The current revision and branch for <paramref name="path"/>. Never throws: a VCS that is
    /// present but unreadable (no permissions, a corrupt index) yields <see cref="VcsStamp.None"/>
    /// rather than failing the command that only wanted to annotate its output.
    /// </summary>
    public static VcsStamp Stamp(string path)
    {
        try
        {
            var (vcs, root) = Find(path);
            if (vcs is null || root is null)
                return VcsStamp.None;

            return new VcsStamp(vcs.GetCurrentRevision(root), vcs.GetCurrentBranch(root));
        }
        catch
        {
            return VcsStamp.None;
        }
    }
}

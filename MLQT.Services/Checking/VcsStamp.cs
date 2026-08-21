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
/// root contains the path" — the same rule <c>RepositoryService</c> uses, kept here so the headless
/// surfaces (baseline stamping, metrics recording, changed-model resolution) all agree on which VCS
/// owns a directory.
/// </summary>
public static class VcsLocator
{
    /// <summary>The VCS owning <paramref name="path"/> and its working-copy root, or (null, null).</summary>
    public static (IRevisionControlSystem? Vcs, string? Root) Find(string path)
    {
        IRevisionControlSystem[] systems = [new GitRevisionControlSystem(), new SvnRevisionControlSystem()];

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

using MLQT.Services.DataTypes;
using RevisionControl;

namespace MLQT.Services.Checking;

/// <summary>
/// What a VCS operation left behind: which classes need re-analysing, and which files may be
/// reformatted.
/// </summary>
/// <param name="AffectedModelIds">Classes to re-analyse.</param>
/// <param name="ChangedFilePaths">
/// Files the formatter may rewrite. Deliberately **not** the same set as
/// <paramref name="AffectedModelIds"/> — see <see cref="VcsChangeResolver"/>.
/// </param>
/// <param name="Source">Which of the three sources answered.</param>
public sealed record VcsChangeSet(
    IReadOnlySet<string> AffectedModelIds,
    IReadOnlySet<string> ChangedFilePaths,
    VcsChangeSource Source);

/// <summary>Where a <see cref="VcsChangeSet"/>'s answer came from.</summary>
public enum VcsChangeSource
{
    /// <summary>Nothing changed and nothing needs doing.</summary>
    Nothing,

    /// <summary>The file monitor's pending changes — the most precise answer.</summary>
    PendingChanges,

    /// <summary>The VCS's own status, because the monitor was paused before the operation.</summary>
    VcsStatus,

    /// <summary>
    /// Neither could say, so every class in the repository is re-analysed and nothing is formatted.
    /// </summary>
    WholeRepository,
}

/// <summary>
/// The fallback chain that decides what a VCS operation affected.
/// </summary>
/// <remarks>
/// <para>Lifted out of <c>MainLayout.OnVcsFilesChanged</c> in phase 7a-4. It runs after every pull,
/// switch, merge, revert and update, and it is load-bearing in a way that is easy to miss: the three
/// sources are tried in order of precision, and <b>the last one deliberately reports no changed
/// files at all</b>.</para>
///
/// <para>That last point is the reason this is worth its own type. When neither the monitor nor the
/// VCS can say what changed — after a branch switch, where every file is replaced — every class is
/// re-analysed, but the formatter is given nothing, so it does not rewrite the entire working copy
/// on the strength of not knowing. A caller that treated "affected" and "changed" as one set would
/// reformat a whole repository after every branch switch.</para>
/// </remarks>
public static class VcsChangeResolver
{
    /// <summary>
    /// Resolves what a VCS operation affected, most precise source first.
    /// </summary>
    /// <param name="pendingChanges">
    /// Changes the file monitor accumulated before it was paused. Available when the operation ran
    /// inside a dialog, where there is no chance to pause the monitor beforehand.
    /// </param>
    /// <param name="vcsModifiedPaths">
    /// The VCS's own list of modified files, consulted only when the monitor had nothing.
    /// </param>
    /// <param name="allRepositoryModelIds">Every class in the repository — the last resort.</param>
    /// <param name="modelIdsInFile">The classes a file holds, as the loaded graph knows it.</param>
    public static VcsChangeSet Resolve(
        IEnumerable<FileChangeInfo> pendingChanges,
        Func<IEnumerable<string>> vcsModifiedPaths,
        Func<IEnumerable<string>> allRepositoryModelIds,
        Func<string, IEnumerable<string>> modelIdsInFile)
    {
        var affected = new HashSet<string>(StringComparer.Ordinal);
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in pendingChanges.Where(c => c.IsModelicaFile))
        {
            // A deleted file's classes are already gone from the graph by the time this runs, so
            // there is nothing to re-analyse and nothing to format.
            if (change.ChangeType == FileChangeType.Deleted)
                continue;

            var models = modelIdsInFile(change.FilePath).ToList();

            // Only files that are part of a loaded library. The monitor watches the whole VCS root,
            // so a change outside the library is real for VCS purposes and not MLQT's to format.
            if (models.Count > 0)
                changedFiles.Add(change.FilePath);

            foreach (var modelId in models)
                affected.Add(modelId);
        }

        if (affected.Count > 0)
            return new VcsChangeSet(affected, changedFiles, VcsChangeSource.PendingChanges);

        foreach (var filePath in vcsModifiedPaths())
        {
            changedFiles.Add(filePath);
            foreach (var modelId in modelIdsInFile(filePath))
                affected.Add(modelId);
        }

        if (affected.Count > 0)
            return new VcsChangeSet(affected, changedFiles, VcsChangeSource.VcsStatus);

        // Neither source could say. Re-analyse everything, and add nothing further to the formatter's
        // list — see the remarks above.
        //
        // Note what is *not* done here: any paths the VCS did report are left in changedFiles, even
        // though its models did not resolve. That is how MainLayout has always behaved, and it is
        // preserved deliberately rather than tidied — a file the VCS says changed but the graph does
        // not know is one the formatter finds nothing in, so the two spellings agree in practice,
        // and a refactor is the wrong place to find out otherwise.
        foreach (var modelId in allRepositoryModelIds())
            affected.Add(modelId);

        return affected.Count == 0
            ? new VcsChangeSet(affected, changedFiles, VcsChangeSource.Nothing)
            : new VcsChangeSet(affected, changedFiles, VcsChangeSource.WholeRepository);
    }

    /// <summary>
    /// The Modelica files a VCS status report names that the formatter may actually rewrite.
    /// </summary>
    /// <remarks>
    /// <para>Four narrowings, each for its own reason, and each easy to drop without noticing:</para>
    /// <list type="bullet">
    /// <item>A deleted file is not there to format.</item>
    /// <item>VCS paths are relative to the <em>VCS root</em>, which can be a parent of the library —
    /// one working copy can hold several libraries, and a sibling's files are not this repository's
    /// to touch.</item>
    /// <item>Only <c>.mo</c> files. A working copy holds scripts, resources and documentation, and
    /// the formatter would make nonsense of any of them.</item>
    /// <item>The file has to exist. A rename reports the old path too, and a moved-away file is
    /// reported as changed by some clients.</item>
    /// </list>
    /// </remarks>
    /// <param name="fileExists">Injected so the rule can be exercised without a working copy.</param>
    public static HashSet<string> FormattableModelicaFiles(
        string localPath,
        string vcsRootPath,
        IEnumerable<VcsWorkingCopyFile> changes,
        Func<string, bool> fileExists)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(localPath))
            return paths;

        foreach (var change in changes.Where(c => c.Status != VcsFileStatus.Deleted))
        {
            var fullPath = Path.Combine(vcsRootPath, change.Path);
            if (fullPath.StartsWith(localPath, StringComparison.OrdinalIgnoreCase)
                && fullPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                && fileExists(fullPath))
            {
                paths.Add(fullPath);
            }
        }

        return paths;
    }
}

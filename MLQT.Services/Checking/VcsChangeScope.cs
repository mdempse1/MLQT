using ModelicaGraph;
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

/// <summary>What re-reading a repository's settings after a VCS operation found.</summary>
/// <param name="Outcome">What <see cref="Interfaces.IRepositoryService.ReloadRepositorySettings"/> said.</param>
/// <param name="ChecksChanged">Whether the settings the operation brought would check a class
/// differently, so that every class in the repository has to be checked again.</param>
public sealed record VcsSettingsReload(SettingsReloadOutcome Outcome, bool ChecksChanged);

/// <summary>
/// Re-reads a repository's committed settings after a VCS operation (B525).
/// </summary>
/// <remarks>
/// The desktop app read <c>.mlqt/settings.json</c> when it loaded the project and never again, so a
/// pull that brought the team's new rules left it checking and formatting with the old ones until
/// the project was reloaded. The analysis pipeline after each operation asks this first, so the
/// files it formats get the new rules and a change of rules re-checks every class.
///
/// <para>A change to the formatting rules alone does not reformat the repository: whoever changed
/// them committed files already laid out under them, and rewriting a working copy on the strength of
/// a pull is what the pipeline exists not to do (see <see cref="VcsChangeResolver"/>). The files the
/// operation changed are formatted with the new rules, as every changed file is.</para>
/// </remarks>
public static class VcsSettingsReloader
{
    /// <summary>Re-reads <paramref name="repositoryId"/>'s settings and says what changed.</summary>
    public static VcsSettingsReload Reload(Interfaces.IRepositoryService repositories, string repositoryId)
    {
        var before = repositories.GetRepository(repositoryId)?.StyleSettings ?? new StyleCheckingSettings();
        var outcome = repositories.ReloadRepositorySettings(repositoryId);
        if (outcome != SettingsReloadOutcome.Changed)
            return new VcsSettingsReload(outcome, false);

        var after = repositories.GetRepository(repositoryId)?.StyleSettings ?? new StyleCheckingSettings();
        return new VcsSettingsReload(outcome, after.ChecksDifferFrom(before));
    }
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
            var fullPath = OnDisk(vcsRootPath, change.Path);
            // Separator-aware: wc/Lib does not contain wc/LibExtra, whose files are not ours (B323).
            if (PathContainment.IsWithin(fullPath, localPath)
                && fullPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                && fileExists(fullPath))
            {
                paths.Add(fullPath);
            }
        }

        return paths;
    }

    /// <summary>
    /// The files a VCS status report says are scheduled for addition, as full paths in the
    /// platform's own form - the form a path read from the graph or the disk has, so the two can be
    /// compared as strings.
    /// </summary>
    /// <remarks>
    /// What a full-library save must not delete: a file a merge added that the formatter did not
    /// write is still owed to the commit. The paths are relative to the <em>VCS root</em>, and
    /// forward-slashed from Git and SVN alike (B472); joined to the library path, or joined without
    /// normalising, they named no file the save had read and protected nothing.
    /// </remarks>
    public static HashSet<string> ScheduledForAddition(
        string vcsRootPath, IEnumerable<VcsWorkingCopyFile> changes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(vcsRootPath))
            return paths;

        foreach (var change in changes.Where(c => c.Status == VcsFileStatus.Added))
            paths.Add(OnDisk(vcsRootPath, change.Path));

        return paths;
    }

    /// <summary>
    /// A VCS-relative path as a full path in the platform's form. A VCS reports <c>/</c> on every
    /// platform, and <c>C:\wc\Lib/Thing.mo</c> is the same file as <c>C:\wc\Lib\Thing.mo</c> to the
    /// filesystem but not to a set of strings.
    /// </summary>
    private static string OnDisk(string vcsRootPath, string relativePath) =>
        Path.GetFullPath(Path.Combine(vcsRootPath, relativePath));
}

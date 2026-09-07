using System.IO;
using RevisionControl;

namespace MLQT.Shared.Dialogs;

/// <summary>
/// How far the user has got with one conflicted file.
/// </summary>
/// <remarks>
/// <c>EditingExternally</c> is not a VCS state. It is the dialog remembering that the user opened
/// the file in their own editor, so the row can offer "I have fixed it" instead of the three
/// resolution choices — the tool cannot tell an externally-edited file from an untouched one.
/// </remarks>
public enum ConflictFileState
{
    Unresolved,
    EditingExternally,
    Resolved,
}

/// <summary>
/// The rules the three long-running VCS dialogs share: which working-copy changes block the
/// operation, how the conflict list is carried across rounds, and when the user may commit.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> <c>GitMergeBranchDialog</c>, <c>MergeBranchDialog</c> (SVN) and
/// <c>GitRebaseDialog</c> are three separate dialogs because the operations differ, but they ask the
/// same questions and each answered them for itself — the blocking-status list, the conflict map, the
/// relative path and the status glyphs were <b>byte-identical</b> in all three. That is the shape
/// this backlog has named repeatedly: one rule, several implementations, and a promise that they
/// agree held by nothing. It cost something real here: see B106 on the status the three copies all
/// left out.</para>
///
/// <para>The glyphs are not here — they went to <see cref="Helpers.VcsStatusHelper"/>, which already
/// held the same table for the browser and the change list, and held it <i>more completely</i>. The
/// dialogs' private copies had drifted from it: an untracked file was a grey question mark in a merge
/// dialog and a green "new" badge everywhere else, and a rename had no glyph at all.</para>
/// </remarks>
public static class VcsConflictRules
{
    /// <summary>
    /// The working-copy changes that must be dealt with before a merge or rebase may start.
    /// </summary>
    /// <remarks>
    /// <para>Untracked files block on purpose. Neither git nor svn refuses to merge over them, but a
    /// merge that wants to create a file the user already has sitting there untracked fails partway
    /// through with a message about an untracked working tree file, which is a much worse place to
    /// find out. The dialog asks first.</para>
    ///
    /// <para><b>Renamed blocks too, which it did not before (B106).</b> All three dialogs listed the
    /// other five statuses and omitted this one, so a staged <c>git mv</c> — a change git will not
    /// rebase over — reported a clean working copy, and the dialog offered to start an operation git
    /// then refused. <c>GetWorkingCopyChanges</c> returns only files with real changes (it drops
    /// <c>Unaltered</c> and <c>Ignored</c>), so every status it can produce belongs here; the list is
    /// written out rather than made unconditional so that a status added later has to be considered
    /// rather than inherited, and a test over <see cref="VcsFileStatus"/> enforces that.</para>
    /// </remarks>
    public static List<VcsWorkingCopyFile> BlockingChanges(IEnumerable<VcsWorkingCopyFile> changes) =>
        [.. changes.Where(f => f.Status is
            VcsFileStatus.Modified or VcsFileStatus.Added or
            VcsFileStatus.Deleted or VcsFileStatus.Renamed or
            VcsFileStatus.Untracked or VcsFileStatus.Conflicted)];

    /// <summary>
    /// The conflict map for a new round, keeping whatever the user has already resolved.
    /// </summary>
    /// <remarks>
    /// <para>A rebase replays commits one at a time and reports a fresh conflict list per commit, so
    /// this is called repeatedly. Rebuilding the map from scratch each time would reset a file the
    /// user resolved in an earlier round back to <see cref="ConflictFileState.Unresolved"/>, and the
    /// "N of M resolved" counter with it.</para>
    ///
    /// <para>Files no longer in conflict drop out rather than lingering as resolved rows.</para>
    /// </remarks>
    public static Dictionary<string, ConflictFileState> CarryForward(
        IEnumerable<string> conflictedFiles,
        IReadOnlyDictionary<string, ConflictFileState> existing)
    {
        var updated = new Dictionary<string, ConflictFileState>();

        foreach (var path in conflictedFiles)
            updated[path] = existing.TryGetValue(path, out var state) ? state : ConflictFileState.Unresolved;

        return updated;
    }

    /// <summary>
    /// Whether every conflict has been resolved — and there was at least one.
    /// </summary>
    /// <remarks>
    /// The count check is the point. `All` on an empty dictionary is true, so without it the commit
    /// button would enable itself before the conflict list had loaded.
    /// </remarks>
    public static bool AllResolved(IReadOnlyDictionary<string, ConflictFileState> states) =>
        states.Count > 0 && states.Values.All(s => s == ConflictFileState.Resolved);

    /// <summary>
    /// A conflicted file's path as the user recognises it: relative to the working copy.
    /// </summary>
    /// <remarks>
    /// Falls back to the full path rather than throwing. A path that cannot be related to the
    /// working copy at all is still worth showing — an absolute path in the list is odd, an
    /// exception mid-merge is a great deal worse.
    /// </remarks>
    public static string RelativeTo(string? localPath, string filePath)
    {
        if (string.IsNullOrEmpty(localPath))
            return filePath;

        try
        {
            return Path.GetRelativePath(localPath, filePath);
        }
        catch
        {
            return filePath;
        }
    }
}

namespace MLQT.Services.Helpers;

/// <summary>
/// Which files a full library save has left behind, and which of those must survive anyway.
/// </summary>
/// <remarks>
/// <para>A full save rewrites a library's whole file layout: classes move between files, a package
/// becomes a directory, a directory collapses back to a file. Anything that was there before and was
/// not written again is an orphan — the old spelling of something that now lives elsewhere — and
/// leaving it behind means the library on disk holds two copies of the same class.</para>
///
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4. It decides what gets <b>deleted</b> from a
/// user's working copy, which is reason enough for it to be answerable on its own.</para>
/// </remarks>
public static class OrphanedFileSelector
{
    /// <summary>
    /// The files to delete after a save: everything that was there before and was not written
    /// again, less anything the VCS is holding as a new file.
    /// </summary>
    /// <param name="originalFiles">The <c>.mo</c> files present before the save.</param>
    /// <param name="originalOrderFiles">The <c>package.order</c> files present before the save.</param>
    /// <param name="writtenFiles">Everything the save wrote.</param>
    /// <param name="vcsAddedFiles">
    /// Files the VCS has scheduled for addition. A file added by an SVN or Git merge is scheduled
    /// for addition but may not be written by the formatter — a malformed within clause, or a class
    /// name that does not match its file. Deleting it leaves the working copy with a file the VCS is
    /// tracking and cannot find, and the next commit fails with "scheduled for addition, but is
    /// missing". The user's own merge is then stuck behind a file MLQT removed.
    /// </param>
    public static IReadOnlyList<string> SelectOrphans(
        IEnumerable<string> originalFiles,
        IEnumerable<string> originalOrderFiles,
        IReadOnlySet<string> writtenFiles,
        IReadOnlySet<string> vcsAddedFiles)
    {
        return originalFiles
            .Concat(originalOrderFiles)
            .Where(f => !writtenFiles.Contains(f) && !vcsAddedFiles.Contains(f))
            .ToList();
    }
}

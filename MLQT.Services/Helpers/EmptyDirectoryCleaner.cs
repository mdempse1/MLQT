using static MLQT.Services.LoggingService;

namespace MLQT.Services.Helpers;

/// <summary>
/// Removes directories the formatter emptied.
/// </summary>
/// <remarks>
/// <para>Saving a library can move classes between files — a class promoted out of a package, or
/// folded back into one — and that leaves directories behind with nothing in them. They are not
/// harmful, but a library tree full of empty folders is one a user has to read past, and they show
/// up in the VCS as clutter.</para>
///
/// <para>Lifted out of <c>MainLayout</c> in phase 7a-4. It deletes things, which is reason enough
/// for it to be somewhere a test can drive it: the rule that keeps it safe is that it never touches
/// a hidden directory, because <c>.git</c> and <c>.svn</c> are full of empty ones and removing them
/// corrupts the working copy.</para>
/// </remarks>
public static class EmptyDirectoryCleaner
{
    /// <summary>
    /// Deletes every empty directory beneath <paramref name="rootPath"/>, deepest first.
    /// </summary>
    /// <remarks>
    /// <para>Deepest first is what makes one pass enough: emptying a child directory leaves its
    /// parent empty, and a shallow-first walk would miss the parent until the next save.</para>
    ///
    /// <para><paramref name="rootPath"/> itself is never removed, however empty it becomes — it is
    /// the library the user opened.</para>
    /// </remarks>
    /// <returns>The directories that were removed.</returns>
    public static IReadOnlyList<string> RemoveEmptyDirectories(string? rootPath)
    {
        var removed = new List<string>();
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            return removed;

        try
        {
            var directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                .Where(d => !FileMonitoringServiceHelpers.IsInHiddenDirectory(d))
                .OrderByDescending(d => d.Count(c => c == Path.DirectorySeparatorChar
                                                  || c == Path.AltDirectorySeparatorChar))
                .ToList();

            foreach (var directory in directories)
            {
                try
                {
                    // A shortcut, not the safety net. What actually guarantees nothing with content
                    // in it is removed is that Directory.Delete is called without `recursive`, so it
                    // throws rather than taking the contents with it. This check is here to avoid
                    // one thrown exception per non-empty directory, which on a real library is most
                    // of them.
                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                        continue;

                    Directory.Delete(directory);
                    removed.Add(directory);
                    Debug(nameof(EmptyDirectoryCleaner), $"Deleted empty directory: {directory}");
                }
                catch (Exception ex)
                {
                    // One directory that will not go - held open, read-only, gone already - is not a
                    // reason to abandon the rest.
                    Debug(nameof(EmptyDirectoryCleaner), $"Could not delete directory {directory}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Warn(nameof(EmptyDirectoryCleaner), $"Error cleaning up empty directories under {rootPath}: {ex.Message}");
        }

        return removed;
    }
}

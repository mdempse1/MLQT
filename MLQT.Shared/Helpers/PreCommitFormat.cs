using System.Globalization;

namespace MLQT.Shared.Helpers;

/// <summary>
/// The decisions the format before a commit makes that need no file system to test: which changed
/// files it can skip, and what it tells the user while it works.
/// </summary>
internal static class PreCommitFormat
{
    /// <summary>
    /// The changed files known to be formatted: recorded by the formatting pipeline, and not written
    /// since, so their write time is still the one recorded.
    /// </summary>
    /// <remarks>
    /// Keyed by write time, not by the file monitor: an edit in another editor, an svn revert or an
    /// update gives the file a new write time, and it is checked again, whether or not the monitor saw
    /// the change. A file that has gone, or cannot be read, is never skipped.
    /// </remarks>
    internal static List<string> AlreadyFormatted(
        IEnumerable<string> changedFiles,
        IReadOnlyDictionary<string, DateTime> knownFormatted,
        Func<string, DateTime> writeTimeOf)
    {
        var skipped = new List<string>();
        foreach (var file in changedFiles)
        {
            if (!knownFormatted.TryGetValue(file, out var formattedAt))
                continue;

            DateTime now;
            try
            {
                now = writeTimeOf(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (now == formattedAt)
                skipped.Add(file);
        }

        return skipped;
    }

    /// <summary>What the progress dialog says while the changed files are formatted.</summary>
    internal static string FormattingMessage(int files) =>
        files == 1
            ? "Applying the formatting rules to 1 changed file"
            : string.Create(CultureInfo.InvariantCulture, $"Applying the formatting rules to {files:N0} changed files");
}

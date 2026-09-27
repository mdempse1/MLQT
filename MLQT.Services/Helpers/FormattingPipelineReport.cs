using ModelicaGraph;

namespace MLQT.Services.Helpers;

/// <summary>
/// What a host tells the user about a formatting pass, beyond "complete".
/// </summary>
public static class FormattingPipelineReport
{
    /// <summary>How many skipped files are named before the rest are counted.</summary>
    public const int NamedFiles = 5;

    /// <summary>
    /// The files Format All left as they were because they have syntax errors (B414), for a
    /// notification: each named relative to <paramref name="root"/> where it is inside it, the first
    /// <see cref="NamedFiles"/> of them, and the rest counted.
    /// </summary>
    /// <param name="skippedFiles">From <c>IFormattingPipeline.SaveAllLibrariesWithFormattingAsync</c>.</param>
    /// <param name="root">The repository's directory, or null to name each file in full.</param>
    public static string SkippedForSyntaxErrors(IReadOnlyList<string> skippedFiles, string? root)
    {
        var names = skippedFiles
            .Take(NamedFiles)
            .Select(f => root is not null && PathContainment.IsWithin(f, root) ? Path.GetRelativePath(root, f) : f);

        var more = skippedFiles.Count > NamedFiles
            ? $" and {skippedFiles.Count - NamedFiles} more"
            : "";

        return $"{skippedFiles.Count} file(s) were not formatted because they have syntax errors, and were "
            + $"left exactly as they were: {string.Join(", ", names)}{more}. Fix the errors (Code Review "
            + "lists them) and run Format All Files again.";
    }
}

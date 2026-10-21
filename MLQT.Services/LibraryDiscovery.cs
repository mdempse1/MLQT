using ModelicaParser.Helpers;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <summary>
/// Discovers the Modelica library roots under a repository path, matching how the MLQT UI
/// (via <c>RepositoryService</c>) treats a repository: a single <c>.mlqt/settings.json</c> at the
/// repository root applies to every library found here.
///
/// Rules (identical to the UI):
/// <list type="bullet">
/// <item>The path is a single <c>.mo</c> file → that file.</item>
/// <item>The directory contains a <c>package.mo</c> or a <c>package.moe</c> → the directory itself
///   is the one library (its sub-packages are loaded as part of it).</item>
/// <item>Otherwise → each immediate, non-hidden subdirectory that contains a <c>package.mo</c> or
///   a <c>package.moe</c>, plus each loose top-level <c>.mo</c> file.</item>
/// </list>
///
/// <para>A <c>package.moe</c> marks an encrypted library, which has no readable source at all.
/// It is discovered here so that pointing at a directory of installed libraries finds every
/// library under it, encrypted or not; what can then be done with it is decided by the loader,
/// not here.</para>
/// </summary>
public static class LibraryDiscovery
{
    /// <summary>
    /// What was looked for, in the user's words, for when nothing was found.
    ///
    /// <para>Kept beside the rules it describes so the two cannot drift: a message listing what MLQT
    /// searches for is only useful while it is still what MLQT searches for. Adding a repository that
    /// yields no library used to be silent apart from a log line — the dialog closed reporting
    /// success and nothing appeared — which is indistinguishable from a library that failed to load
    /// (B206, and the reason B198 could not be told apart from either).</para>
    /// </summary>
    public static string NothingFoundIn(string path) =>
        $"No Modelica library was found in '{path}'. MLQT looks for a package.mo or package.moe in " +
        "the folder itself, or in any immediate subfolder that is not hidden, and for loose .mo files " +
        "at the top level.";

    public static IReadOnlyList<string> DiscoverLibraryPaths(string basePath)
    {
        var results = new List<string>();

        if (File.Exists(basePath) && basePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(basePath);
            return results;
        }

        if (!Directory.Exists(basePath))
            return results;

        if (IsLibraryRoot(basePath))
        {
            results.Add(basePath);
            return results;
        }

        foreach (var subDir in Directory.GetDirectories(basePath))
        {
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.')) // skip .git, .svn, .mlqt, etc.
                continue;
            if (IsLibraryRoot(subDir))
                results.Add(subDir);
        }

        foreach (var file in Directory.GetFiles(basePath, "*.mo", SearchOption.TopDirectoryOnly))
            results.Add(file);

        return results;
    }

    /// <summary>
    /// The name a library declares, read from its files: an encrypted library's from its versioned
    /// directory name or <c>libraryinfo.mos</c> (<see cref="EncryptedLibraryDetector"/>), since its
    /// package cannot be read; otherwise the outermost class in its <c>package.mo</c>, or in the
    /// <c>.mo</c> file that is the library. Null when there is nothing to read one from.
    ///
    /// <para>Never the folder name. That is usually the same word, which is why reading it instead went
    /// unnoticed twice: it is wrong exactly when the folder is not named after the library - a
    /// repository checked out under its own name (B204), a second copy beside the first.</para>
    /// </summary>
    /// <param name="libraryPath">A library root as <see cref="DiscoverLibraryPaths"/> returns one: a
    /// directory holding a <c>package.mo</c> or <c>package.moe</c>, or a single <c>.mo</c> file.</param>
    public static string? ReadLibraryName(string libraryPath)
    {
        if (EncryptedLibraryDetector.Detect(libraryPath)?.Name is { } encryptedName)
            return encryptedName;

        var packageMoPath = File.Exists(libraryPath) ? libraryPath : Path.Combine(libraryPath, "package.mo");
        if (!File.Exists(packageMoPath))
            return null;

        try
        {
            var content = ModelicaFileEncoding.ReadAllTextOnly(packageMoPath);
            // The outermost class has no parent, and "no parent" is the empty string, not null (B204).
            return ModelicaParserHelper.ExtractModels(content)
                .FirstOrDefault(m => string.IsNullOrEmpty(m.ParentModelName))?.Name;
        }
        catch (Exception ex)
        {
            Warn("LibraryDiscovery", $"Failed to extract library name from {packageMoPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether a directory is the root of a Modelica library — one holding readable source, or an
    /// encrypted one whose classes can only be recovered from its documentation.
    /// </summary>
    private static bool IsLibraryRoot(string directory) =>
        File.Exists(Path.Combine(directory, "package.mo")) ||
        EncryptedLibraryDetector.IsEncryptedLibraryRoot(directory);
}

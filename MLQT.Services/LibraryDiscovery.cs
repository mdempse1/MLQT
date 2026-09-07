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
    /// Whether a directory is the root of a Modelica library — one holding readable source, or an
    /// encrypted one whose classes can only be recovered from its documentation.
    /// </summary>
    private static bool IsLibraryRoot(string directory) =>
        File.Exists(Path.Combine(directory, "package.mo")) ||
        EncryptedLibraryDetector.IsEncryptedLibraryRoot(directory);
}

namespace MLQT.Cli;

/// <summary>
/// Resolves user-supplied file paths (`--config`, `--baseline`) against the library/repository
/// being checked: absolute paths are used as-is; a relative path is resolved against the library
/// path (its directory if the library path is a file), NOT the current working directory. This
/// keeps config/baseline anchored to the repo — `--baseline .mlqt/baseline.json` finds
/// `&lt;repo&gt;/.mlqt/baseline.json` from any directory, which is what CI wants.
/// </summary>
internal static class RepoPath
{
    public static string Resolve(string libraryPath, string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        var baseDir = Directory.Exists(libraryPath)
            ? libraryPath
            : Path.GetDirectoryName(libraryPath) ?? libraryPath;

        return Path.Combine(baseDir, path);
    }
}

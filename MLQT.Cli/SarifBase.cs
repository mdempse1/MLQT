namespace MLQT.Cli;

/// <summary>
/// Resolves and checks the directory SARIF file URIs are written relative to.
///
/// <para>SARIF locations are relative paths, and whoever reads them resolves them against a root of
/// their own choosing — for GitHub code scanning, the root of the checked-out repository. MLQT writes
/// them relative to the library it checked, which is the same directory only when the library *is*
/// the repository. For the common layout, a library in a subdirectory, every annotation then names a
/// path that does not exist from the repository root, and GitHub silently attaches them to
/// nothing.</para>
/// </summary>
internal static class SarifBase
{
    /// <summary>
    /// Resolves a user-supplied base directory, or reports why it cannot be used.
    ///
    /// <para>The library has to sit inside the base: a path that has to climb out of it would be
    /// written as <c>../…</c>, which GitHub rejects — the same silent non-attachment the option
    /// exists to prevent, arrived at from the other direction. Better to say so before the check
    /// runs than after several minutes of work.</para>
    /// </summary>
    public static bool TryResolve(
        string libraryPath, string requested, out string? resolved, out string? error)
    {
        resolved = null;
        error = null;

        var basePath = Path.GetFullPath(RepoPath.Resolve(libraryPath, requested));

        if (File.Exists(basePath))
        {
            error = $"--sarif-base must be a directory, not a file: {basePath}";
            return false;
        }

        if (!Directory.Exists(basePath))
        {
            error = $"--sarif-base directory not found: {basePath}";
            return false;
        }

        var libraryFull = Path.GetFullPath(libraryPath);
        if (!Contains(basePath, libraryFull))
        {
            error = $"--sarif-base '{basePath}' does not contain the library '{libraryFull}', so the " +
                    "file paths in the report would have to point outside it";
            return false;
        }

        resolved = basePath;
        return true;
    }

    /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or sits beneath it.</summary>
    private static bool Contains(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);

        // A path on another drive comes back rooted; one that has to climb out starts with "..".
        return !Path.IsPathRooted(relative)
               && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith("../", StringComparison.Ordinal);
    }
}

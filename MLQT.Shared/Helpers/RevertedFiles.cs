namespace MLQT.Shared.Helpers;

/// <summary>
/// What a revert gives the graph to reload: the reverted files that bear on it.
/// </summary>
internal static class RevertedFiles
{
    /// <summary>
    /// The full paths of the reverted <c>.mo</c> and <c>package.order</c> files, each once. Other
    /// files - resources, documentation - are nothing the graph holds. A file the revert deleted (a
    /// formerly added one) is kept: reloading it is what removes its classes.
    /// </summary>
    internal static List<string> ToReload(IEnumerable<string> revertedRelativePaths, string vcsRootPath) =>
        revertedRelativePaths
            .Where(p => p.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(p).Equals("package.order", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFullPath(Path.Combine(vcsRootPath, p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

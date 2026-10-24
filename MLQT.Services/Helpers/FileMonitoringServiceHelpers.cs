using ModelicaGraph;

namespace MLQT.Services.Helpers;

/// <summary>
/// Helper methods for the file monitoring service.
/// </summary>
public static class FileMonitoringServiceHelpers
{
    /// <summary>
    /// The folders a version control system or MLQT keeps beside the code, recognised by name wherever
    /// they are.
    /// </summary>
    private static readonly string[] MetadataFolders = [".git", ".svn", ".hg", ".bzr", ".mlqt"];

    /// <summary>
    /// Checks if a path is inside a version control or MLQT metadata folder (<c>.git</c>, <c>.svn</c>,
    /// <c>.hg</c>, <c>.bzr</c>, <c>.mlqt</c>), or is itself a hidden file.
    /// This is a public static method so it can be used by other services as a safeguard.
    /// </summary>
    /// <remarks>
    /// Without a root there is no telling a hidden folder inside a library from one the library merely
    /// sits under, so only the metadata folders are recognised. Until 2026-10-24 any segment starting
    /// with a dot counted, and a library under <c>~/.local/share</c> or a <c>.tmp</c> folder was then
    /// "hidden" whole: a class <c>mlqt_write_class</c> wrote there never reached the graph. Use the
    /// overload taking a root wherever the library, repository or watched folder is known.
    /// </remarks>
    public static bool IsInHiddenDirectory(string path)
    {
        var parts = Segments(path);
        if (parts.Length == 0)
            return false;

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return parts.Any(part => MetadataFolders.Any(folder => part.Equals(folder, comparison)))
               || IsHiddenName(parts[^1]);
    }

    /// <summary>
    /// Checks if a path is hidden below <paramref name="root"/>: whether any folder or file between the
    /// root and the path starts with a dot (<c>.git</c>, <c>.svn</c>, <c>.vscode</c>, ...). Folders
    /// above the root are not looked at, so a library under a dot folder is not hidden.
    /// </summary>
    /// <remarks>
    /// A path outside <paramref name="root"/>, or no root, is answered as
    /// <see cref="IsInHiddenDirectory(string)"/> answers it.
    /// </remarks>
    public static bool IsInHiddenDirectory(string path, string? root)
    {
        if (string.IsNullOrEmpty(root) || !PathContainment.IsWithin(path, root))
            return IsInHiddenDirectory(path);

        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return Segments(relative).Any(IsHiddenName);
    }

    private static string[] Segments(string path) =>
        path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != "." && part != "..")
            .ToArray();

    private static bool IsHiddenName(string name) => name.StartsWith('.');
}

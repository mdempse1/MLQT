using MLQT.McpServer.Dtos;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Filesystem-inferred writability. The server does not track a "read-only library" flag; instead a
/// file is editable iff the OS lets this process write it. This naturally scopes edits: a user's own
/// library is writable, whereas reference libraries installed under Program Files (e.g. the Modelica
/// Standard Library shipped with Dymola) typically require admin rights and so are treated as
/// read-only. Multi-file operations pre-flight every target so they stay all-or-nothing.
/// </summary>
internal static class FileWritability
{
    /// <summary>
    /// True if this process can write <paramref name="path"/>. For an existing file: it must not have
    /// the read-only attribute and must open for write (catches ACL denials). For a not-yet-existing
    /// file: its containing directory must exist and accept a new file.
    /// </summary>
    public static bool IsWritable(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                    return false;
                // Probe real write access (ACLs) without altering the file's content.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) && IsDirectoryWritable(dir);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        catch (Exception) { return false; }
    }

    private static bool IsDirectoryWritable(string dir)
    {
        var probe = Path.Combine(dir, $".mlqt-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Guard for a single write. Returns null when writable, or a state-aware <see cref="ToolError"/>.
    /// </summary>
    public static ToolError? RequireWritable(string path, string operation)
        => PreflightWritable(new[] { path }, operation);

    /// <summary>
    /// Guard for a multi-file operation: returns null only when EVERY path is writable, otherwise a
    /// <see cref="ToolError"/> listing the blocked files so the caller can leave the change unmade.
    /// </summary>
    public static ToolError? PreflightWritable(IEnumerable<string> paths, string operation)
    {
        var blocked = paths.Where(p => !IsWritable(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (blocked.Count == 0)
            return null;

        var shown = string.Join(", ", blocked.Take(10));
        var more = blocked.Count > 10 ? $" (+{blocked.Count - 10} more)" : "";
        return new ToolError(
            $"Cannot {operation}: {blocked.Count} file(s) are read-only or outside your write permissions " +
            "(for example a reference library installed under Program Files, which needs admin rights to " +
            $"modify). No files were changed. Blocked: {shown}{more}. Edit a writable copy of that library " +
            "instead.");
    }
}

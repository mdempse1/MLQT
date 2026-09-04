using ModelicaGraph;
using MLQT.McpServer.Dtos;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Whether a write may go ahead. Two questions, and the second is not a matter of permissions.
///
/// <para><b>Filesystem writability.</b> A file is editable iff the OS lets this process write it,
/// which naturally scopes most edits: a user's own library is writable, whereas a reference library
/// installed under <c>Program Files</c> typically needs admin rights and is therefore read-only.
/// Multi-file operations pre-flight every target so they stay all-or-nothing.</para>
///
/// <para><b>An encrypted package is refused whatever the permissions say.</b> A class recovered from
/// a vendor's documentation has a file node pointing at the <c>package.moe</c> it came from — that
/// being the honest answer to where it lives — so an edit tool taking the class's file path at face
/// value would write a page of synthesized Modelica over an encrypted binary. Permissions do not
/// stop it: they only happen to, when the library sits somewhere the user cannot write, and a
/// library installed in a home directory or on a share sits somewhere they can. The encrypted-library
/// design note calls this "the highest-severity failure mode" and asks for a refusal rather than a
/// skip, which is what <c>ModelicaPackageSaver</c> does on the desktop side; this is the same refusal
/// on the path that does not go through it.</para>
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
        // Not a permissions question, but it is the honest answer to "can this be written", and it is
        // the one `get_class_info` reports: a stub whose library sits somewhere writable used to be
        // advertised as editable, which is an invitation to try.
        if (ExternalStubBuilder.IsEncryptedPackageFile(path))
            return false;

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
        var pathList = paths as IReadOnlyCollection<string> ?? paths.ToList();

        // Before permissions, because this one is not about permissions and its answer does not
        // change with where the library happens to be installed.
        var encrypted = pathList
            .Where(ExternalStubBuilder.IsEncryptedPackageFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (encrypted.Count > 0)
            return new ToolError(
                $"Cannot {operation}: {string.Join(", ", encrypted.Take(10))} is an encrypted Modelica " +
                "package. Its classes are reconstructions MLQT built from the vendor's documentation so " +
                "that references into the library resolve — there is no source to edit, and writing here " +
                "would destroy the package. No files were changed.");

        var blocked = pathList.Where(p => !IsWritable(p))
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

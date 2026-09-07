namespace MLQT.Services.Helpers;

/// <summary>
/// Whether MLQT could write beside a library if it wanted to.
///
/// <para>Used to offer the right default when a repository is added: a tool's library folder under
/// Program Files is code the user does not own, and MLQT would spend the session failing to write a
/// <c>.mlqt</c> directory into it. Asked by trying, because permissions on Windows are decided by
/// more than the path — an ACL, a read-only volume, a network share — and the only answer that
/// matters is whether the write succeeds.</para>
/// </summary>
public static class DirectoryWritability
{
    /// <summary>
    /// True if a file can be created in <paramref name="path"/>. False for a missing directory, a
    /// denied one, or anything else that stops the write — the caller is choosing a default, so an
    /// uncertain answer should be the cautious one.
    /// </summary>
    public static bool CanWriteInto(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var probe = Path.Combine(path, $".mlqt-write-probe-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception)
        {
            // Denied, read-only, offline, or a path the filesystem will not take: all "no".
            return false;
        }
    }
}

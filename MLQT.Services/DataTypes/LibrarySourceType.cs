namespace MLQT.Services.DataTypes;

/// <summary>
/// Type of source for a Modelica library.
/// </summary>
public enum LibrarySourceType
{
    File,
    Directory,
    Zip,
    Git,
    SVN,

    /// <summary>
    /// A directory holding an encrypted library — an unreadable <c>package.moe</c> plus the
    /// vendor's generated documentation. Its classes are reconstructed from that documentation and
    /// exist only to resolve references; the library is read-only and is never reported on.
    /// </summary>
    EncryptedDirectory
}

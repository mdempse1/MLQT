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
    EncryptedDirectory,

    /// <summary>
    /// Classes a host supplied from memory through an <see cref="ModelicaGraph.IReadOnlyClassSource"/>.
    /// Nothing of the library is on disk: its <c>SourcePath</c> and every file of it are under
    /// <see cref="ModelicaGraph.ReadOnlySources.InMemoryPathPrefix"/>. Read-only, never reported on.
    /// </summary>
    Supplied
}

namespace ModelicaGraph.DataTypes;

/// <summary>
/// Simple data structure containing library information needed for resource path resolution.
/// This avoids circular dependencies by allowing MLQTServices to pass library data
/// to GraphBuilder without requiring GraphBuilder to reference MLQTServices types.
/// </summary>
public class LibraryInfo
{
    /// <summary>
    /// The library name (e.g., "Modelica", "Buildings").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The absolute path to the library root directory.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Whether this library has no readable source — a vendor's encrypted build, whose classes are
    /// recovered from its documentation.
    /// </summary>
    /// <remarks>
    /// <para>Carried here rather than re-derived from the directory, because the loader already knows
    /// it and <c>GraphBuilder</c> cannot ask: <c>EncryptedLibraryDetector</c> lives in
    /// <c>MLQT.Services</c>, which depends on this assembly rather than the other way round.</para>
    ///
    /// <para>It settles which copy of a library a <c>modelica://</c> URI means when two are loaded
    /// under one name (B169). <b>An encrypted library cannot be the answer while a readable one
    /// exists</b>: nothing can read its code, so nothing knows what it references, and every
    /// reference that mentions it comes from somewhere else.</para>
    /// </remarks>
    public bool IsEncrypted { get; }

    /// <summary>
    /// Creates a new LibraryInfo instance.
    /// </summary>
    /// <param name="name">The library name.</param>
    /// <param name="rootPath">The absolute path to the library root directory.</param>
    /// <param name="isEncrypted">Whether the library ships without readable source.</param>
    public LibraryInfo(string name, string rootPath, bool isEncrypted = false)
    {
        Name = name;
        RootPath = rootPath;
        IsEncrypted = isEncrypted;
    }
}

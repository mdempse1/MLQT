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
    /// Creates a new LibraryInfo instance.
    /// </summary>
    /// <param name="name">The library name.</param>
    /// <param name="rootPath">The absolute path to the library root directory.</param>
    public LibraryInfo(string name, string rootPath)
    {
        Name = name;
        RootPath = rootPath;
    }
}

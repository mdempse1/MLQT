namespace ModelicaGraph.DataTypes;

/// <summary>
/// Types of nodes in the graph.
/// </summary>
public enum NodeType
{
    /// <summary>
    /// A Modelica source file (.mo file).
    /// </summary>
    File,

    /// <summary>
    /// A Modelica model/class definition.
    /// </summary>
    Model,

    /// <summary>
    /// An external resource file (data file, header, library, image, etc.).
    /// </summary>
    ResourceFile,

    /// <summary>
    /// An external resource directory (IncludeDirectory, LibraryDirectory, SourceDirectory).
    /// </summary>
    ResourceDirectory
}

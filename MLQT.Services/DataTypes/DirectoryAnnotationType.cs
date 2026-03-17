namespace MLQT.Services.DataTypes;

/// <summary>
/// Specifies the type of annotation that created a directory node.
/// Used to display different icons for different directory types.
/// </summary>
public enum DirectoryAnnotationType
{
    /// <summary>
    /// Not an annotated directory (regular directory in path hierarchy).
    /// </summary>
    None,

    /// <summary>
    /// Directory from IncludeDirectory annotation (C header files).
    /// </summary>
    IncludeDirectory,

    /// <summary>
    /// Directory from LibraryDirectory annotation (compiled libraries).
    /// </summary>
    LibraryDirectory,

    /// <summary>
    /// Directory from SourceDirectory annotation (C/Fortran source files).
    /// </summary>
    SourceDirectory
}

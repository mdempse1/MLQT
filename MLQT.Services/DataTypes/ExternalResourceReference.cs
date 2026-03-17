using ModelicaParser.DataTypes;

namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a resolved external resource reference with validation results.
/// </summary>
public class ExternalResourceReference
{
    /// <summary>
    /// Fully qualified ID of the model containing the reference.
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// The raw path as it appears in the source code.
    /// </summary>
    public string RawPath { get; set; } = "";

    /// <summary>
    /// The resolved absolute file system path, or null if unresolvable.
    /// </summary>
    public string? ResolvedPath { get; set; }

    /// <summary>
    /// How the resource was referenced in the code.
    /// </summary>
    public ResourceReferenceType ReferenceType { get; set; }

    /// <summary>
    /// For LoadSelector references, the name of the parameter.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Whether the raw path is an absolute file system path (non-portable).
    /// </summary>
    public bool IsAbsolutePath { get; set; }

    /// <summary>
    /// Whether the resolved file exists on disk.
    /// </summary>
    public bool FileExists { get; set; }

    /// <summary>
    /// Whether the file is an image file (images don't affect simulation results).
    /// </summary>
    public bool IsImageFile { get; set; }

    /// <summary>
    /// Whether this resource is a directory (IncludeDirectory, LibraryDirectory, etc.).
    /// </summary>
    public bool IsDirectory { get; set; }
}

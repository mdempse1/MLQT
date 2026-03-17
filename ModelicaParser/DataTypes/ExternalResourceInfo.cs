namespace ModelicaParser.DataTypes;

/// <summary>
/// Types of external resource references found in Modelica code.
/// </summary>
public enum ResourceReferenceType
{
    /// <summary>
    /// Resource loaded via Modelica.Utilities.Files.loadResource() function call.
    /// </summary>
    LoadResource,

    /// <summary>
    /// Resource path from a parameter with loadSelector annotation in its Dialog.
    /// </summary>
    LoadSelector,

    /// <summary>
    /// Modification of a parameter that has loadResource() as its default value.
    /// Similar to LoadSelector but detected via the default value rather than annotation.
    /// </summary>
    LoadResourceParameter,

    /// <summary>
    /// URI reference (modelica://) found in annotations or documentation strings.
    /// </summary>
    UriReference,

    /// <summary>
    /// Include directive from an external function annotation (e.g., Include="#include \"foo.h\"").
    /// </summary>
    ExternalInclude,

    /// <summary>
    /// Library reference from an external function annotation (e.g., Library="ModelicaStandardTables").
    /// </summary>
    ExternalLibrary,

    /// <summary>
    /// Include directory from an external function annotation (e.g., IncludeDirectory="modelica://Lib/Resources/C-Sources").
    /// </summary>
    ExternalIncludeDirectory,

    /// <summary>
    /// Library directory from an external function annotation (e.g., LibraryDirectory="modelica://Lib/Resources/Library").
    /// </summary>
    ExternalLibraryDirectory,

    /// <summary>
    /// Source directory from an external function annotation (e.g., SourceDirectory="modelica://Lib/Resources/Source").
    /// </summary>
    ExternalSourceDirectory
}

/// <summary>
/// Represents an external resource reference extracted from Modelica source code
/// during parsing, before path resolution.
/// </summary>
public class ExternalResourceInfo
{
    /// <summary>
    /// The raw path as it appears in the source code (e.g., "modelica://Modelica/Resources/data.mat").
    /// </summary>
    public string RawPath { get; set; } = "";

    /// <summary>
    /// How the resource was referenced in the code.
    /// </summary>
    public ResourceReferenceType ReferenceType { get; set; }

    /// <summary>
    /// For LoadSelector and LoadResourceParameter references, the name of the parameter that holds the file path.
    /// Null for other reference types.
    /// </summary>
    public string? ParameterName { get; set; }
}

using ModelicaParser.Icons;

namespace ModelicaParser.Visitors;

/// <summary>
/// Result of icon extraction including extends clause information.
/// </summary>
public class IconExtractionResult
{
    /// <summary>
    /// The extracted icon data (may be null if no Icon annotation found).
    /// </summary>
    public IconData? Icon { get; set; }

    /// <summary>
    /// List of base class names from extends clauses.
    /// These should be resolved to get inherited icons.
    /// </summary>
    public List<string> ExtendsClasses { get; set; } = new();

    /// <summary>
    /// Gets whether this model extends any base classes.
    /// </summary>
    public bool HasExtends => ExtendsClasses.Count > 0;

    /// <summary>
    /// The package name from the 'within' clause of the stored_definition (e.g. "Modelica.Blocks").
    /// Null if there is no within clause (e.g. the class is an inner class snippet without a file header).
    /// Used to qualify unresolved base class names for proper multi-level inheritance resolution.
    /// </summary>
    public string? WithinPackage { get; set; }
}

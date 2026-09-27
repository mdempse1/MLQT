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
    /// The extends clauses, by base name as written, whose <c>IconMap</c> / <c>DiagramMap</c> (this
    /// layer's) states something other than the defaults: a region to map the base into, or
    /// <c>primitivesVisible=false</c> (MLS 3.6 §18.6.3). A clause absent here draws its base as it is.
    /// </summary>
    public IReadOnlyDictionary<string, ExtendsMap> ExtendsMaps { get; set; } = new Dictionary<string, ExtendsMap>();

    /// <summary>
    /// The extends clauses, by base name as written, whose map states an extent other than the null
    /// region: the base is mapped into that region and does not lend the class its coordinate system
    /// (MLS 3.6 §18.6.1.1, §18.6.3).
    /// </summary>
    public IReadOnlySet<string> MappedExtends =>
        ExtendsMaps.Where(m => m.Value.Region is not null).Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>What an extends clause's map says about its base, or null when it has none.</summary>
    public ExtendsMap? MapFor(string baseName) => ExtendsMaps.GetValueOrDefault(baseName);

    /// <summary>
    /// The package name from the 'within' clause of the stored_definition (e.g. "Modelica.Blocks").
    /// Null if there is no within clause (e.g. the class is an inner class snippet without a file header).
    /// Used to qualify unresolved base class names for proper multi-level inheritance resolution.
    /// </summary>
    public string? WithinPackage { get; set; }
}

/// <summary>
/// An extends clause's <c>IconMap</c> or <c>DiagramMap</c> (MLS 3.6 §18.6.3).
/// </summary>
/// <param name="Region">Where the base's coordinate system, and what is drawn in it, is mapped to in
/// the derived class's; null for the null region, the default, which draws the base where it is.</param>
/// <param name="PrimitivesVisible">False hides the base's graphics; its components and connections
/// are still drawn.</param>
public sealed record ExtendsMap(double[]? Region, bool PrimitivesVisible);

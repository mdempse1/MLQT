namespace ModelicaParser.StyleRules;

/// <summary>
/// Configuration for naming convention checking, passed from the settings layer
/// into the parser-layer visitor. Uses a dictionary keyed by Modelica class keyword
/// (model, function, block, connector, record, type, package, class, operator).
/// </summary>
public record NamingConventionConfig
{
    /// <summary>
    /// Naming style rules for class names, keyed by Modelica class keyword
    /// (e.g., "model", "function", "block", "connector", "record", "type", "package", "class", "operator").
    /// </summary>
    public Dictionary<string, NamingStyle> ClassNamingRules { get; init; } = new();

    public NamingStyle PublicVariableNaming { get; init; }
    public NamingStyle PublicParameterNaming { get; init; }
    public NamingStyle PublicConstantNaming { get; init; }
    public NamingStyle ProtectedVariableNaming { get; init; }
    public NamingStyle ProtectedParameterNaming { get; init; }
    public NamingStyle ProtectedConstantNaming { get; init; }

    /// <summary>
    /// When true, a single trailing underscore segment is stripped from element names
    /// before checking against the naming style (e.g., "pressure_in" checks "pressure").
    /// </summary>
    public bool AllowUnderscoreSuffixes { get; init; }

    /// <summary>
    /// Names that are always accepted regardless of naming convention (e.g., product names
    /// like "NASCAR", established abbreviations like "OMC"). Compared case-sensitively.
    /// </summary>
    public HashSet<string> ExceptionNames { get; init; } = [];
}

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

    /// <summary>
    /// Additional regex patterns per naming slot. A name is valid if it matches the base
    /// NamingStyle OR any pattern for its slot. Keys are slot names from <see cref="SlotKeys"/>.
    /// Patterns are matched against the full original name (not suffix-stripped).
    /// </summary>
    public Dictionary<string, List<string>> AdditionalPatterns { get; init; } = new();

    /// <summary>
    /// Constants for the naming slot keys used in <see cref="AdditionalPatterns"/>.
    /// Class type keys match the keys used in <see cref="ClassNamingRules"/>.
    /// </summary>
    public static class SlotKeys
    {
        // Class types
        public const string Model = "model";
        public const string Function = "function";
        public const string Block = "block";
        public const string Connector = "connector";
        public const string Record = "record";
        public const string Type = "type";
        public const string Package = "package";
        public const string Class = "class";
        public const string Operator = "operator";

        // Elements
        public const string PublicVariable = "publicVariable";
        public const string PublicParameter = "publicParameter";
        public const string PublicConstant = "publicConstant";
        public const string ProtectedVariable = "protectedVariable";
        public const string ProtectedParameter = "protectedParameter";
        public const string ProtectedConstant = "protectedConstant";
    }
}

using ModelicaParser.StyleRules;

namespace ModelicaGraph;

/// <summary>
/// Serializable settings for naming convention checking. Uses flat properties
/// for clean JSON serialization. Call <see cref="ToConfig"/> to convert to the
/// parser-layer <see cref="NamingConventionConfig"/>.
/// </summary>
public class NamingConventionSettings
{
    public string PresetName { get; set; } = "Modelica Standard";

    // Class name rules (keyed by Modelica class keyword)
    public NamingStyle ModelNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle FunctionNaming { get; set; } = NamingStyle.CamelCase;
    public NamingStyle BlockNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle ConnectorNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle RecordNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle TypeNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle PackageNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle ClassNaming { get; set; } = NamingStyle.PascalCase;
    public NamingStyle OperatorNaming { get; set; } = NamingStyle.PascalCase;

    // Public element rules
    public NamingStyle PublicVariableNaming { get; set; } = NamingStyle.CamelCase;
    public NamingStyle PublicParameterNaming { get; set; } = NamingStyle.CamelCase;
    public NamingStyle PublicConstantNaming { get; set; } = NamingStyle.CamelCase;

    // Protected element rules
    public NamingStyle ProtectedVariableNaming { get; set; } = NamingStyle.CamelCase;
    public NamingStyle ProtectedParameterNaming { get; set; } = NamingStyle.CamelCase;
    public NamingStyle ProtectedConstantNaming { get; set; } = NamingStyle.CamelCase;

    // Suffix handling
    public bool AllowUnderscoreSuffixes { get; set; } = true;

    /// <summary>
    /// Names that are always accepted regardless of naming convention (e.g., product names
    /// like "NASCAR", established abbreviations). Case-sensitive.
    /// </summary>
    public List<string> ExceptionNames { get; set; } = [];

    /// <summary>
    /// Additional regex patterns per naming slot. Keys are slot names from
    /// <see cref="NamingConventionConfig.SlotKeys"/>. Each list contains regex pattern strings.
    /// A name is valid if it matches the base style OR any pattern for its slot.
    /// </summary>
    public Dictionary<string, List<string>> AdditionalPatterns { get; set; } = new();

    /// <summary>
    /// Converts to the parser-layer config for the naming convention visitor.
    /// </summary>
    public NamingConventionConfig ToConfig() => new()
    {
        ClassNamingRules = new Dictionary<string, NamingStyle>
        {
            ["model"] = ModelNaming,
            ["function"] = FunctionNaming,
            ["block"] = BlockNaming,
            ["connector"] = ConnectorNaming,
            ["record"] = RecordNaming,
            ["type"] = TypeNaming,
            ["package"] = PackageNaming,
            ["class"] = ClassNaming,
            ["operator"] = OperatorNaming
        },
        PublicVariableNaming = PublicVariableNaming,
        PublicParameterNaming = PublicParameterNaming,
        PublicConstantNaming = PublicConstantNaming,
        ProtectedVariableNaming = ProtectedVariableNaming,
        ProtectedParameterNaming = ProtectedParameterNaming,
        ProtectedConstantNaming = ProtectedConstantNaming,
        AllowUnderscoreSuffixes = AllowUnderscoreSuffixes,
        ExceptionNames = new HashSet<string>(ExceptionNames),
        AdditionalPatterns = AdditionalPatterns
            .Where(kvp => kvp.Value.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => new List<string>(kvp.Value))
    };

    /// <summary>
    /// Value equality check for change detection in the UI.
    /// </summary>
    public bool Equals(NamingConventionSettings other)
    {
        if (other is null) return false;
        return PresetName == other.PresetName &&
               ModelNaming == other.ModelNaming &&
               FunctionNaming == other.FunctionNaming &&
               BlockNaming == other.BlockNaming &&
               ConnectorNaming == other.ConnectorNaming &&
               RecordNaming == other.RecordNaming &&
               TypeNaming == other.TypeNaming &&
               PackageNaming == other.PackageNaming &&
               ClassNaming == other.ClassNaming &&
               OperatorNaming == other.OperatorNaming &&
               PublicVariableNaming == other.PublicVariableNaming &&
               PublicParameterNaming == other.PublicParameterNaming &&
               PublicConstantNaming == other.PublicConstantNaming &&
               ProtectedVariableNaming == other.ProtectedVariableNaming &&
               ProtectedParameterNaming == other.ProtectedParameterNaming &&
               ProtectedConstantNaming == other.ProtectedConstantNaming &&
               AllowUnderscoreSuffixes == other.AllowUnderscoreSuffixes &&
               ExceptionNames.OrderBy(x => x).SequenceEqual(other.ExceptionNames.OrderBy(x => x)) &&
               AdditionalPatternsEqual(other.AdditionalPatterns);
    }

    private bool AdditionalPatternsEqual(Dictionary<string, List<string>> other)
    {
        var thisKeys = AdditionalPatterns
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp => kvp.Key).OrderBy(k => k).ToList();
        var otherKeys = other
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp => kvp.Key).OrderBy(k => k).ToList();

        if (!thisKeys.SequenceEqual(otherKeys))
            return false;

        foreach (var key in thisKeys)
        {
            if (!AdditionalPatterns[key].OrderBy(p => p)
                    .SequenceEqual(other[key].OrderBy(p => p)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Creates a deep copy of this settings object.
    /// </summary>
    public NamingConventionSettings Clone() => new()
    {
        PresetName = PresetName,
        ModelNaming = ModelNaming,
        FunctionNaming = FunctionNaming,
        BlockNaming = BlockNaming,
        ConnectorNaming = ConnectorNaming,
        RecordNaming = RecordNaming,
        TypeNaming = TypeNaming,
        PackageNaming = PackageNaming,
        ClassNaming = ClassNaming,
        OperatorNaming = OperatorNaming,
        PublicVariableNaming = PublicVariableNaming,
        PublicParameterNaming = PublicParameterNaming,
        PublicConstantNaming = PublicConstantNaming,
        ProtectedVariableNaming = ProtectedVariableNaming,
        ProtectedParameterNaming = ProtectedParameterNaming,
        ProtectedConstantNaming = ProtectedConstantNaming,
        AllowUnderscoreSuffixes = AllowUnderscoreSuffixes,
        ExceptionNames = new List<string>(ExceptionNames),
        AdditionalPatterns = AdditionalPatterns.ToDictionary(
            kvp => kvp.Key,
            kvp => new List<string>(kvp.Value))
    };
}

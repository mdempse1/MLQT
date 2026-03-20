using ModelicaParser.StyleRules;

namespace ModelicaGraph;

/// <summary>
/// Factory methods for predefined naming convention presets.
/// </summary>
public static class NamingConventionPresets
{
    /// <summary>
    /// Standard Modelica naming: PascalCase for classes (except camelCase for functions),
    /// camelCase for all elements, with underscore suffixes allowed.
    /// </summary>
    public static NamingConventionSettings ModelicaStandard() => new()
    {
        PresetName = "Modelica Standard",
        ModelNaming = NamingStyle.PascalCase,
        FunctionNaming = NamingStyle.CamelCase,
        BlockNaming = NamingStyle.PascalCase,
        ConnectorNaming = NamingStyle.PascalCase,
        RecordNaming = NamingStyle.PascalCase,
        TypeNaming = NamingStyle.PascalCase,
        PackageNaming = NamingStyle.PascalCase,
        ClassNaming = NamingStyle.PascalCase,
        OperatorNaming = NamingStyle.PascalCase,
        PublicVariableNaming = NamingStyle.CamelCase,
        PublicParameterNaming = NamingStyle.CamelCase,
        PublicConstantNaming = NamingStyle.CamelCase,
        ProtectedVariableNaming = NamingStyle.CamelCase,
        ProtectedParameterNaming = NamingStyle.CamelCase,
        ProtectedConstantNaming = NamingStyle.CamelCase,
        AllowUnderscoreSuffixes = true,
        AdditionalPatterns = new()
    };

    /// <summary>
    /// All snake_case naming for both classes and elements.
    /// </summary>
    public static NamingConventionSettings SnakeCase() => new()
    {
        PresetName = "snake_case",
        ModelNaming = NamingStyle.SnakeCase,
        FunctionNaming = NamingStyle.SnakeCase,
        BlockNaming = NamingStyle.SnakeCase,
        ConnectorNaming = NamingStyle.SnakeCase,
        RecordNaming = NamingStyle.SnakeCase,
        TypeNaming = NamingStyle.SnakeCase,
        PackageNaming = NamingStyle.SnakeCase,
        ClassNaming = NamingStyle.SnakeCase,
        OperatorNaming = NamingStyle.SnakeCase,
        PublicVariableNaming = NamingStyle.SnakeCase,
        PublicParameterNaming = NamingStyle.SnakeCase,
        PublicConstantNaming = NamingStyle.SnakeCase,
        ProtectedVariableNaming = NamingStyle.SnakeCase,
        ProtectedParameterNaming = NamingStyle.SnakeCase,
        ProtectedConstantNaming = NamingStyle.SnakeCase,
        AllowUnderscoreSuffixes = false,
        AdditionalPatterns = new()
    };

    /// <summary>
    /// Modelica Standard with UPPER_CASE constants.
    /// </summary>
    public static NamingConventionSettings UpperCaseConstants() => new()
    {
        PresetName = "Modelica + UPPER_CASE Constants",
        ModelNaming = NamingStyle.PascalCase,
        FunctionNaming = NamingStyle.CamelCase,
        BlockNaming = NamingStyle.PascalCase,
        ConnectorNaming = NamingStyle.PascalCase,
        RecordNaming = NamingStyle.PascalCase,
        TypeNaming = NamingStyle.PascalCase,
        PackageNaming = NamingStyle.PascalCase,
        ClassNaming = NamingStyle.PascalCase,
        OperatorNaming = NamingStyle.PascalCase,
        PublicVariableNaming = NamingStyle.CamelCase,
        PublicParameterNaming = NamingStyle.CamelCase,
        PublicConstantNaming = NamingStyle.UpperCase,
        ProtectedVariableNaming = NamingStyle.CamelCase,
        ProtectedParameterNaming = NamingStyle.CamelCase,
        ProtectedConstantNaming = NamingStyle.UpperCase,
        AllowUnderscoreSuffixes = true,
        AdditionalPatterns = new()
    };

    /// <summary>
    /// All available presets with their display names.
    /// </summary>
    public static IReadOnlyList<(string Name, Func<NamingConventionSettings> Factory)> All =>
    [
        ("Modelica Standard", ModelicaStandard),
        ("snake_case", SnakeCase),
        ("Modelica + UPPER_CASE Constants", UpperCaseConstants)
    ];
}

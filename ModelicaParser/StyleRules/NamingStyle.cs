namespace ModelicaParser.StyleRules;

/// <summary>
/// Defines the naming convention styles that can be enforced on identifiers.
/// </summary>
public enum NamingStyle
{
    /// <summary>No enforcement — any name is accepted.</summary>
    Any,

    /// <summary>camelCase — starts with a lowercase letter, no underscores (e.g., myVariable).</summary>
    CamelCase,

    /// <summary>PascalCase — starts with an uppercase letter, no underscores (e.g., MyModel).</summary>
    PascalCase,

    /// <summary>snake_case — all lowercase with underscores (e.g., my_variable).</summary>
    SnakeCase,

    /// <summary>UPPER_CASE — all uppercase with underscores (e.g., MY_CONSTANT).</summary>
    UpperCase
}

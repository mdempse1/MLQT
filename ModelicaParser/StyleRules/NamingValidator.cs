using System.Text.RegularExpressions;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Pure validation logic for naming convention checking. No ANTLR dependencies.
/// </summary>
public static class NamingValidator
{
    /// <summary>
    /// Checks whether a name conforms to the specified naming style.
    /// Short abbreviation names (a letter followed by only digits, e.g., T, P3, V12)
    /// are always valid. When suffix stripping is enabled, a single trailing underscore
    /// segment is stripped before checking (e.g., "pressure_in" checks "pressure").
    /// </summary>
    public static bool IsValid(string name, NamingStyle style,
        bool allowSuffixes = false)
    {
        if (string.IsNullOrEmpty(name))
            return true;

        if (style == NamingStyle.Any)
            return true;

        // Short abbreviation names are always valid — Modelica uses T, P3, V12 extensively
        if (IsShortAbbreviation(name))
            return true;

        string nameToCheck = name;
        if (allowSuffixes)
        {
            var (baseName, _) = StripSuffix(name);
            nameToCheck = baseName;

            // If stripping left a short abbreviation, it's valid
            if (IsShortAbbreviation(nameToCheck))
                return true;
        }

        return style switch
        {
            NamingStyle.CamelCase => IsCamelCase(nameToCheck),
            NamingStyle.PascalCase => IsPascalCase(nameToCheck),
            NamingStyle.SnakeCase => IsSnakeCase(nameToCheck),
            NamingStyle.UpperCase => IsUpperCase(nameToCheck),
            _ => true
        };
    }

    /// <summary>
    /// Checks whether a name conforms to the specified naming style or matches any
    /// additional regex pattern. Patterns are matched against the full original name
    /// (not the suffix-stripped version).
    /// </summary>
    public static bool IsValid(string name, NamingStyle style,
        bool allowSuffixes, IReadOnlyList<Regex>? additionalPatterns)
    {
        if (IsValid(name, style, allowSuffixes))
            return true;

        if (additionalPatterns != null)
        {
            foreach (var pattern in additionalPatterns)
            {
                if (pattern.IsMatch(name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a name follows camelCase: starts with lowercase, no underscores.
    /// </summary>
    public static bool IsCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLower(name[0]))
            return false;

        return !name.Contains('_');
    }

    /// <summary>
    /// Checks if a name follows PascalCase: starts with uppercase, no underscores.
    /// </summary>
    public static bool IsPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsUpper(name[0]))
            return false;

        return !name.Contains('_');
    }

    /// <summary>
    /// Checks if a name follows snake_case: all lowercase letters, digits, and underscores.
    /// Must not start or end with underscore, no consecutive underscores.
    /// </summary>
    public static bool IsSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.StartsWith('_') || name.EndsWith('_'))
            return false;

        if (name.Contains("__"))
            return false;

        foreach (char c in name)
        {
            if (!char.IsLower(c) && !char.IsDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a name follows UPPER_CASE: all uppercase letters, digits, and underscores.
    /// Must not start or end with underscore, no consecutive underscores.
    /// </summary>
    public static bool IsUpperCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.StartsWith('_') || name.EndsWith('_'))
            return false;

        if (name.Contains("__"))
            return false;

        foreach (char c in name)
        {
            if (!char.IsUpper(c) && !char.IsDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a name is a short abbreviation: a single letter optionally followed
    /// by only digits (e.g., "T", "P3", "V12"). These are common in Modelica for
    /// well-established physical variable names and should not be convention-checked.
    /// </summary>
    public static bool IsShortAbbreviation(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (!char.IsLetter(name[0]))
            return false;

        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsDigit(name[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Strips a single trailing underscore segment from a name.
    /// Only the last underscore is considered (e.g., "pressure_in" → "pressure", "in").
    /// Names with a leading underscore only or trailing underscore are returned unchanged.
    /// </summary>
    public static (string BaseName, string? Suffix) StripSuffix(string name)
    {
        var lastUnderscore = name.LastIndexOf('_');
        if (lastUnderscore <= 0 || lastUnderscore == name.Length - 1)
            return (name, null);

        var suffix = name[(lastUnderscore + 1)..];
        var baseName = name[..lastUnderscore];

        return (baseName, suffix);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace ModelicaParser.DataTypes;

/// <summary>
/// The text a <see cref="RuleSeverity"/> is written as - in <c>.mlqt/settings.json</c>, a baseline, and
/// every message that names one - and the reading of it back.
/// </summary>
/// <remarks>
/// <b>Spelled out, never read from the enum.</b> An obfuscator may rename an enum's members, or delete
/// them from metadata altogether, after which <c>ToString()</c> gives a number, <c>Enum.Parse</c> finds
/// nothing and a reflection-based string-enum converter writes numbers (B502). Nothing here asks the
/// enum about itself, so a protected build writes and reads the same files as every other.
/// </remarks>
public static class RuleSeverityText
{
    /// <summary>The severity's name - <c>Off</c>, <c>Info</c>, <c>Warning</c> or <c>Error</c> - or its
    /// number for a value with no name.</summary>
    public static string Of(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Off => "Off",
        RuleSeverity.Info => "Info",
        RuleSeverity.Warning => "Warning",
        RuleSeverity.Error => "Error",
        _ => ((int)severity).ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Reads a severity's name, ignoring case.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out RuleSeverity severity)
    {
        severity = default;
        if (text is null)
            return false;

        foreach (var candidate in All)
        {
            if (string.Equals(text, Of(candidate), StringComparison.OrdinalIgnoreCase))
            {
                severity = candidate;
                return true;
            }
        }
        return false;
    }

    // Listed, not Enum.GetValues: the metadata that would answer that may not be there.
    private static readonly RuleSeverity[] All = [RuleSeverity.Off, RuleSeverity.Info, RuleSeverity.Warning, RuleSeverity.Error];
}

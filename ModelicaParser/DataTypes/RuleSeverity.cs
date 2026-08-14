using System.Text.Json.Serialization;

namespace ModelicaParser.DataTypes;

/// <summary>
/// Severity of a style/analysis rule. <see cref="Off"/> means the rule is disabled.
/// The CI quality gate fails on <see cref="Error"/> (threshold configurable); warnings and
/// info are reported but do not fail the build.
/// Serialized as a string (e.g. "Warning") so <c>.mlqt/settings.json</c> stays readable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleSeverity>))]
public enum RuleSeverity
{
    Off,
    Info,
    Warning,
    Error
}

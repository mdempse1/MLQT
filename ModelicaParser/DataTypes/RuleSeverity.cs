using System.Text.Json.Serialization;

namespace ModelicaParser.DataTypes;

/// <summary>
/// Severity of a style/analysis rule. <see cref="Off"/> means the rule is disabled.
/// The CI quality gate fails on <see cref="Error"/> (threshold configurable); warnings and
/// info are reported but do not fail the build.
/// Serialized as a string (e.g. "Warning") so <c>.mlqt/settings.json</c> stays readable.
///
/// <para><b>Written by <see cref="RuleSeverityJsonConverter"/></b>, whose names are spelled out in
/// <see cref="RuleSeverityText"/>, never read from these members: an obfuscator may rename them or
/// delete them from metadata, and these names are in every committed <c>.mlqt/settings.json</c> and
/// baseline (B502).</para>
/// </summary>
[JsonConverter(typeof(RuleSeverityJsonConverter))]
public enum RuleSeverity
{
    Off,
    Info,
    Warning,
    Error
}

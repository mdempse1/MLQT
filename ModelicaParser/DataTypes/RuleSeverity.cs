using System.Text.Json.Serialization;

namespace ModelicaParser.DataTypes;

/// <summary>
/// Severity of a style/analysis rule. <see cref="Off"/> means the rule is disabled.
/// The CI quality gate fails on <see cref="Error"/> (threshold configurable); warnings and
/// info are reported but do not fail the build.
/// Serialized as a string (e.g. "Warning") so <c>.mlqt/settings.json</c> stays readable.
///
/// <para><b>Each name is written down</b> rather than taken from the member, because these names are
/// in every committed <c>.mlqt/settings.json</c> and baseline: an obfuscated build renames the
/// members, and would otherwise write files nothing else can read (B502). A test holds each to its
/// member's name.</para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleSeverity>))]
public enum RuleSeverity
{
    [JsonStringEnumMemberName("Off")] Off,
    [JsonStringEnumMemberName("Info")] Info,
    [JsonStringEnumMemberName("Warning")] Warning,
    [JsonStringEnumMemberName("Error")] Error
}

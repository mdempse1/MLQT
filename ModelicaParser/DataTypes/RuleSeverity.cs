namespace ModelicaParser.DataTypes;

/// <summary>
/// Severity of a style/analysis rule. <see cref="Off"/> means the rule is disabled.
/// The CI quality gate fails on <see cref="Error"/> (threshold configurable); warnings and
/// info are reported but do not fail the build.
/// </summary>
public enum RuleSeverity
{
    Off,
    Info,
    Warning,
    Error
}

namespace ModelicaParser.DataTypes;

/// <summary>
/// A structured style/analysis finding. Unlike <see cref="LogMessage"/> (a flat, display-oriented
/// message shared with the parser and external tools), a <see cref="Finding"/> carries the
/// structured identity later phases depend on: a stable <see cref="RuleId"/>, a
/// <see cref="RuleSeverity"/>, structured element identity, and a reformat-stable
/// <see cref="Fingerprint"/> (for the baseline/ratchet).
///
/// It is a record so later phases can add fields (e.g. a resolution-confidence flag for the
/// Wave-2 analyses) with a default, without breaking existing construction sites.
/// </summary>
public sealed record Finding
{
    /// <summary>Stable rule identifier, e.g. <c>"MLQT.Naming.Convention"</c>.</summary>
    public required string RuleId { get; init; }

    /// <summary>Fully qualified model/class name the finding belongs to.</summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The element within the model the finding is about (e.g. a component name), or
    /// <c>null</c> for class-level findings. Part of the fingerprint.
    /// </summary>
    public string? ElementPath { get; init; }

    /// <summary>
    /// Disambiguator for rules that can fire multiple times on the same element
    /// (e.g. the misspelled word for a spelling rule). Part of the fingerprint.
    /// </summary>
    public string? Discriminator { get; init; }

    /// <summary>Human-readable message. Preserved verbatim from the rule visitor.</summary>
    public required string Message { get; init; }

    /// <summary>Source line for display only — deliberately NOT part of the fingerprint.</summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Configured severity. Left at the default when a visitor emits the finding; the
    /// orchestrator stamps the resolved severity from the settings map.
    /// </summary>
    public RuleSeverity Severity { get; init; } = RuleSeverity.Warning;

    /// <summary>Stable, reformat-independent identity used by the baseline/ratchet.</summary>
    public string Fingerprint => FindingFingerprint.Compute(RuleId, ModelId, ElementPath, Discriminator);

    /// <summary>
    /// Projects to the legacy <see cref="LogMessage"/> shape consumed by the GUI/MCP today.
    /// Reproduces the exact strings existing consumers rely on (<c>"Style warning"</c> severity,
    /// <c>"StyleChecking"</c> source, empty details).
    /// </summary>
    public LogMessage ToLogMessage() =>
        new(ModelId, "Style warning", LineNumber, Message) { Source = "StyleChecking" };
}

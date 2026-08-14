using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.StyleRules;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Waive a style-check finding in source by writing a <c>__MLQT(suppress="…")</c> vendor annotation
/// onto the class or one of its components. The annotation is spec-sanctioned (a <c>__VendorName</c>
/// annotation, ignored by Dymola/OpenModelica) and survives reformatting — unlike a comment — so the
/// waiver stays put and is honoured by <c>mlqt check</c> and the desktop app everywhere findings are
/// produced. Every write is parse-checked with rollback and refuses read-only files.
/// </summary>
[McpServerToolType]
public sealed class SuppressionTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public SuppressionTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "suppress_rule")]
    [Description("Suppress a style-check rule for a class (or one of its components) by adding a " +
                "'__MLQT(suppress=\"<ruleId>\")' vendor annotation to the source. Use this to waive a finding " +
                "that is a false positive or an accepted exception — the waiver is written into the .mo file, " +
                "survives reformatting, and is honoured by both 'mlqt check' and the desktop app. Pass the " +
                "ruleId exactly as reported (e.g. 'MLQT.Documentation.ParameterDescription'; the short form " +
                "without the 'MLQT.' prefix and the wildcard '*' are also accepted). Give 'component' to scope " +
                "the waiver to a single component (e.g. a parameter) rather than the whole class. An optional " +
                "'reason' is recorded as 'reason=\"…\"' alongside the suppression. Merges into any existing " +
                "'__MLQT' annotation rather than duplicating it. Fails if the class/component is not found or " +
                "the result would not parse. Set preview=true to see the file text without writing.")]
    public async Task<object> SuppressRule(
        [Description("Fully-qualified id of the class the finding is in.")] string classId,
        [Description("The rule id to suppress, exactly as reported (e.g. " +
                     "'MLQT.Documentation.ParameterDescription'). The short form without the 'MLQT.' prefix " +
                     "and the wildcard '*' (suppress all rules here) are also accepted.")]
        string ruleId,
        [Description("Optional component name to scope the waiver to a single component (a parameter, " +
                     "variable or connector). Omit to suppress the rule for the whole class.")]
        string? component = null,
        [Description("Optional human-readable reason, recorded as reason=\"…\" in the annotation.")]
        string? reason = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            return new ToolError("ruleId is required — the rule to suppress, e.g. 'MLQT.Documentation.ParameterDescription' or '*'.");
        ruleId = ruleId.Trim();

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        if (component is not null &&
            !ctx!.Layout.Components.Any(c => string.Equals(c.Name, component, StringComparison.Ordinal)))
            return new ToolError($"'{classId}' has no component named '{component}'.");

        if (!MlqtSuppressionWriter.TryAddSuppression(ctx!.ClassCode, component, ruleId, reason, out var newClassCode, out var writeError))
            return new ToolError(writeError ?? "Could not add the suppression annotation.");

        // A rule id that no catalog rule matches is allowed (custom rules, the wildcard), but flag it so a
        // typo doesn't silently waive nothing.
        string? note = null;
        if (ruleId != "*" && !RuleCatalog.IsKnown(ruleId) && !RuleCatalog.IsKnown("MLQT." + ruleId))
            note = $"Note: '{ruleId}' is not a known built-in rule id — check the id from the finding if you did not intend a custom rule.";

        var outcome = await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview,
            component is null ? $"suppress '{ruleId}' on '{classId}'" : $"suppress '{ruleId}' on '{classId}.{component}'");

        if (outcome is ToolError)
            return outcome;
        var r = (ClassEditResult)outcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, note);
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
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

        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be edited.");

        var owner = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (owner is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        // Path from the file's top class to the target class. We edit the on-disk file text (the ground
        // truth, always holding the full nested structure), located by name path — robust for a nested
        // class and a short-class `type`, and safe when the package node's stored code is a formatting
        // "shell" that omits nested standalone classes.
        string[]? classPath = null;
        if (!string.Equals(owner.FileOwner.Id, node.Id, StringComparison.Ordinal))
        {
            var prefix = owner.FileOwner.Id + ".";
            if (!node.Id.StartsWith(prefix, StringComparison.Ordinal))
                return new ToolError($"Could not locate '{classId}' within '{owner.FilePath}'.");
            classPath = node.Id[prefix.Length..].Split('.');
        }

        string fileContent;
        try
        {
            fileContent = ModelicaParserHelper.PreprocessCode(await File.ReadAllTextAsync(owner.FilePath));
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not read '{owner.FilePath}': {ex.Message}");
        }

        if (!MlqtSuppressionWriter.TryAddSuppression(fileContent, classPath, component, ruleId, reason, out var newFileContent, out var writeError))
            return new ToolError(writeError ?? "Could not add the suppression annotation.");

        // A rule id that no catalog rule matches is allowed (custom rules, the wildcard), but flag it so a
        // typo doesn't silently waive nothing.
        string? note = null;
        if (ruleId != "*" && !RuleCatalog.IsKnown(ruleId) && !RuleCatalog.IsKnown("MLQT." + ruleId))
            note = $"Note: '{ruleId}' is not a known built-in rule id — check the id from the finding if you did not intend a custom rule.";

        var outcome = await ClassBodyEditor.PersistFileContentAsync(
            _libraries, _resources, _session, owner.FilePath, newFileContent, preview,
            component is null ? $"suppress '{ruleId}' on '{classId}'" : $"suppress '{ruleId}' on '{classId}.{component}'");

        if (outcome is ToolError)
            return outcome;
        var r = (ClassEditResult)outcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, note);
    }
}

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

        var located = LocateInFile(node, classId);
        if (located.Error is not null)
            return located.Error;
        var (owner, classPath, _) = located;

        string fileContent;
        try
        {
            // Read raw so the write is a minimal edit that keeps the file's existing line endings.
            fileContent = await ModelicaFileEncoding.ReadAllTextOnlyAsync(owner!.FilePath);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not read '{owner!.FilePath}': {ex.Message}");
        }

        if (!MlqtSuppressionWriter.TryAddSuppressionToFile(fileContent, classPath, component, ruleId, reason, out var newFileContent, out var writeError))
            return new ToolError(writeError ?? "Could not add the suppression annotation.");

        // A rule id that no catalog rule matches is allowed (custom rules, the wildcard), but flag it so a
        // typo doesn't silently waive nothing.
        string? note = null;
        if (ruleId != "*" && !RuleCatalog.IsKnown(ruleId) && !RuleCatalog.IsKnown("MLQT." + ruleId))
            note = $"Note: '{ruleId}' is not a known built-in rule id — check the id from the finding if you did not intend a custom rule.";

        var outcome = await ClassBodyEditor.PersistFileContentAsync(
            _libraries, _resources, _session, owner!.FilePath, newFileContent, preview,
            component is null ? $"suppress '{ruleId}' on '{classId}'" : $"suppress '{ruleId}' on '{classId}.{component}'");

        if (outcome is ToolError)
            return outcome;
        var r = (ClassEditResult)outcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, note);
    }

    [McpServerTool(Name = "accept_spelling_in_class")]
    [Description("Accept a word as correctly spelled in one class by adding a " +
                "'__MLQT(spelling=\"<word>\")' vendor annotation to its source. Use this for a term that is " +
                "right here but is not the library's vocabulary generally — it silences the spelling findings " +
                "for that word in that class only, and leaves every other misspelling in the class reported. " +
                "For a term the whole repository uses, add it to the repository's accepted spellings " +
                "(.mlqt/dictionary.txt) instead; to waive spelling for a class entirely, suppress " +
                "'MLQT.Spelling.Description' / 'MLQT.Spelling.Documentation' with suppress_rule. The possessive " +
                "of an accepted word is accepted too, so pass the word itself ('Stodola', not \"Stodola's\"). " +
                "Merges into any existing '__MLQT' annotation. Set preview=true to see the file text without writing.")]
    public async Task<object> AcceptSpellingInClass(
        [Description("Fully-qualified id of the class the misspelling was reported in.")] string classId,
        [Description("The word to accept, exactly as it appears in the text.")] string word,
        [Description("Optional human-readable reason, recorded as reason=\"…\" in the annotation.")]
        string? reason = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new ToolError("word is required — the word to accept in this class.");
        word = word.Trim();

        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be edited.");

        var located = LocateInFile(node, classId);
        if (located.Error is not null)
            return located.Error;
        var (owner, classPath, _) = located;

        string fileContent;
        try
        {
            fileContent = await ModelicaFileEncoding.ReadAllTextOnlyAsync(owner!.FilePath);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not read '{owner!.FilePath}': {ex.Message}");
        }

        if (!MlqtSuppressionWriter.TryAddSpellingExceptionToFile(
                fileContent, classPath, word, reason, out var newFileContent, out var writeError))
            return new ToolError(writeError ?? "Could not add the spelling annotation.");

        var outcome = await ClassBodyEditor.PersistFileContentAsync(
            _libraries, _resources, _session, owner.FilePath, newFileContent, preview,
            $"accept the spelling '{word}' in '{classId}'");

        if (outcome is ToolError)
            return outcome;
        var r = (ClassEditResult)outcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, null);
    }

    /// <summary>
    /// The file a class lives in and the path from that file's top class down to it.
    ///
    /// <para>We edit the on-disk file text (the ground truth, always holding the full nested
    /// structure), located by name path — robust for a nested class and a short-class <c>type</c>, and
    /// safe when the package node's stored code is a formatting "shell" that omits nested standalone
    /// classes.</para>
    /// </summary>
    private LocatedClass LocateInFile(ModelicaGraph.DataTypes.ModelNode node, string classId)
    {
        var owner = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (owner is null)
            return new LocatedClass(null, null, new ToolError($"Could not locate the source file for '{classId}'."));

        if (string.Equals(owner.FileOwner.Id, node.Id, StringComparison.Ordinal))
            return new LocatedClass(owner, null, null);

        var prefix = owner.FileOwner.Id + ".";
        if (!node.Id.StartsWith(prefix, StringComparison.Ordinal))
            return new LocatedClass(null, null, new ToolError($"Could not locate '{classId}' within '{owner.FilePath}'."));

        return new LocatedClass(owner, node.Id[prefix.Length..].Split('.'), null);
    }

    private readonly record struct LocatedClass(ModelFilePersistence.FileOwnerContext? Owner, string[]? ClassPath, ToolError? Error);
}

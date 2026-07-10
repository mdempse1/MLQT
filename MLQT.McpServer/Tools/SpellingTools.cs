using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.SpellChecking;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Spell checking of Modelica description/Documentation prose, spelling suggestions, and applying a
/// correction. correct_spelling writes the updated file to disk and refreshes the graph unless
/// preview is set. Typical workflow: spell_check → spelling_suggestions → correct_spelling.
/// </summary>
[McpServerToolType]
public sealed class SpellingTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IStyleCheckingService _styleChecking;

    public SpellingTools(ILibraryDataService libraries, IStyleCheckingService styleChecking)
    {
        _libraries = libraries;
        _styleChecking = styleChecking;
    }

    [McpServerTool(Name = "spell_check")]
    [Description("Spell-check the description and Documentation prose of a loaded class (or of an " +
                "arbitrary source snippet) and return the misspellings as violations (word + line). " +
                "Provide exactly one of class_id or source. Then use spelling_suggestions for fixes and " +
                "correct_spelling to apply them.")]
    public object SpellCheck(
        [Description("Fully-qualified class id to spell-check.")] string? classId = null,
        [Description("Arbitrary Modelica source to spell-check instead of a loaded class.")]
        string? source = null)
    {
        var settings = new ModelicaGraph.StyleCheckingSettings
        {
            SpellCheckDescription = true,
            SpellCheckDocumentation = true,
        };

        if (!string.IsNullOrWhiteSpace(classId))
        {
            var node = _libraries.GetModelById(classId);
            if (node is null)
                return NotFound(classId);
            if (node.IsParseFailurePlaceholder)
                return new ToolError($"Class '{classId}' failed to parse and cannot be spell-checked.");

            var violations = StyleCheckRunner.Run(node, settings, _libraries.CombinedGraph, _styleChecking);
            return ToViolationList(violations);
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var violations = StyleCheckRunner.RunStateless(source, settings, _styleChecking);
            return ToViolationList(violations);
        }

        return new ToolError("Provide either class_id or source.");
    }

    [McpServerTool(Name = "spelling_suggestions")]
    [Description("Get spelling suggestions for a single word from the spell checker (bundled en_US/en_GB " +
                "dictionaries plus the user's custom dictionary), and whether the word is already " +
                "considered correct.")]
    public object SpellingSuggestions(
        [Description("The (possibly misspelled) word to get suggestions for.")] string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new ToolError("word must be a non-empty string.");

        var checker = _styleChecking.EnsureSpellChecker();
        var isCorrect = checker.IsCorrect(word);
        var suggestions = isCorrect
            ? Array.Empty<string>()
            : checker.Suggest(word).ToArray();
        return new SpellSuggestionsResult(word, isCorrect, suggestions);
    }

    [McpServerTool(Name = "correct_spelling")]
    [Description("Replace a misspelled word with a correction throughout the description and " +
                "Documentation prose of the file containing the given class (whole-word, case-sensitive; " +
                "HTML tags, hyperlink hrefs and code/pre blocks are left untouched). By default the " +
                "corrected file is re-rendered, written to disk, and the graph refreshed; set " +
                "preview=true to return the corrected file text without writing. Returns the number of " +
                "replacements made (0 means the word was not found).")]
    public async Task<object> CorrectSpelling(
        [Description("Fully-qualified class id whose file should be corrected.")] string classId,
        [Description("The misspelled word to replace (whole-word, case-sensitive).")] string oldWord,
        [Description("The replacement word.")] string newWord,
        [Description("Emit at most one of each section when re-rendering; default false.")]
        bool oneOfEachSection = false,
        [Description("Move import statements to the top when re-rendering; default false.")]
        bool importStatementsFirst = false,
        [Description("Order components before nested classes when re-rendering; default false.")]
        bool componentsBeforeClasses = false,
        [Description("Return the corrected text without writing to disk or updating the graph; default false.")]
        bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(oldWord) || string.IsNullOrEmpty(newWord))
            return new ToolError("Both oldWord and newWord must be provided.");

        var node = _libraries.GetModelById(classId);
        if (node is null)
            return NotFound(classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be corrected.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        var owner = ctx.FileOwner;
        var originalCode = owner.Definition.ModelicaCode ?? string.Empty;
        var (corrected, replacements) = SpellingCorrector.ReplaceWordInStrings(originalCode, oldWord, newWord);

        if (replacements == 0)
            return new CorrectSpellingResult(classId, ctx.FilePath, 0, Changed: false, preview, Source: null);

        // Temporarily apply the correction so the saver re-renders the whole file from it.
        var originalParsed = owner.Definition.ParsedCode;
        owner.Definition.ModelicaCode = corrected;
        owner.Definition.ParsedCode = null;

        string rendered;
        try
        {
            rendered = ModelicaPackageSaver.RenderFileOwnerModel(
                owner, oneOfEachSection, importStatementsFirst, componentsBeforeClasses);
        }
        catch (Exception ex)
        {
            owner.Definition.ModelicaCode = originalCode;
            owner.Definition.ParsedCode = originalParsed;
            return new ToolError($"Rendering the corrected file failed: {ex.Message}");
        }

        if (preview)
        {
            // Restore in-memory state so the graph stays consistent with what is on disk.
            owner.Definition.ModelicaCode = originalCode;
            owner.Definition.ParsedCode = originalParsed;
            return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: false, PreviewOnly: true, rendered);
        }

        await File.WriteAllTextAsync(ctx.FilePath, rendered);
        // Re-parse the file from disk so all its model nodes are rebuilt from the saved content.
        await _libraries.ReloadFileAsync(ctx.FilePath);

        return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: true, PreviewOnly: false, rendered);
    }

    private static object ToViolationList(IReadOnlyList<ModelicaParser.DataTypes.LogMessage> violations) =>
        violations
            .Select(v => new StyleViolationDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "SpellCheck" : v.Source))
            .ToList();

    private static ToolError NotFound(string classId) =>
        new($"No class with id '{classId}'. Ensure a library is loaded and the id is fully-qualified; " +
            "use search_classes to find it.");
}

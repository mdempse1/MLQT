using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaParser.SpellChecking;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Spell checking of Modelica description/Documentation prose, spelling suggestions, and applying a
/// correction. The spell-check dictionary languages come from the repository's .mlqt/settings.json
/// (set via set_style_settings). correct_spelling writes the updated file to disk and refreshes the
/// graph unless preview is set. Typical workflow: spell_check → spelling_suggestions → correct_spelling.
/// </summary>
[McpServerToolType]
public sealed class SpellingTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly ICustomDictionaryService _customDictionary;
    private readonly IDictionaryManagerService _dictionaryManager;

    public SpellingTools(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager)
    {
        _libraries = libraries;
        _repositories = repositories;
        _customDictionary = customDictionary;
        _dictionaryManager = dictionaryManager;
    }

    [McpServerTool(Name = "spell_check")]
    [Description("Spell-check the description and Documentation prose of a loaded class (or an arbitrary " +
                "source snippet) and return the misspellings as violations (word + line). The dictionary " +
                "language(s) come from the relevant repository's settings (default en_US/en_GB). Provide " +
                "exactly one of class_id or source. Then use spelling_suggestions and correct_spelling.")]
    public object SpellCheck(
        [Description("Fully-qualified class id to spell-check.")] string? classId = null,
        [Description("Arbitrary Modelica source to spell-check instead of a loaded class.")]
        string? source = null)
    {
        if (!string.IsNullOrWhiteSpace(classId))
        {
            var node = _libraries.GetModelById(classId);
            if (node is null)
                return ToolDiagnostics.ClassNotFound(_libraries, classId);
            if (node.IsParseFailurePlaceholder)
                return new ToolError($"Class '{classId}' failed to parse and cannot be spell-checked.");

            var settings = SpellSettings(LanguagesForClass(classId));
            var context = StyleCheckContext.Build(settings, _libraries.CombinedGraph, _customDictionary, _dictionaryManager);
            return ToViolationList(StyleCheckRunner.Run(node, settings, context));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var settings = SpellSettings(SingleRepoLanguages());
            var context = StyleCheckContext.BuildStateless(settings, _customDictionary, _dictionaryManager);
            return ToViolationList(StyleCheckRunner.RunStateless(source, settings, context));
        }

        return new ToolError("Provide either class_id or source.");
    }

    [McpServerTool(Name = "spelling_suggestions")]
    [Description("Get spelling suggestions for a single word and whether it is already considered " +
                "correct. Uses the dictionary language(s) configured for the repository (default " +
                "en_US/en_GB) plus the user's custom dictionary.")]
    public object SpellingSuggestions(
        [Description("The (possibly misspelled) word to get suggestions for.")] string word,
        [Description("Optional repository id (GUID) or name to pick the dictionary language. Omit when a " +
                     "single repository is loaded.")]
        string? repositoryId = null)
    {
        if (string.IsNullOrWhiteSpace(word))
            return new ToolError("word must be a non-empty string.");

        IReadOnlyList<string>? languages;
        if (repositoryId is not null)
        {
            var (repo, error) = EntityResolver.ResolveRepository(_repositories, repositoryId);
            if (error is not null)
                return error;
            languages = repo!.StyleSettings?.SpellCheckLanguages;
        }
        else
        {
            languages = SingleRepoLanguages();
        }

        var checker = SpellCheckerFactory.Build(languages, _customDictionary, _dictionaryManager);
        var isCorrect = checker.IsCorrect(word);
        var suggestions = isCorrect ? Array.Empty<string>() : checker.Suggest(word).ToArray();
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
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
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

    private static StyleCheckingSettings SpellSettings(IReadOnlyList<string>? languages)
    {
        var settings = new StyleCheckingSettings { SpellCheckDescription = true, SpellCheckDocumentation = true };
        if (languages is { Count: > 0 })
            settings.SpellCheckLanguages = languages.ToList();
        return settings;
    }

    private IReadOnlyList<string>? LanguagesForClass(string classId)
    {
        var library = _libraries.Libraries.FirstOrDefault(l => l.ModelIds.Contains(classId));
        var repo = library?.RepositoryId is { } rid ? _repositories.GetRepository(rid) : null;
        return repo?.StyleSettings?.SpellCheckLanguages;
    }

    private IReadOnlyList<string>? SingleRepoLanguages()
        => _repositories.Repositories.Count == 1
            ? _repositories.Repositories[0].StyleSettings?.SpellCheckLanguages
            : null;

    private static object ToViolationList(IReadOnlyList<ModelicaParser.DataTypes.LogMessage> violations) =>
        violations
            .Select(v => new StyleViolationDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "SpellCheck" : v.Source))
            .ToList();
}

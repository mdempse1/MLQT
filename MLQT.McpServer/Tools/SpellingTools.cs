using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
using ModelicaGraph;
using ModelicaParser.SpellChecking;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Checking;
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
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public SpellingTools(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        IExternalResourceService resources,
        SessionState session)
    {
        _libraries = libraries;
        _repositories = repositories;
        _customDictionary = customDictionary;
        _dictionaryManager = dictionaryManager;
        _resources = resources;
        _session = session;
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

            var repository = RepositoryForClass(classId);
            var settings = SpellSettings(LanguagesOf(repository));
            var context = StyleCheckContext.Build(
                settings, _libraries.CombinedGraph, _customDictionary, _dictionaryManager,
                repository?.LocalPath);
            return ToViolationList(StyleCheckRunner.Run(node, settings, context));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            // A snippet belongs to no class, so the only sensible scope is the one loaded repository —
            // and then it is that repository's accepted words as well as its languages. Taking the
            // languages and not the words reported a term the team had accepted as a misspelling.
            var repository = SingleRepository();
            var settings = SpellSettings(LanguagesOf(repository));
            var context = StyleCheckContext.BuildStateless(
                settings, _customDictionary, _dictionaryManager, repository?.LocalPath);
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

        Repository? repository;
        if (repositoryId is not null)
        {
            var (repo, error) = EntityResolver.ResolveRepository(_repositories, repositoryId);
            if (error is not null)
                return error;
            repository = repo;
        }
        else
        {
            repository = SingleRepository();
        }

        var checker = SpellCheckerFactory.Build(
            LanguagesOf(repository), _customDictionary.WordsFor(repository?.LocalPath), _dictionaryManager);
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

        // Correct the file as it is on disk, not the owner's stored source. Style checking trims a
        // package's inline standalone children out of its ModelicaCode — each child has its own node
        // — so the word is often not in the stored source at all, and re-rendering the file from
        // what was left would write it back without those classes.
        string fileText;
        try
        {
            fileText = await ModelicaFileEncoding.ReadAllTextOnlyAsync(ctx.FilePath);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not read '{ctx.FilePath}': {ex.Message}");
        }

        var (corrected, replacements) = SpellingCorrector.ReplaceWordInStrings(fileText, oldWord, newWord);

        if (replacements == 0)
            return new CorrectSpellingResult(classId, ctx.FilePath, 0, Changed: false, preview, Source: null);

        // Temporarily hand the corrected file to the owner node so the saver re-renders from it. The
        // node's own stored source is kept so it can be put back if the file is never written.
        var storedCode = owner.Definition.ModelicaCode;
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
            owner.Definition.ModelicaCode = storedCode;
            owner.Definition.ParsedCode = originalParsed;
            return new ToolError($"Rendering the corrected file failed: {ex.Message}");
        }

        if (preview)
        {
            // Restore in-memory state so the graph stays consistent with what is on disk.
            owner.Definition.ModelicaCode = storedCode;
            owner.Definition.ParsedCode = originalParsed;
            return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: false, PreviewOnly: true, rendered);
        }

        if (FileWritability.RequireWritable(ctx.FilePath, "correct spelling in this file") is { } readOnly)
        {
            // Restore in-memory state so the graph stays consistent with what is on disk.
            owner.Definition.ModelicaCode = storedCode;
            owner.Definition.ParsedCode = originalParsed;
            return readOnly;
        }

        await ModelicaFileEncoding.WriteAllTextAsync(ctx.FilePath, rendered);
        // Re-parse the file from disk so all its model nodes are rebuilt from the saved content.
        var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: true, PreviewOnly: false, rendered);
    }

    private static StyleCheckingSettings SpellSettings(IReadOnlyList<string>? languages)
    {
        var settings = new StyleCheckingSettings { SpellCheckDescription = true, SpellCheckDocumentation = true };
        if (languages is { Count: > 0 })
            settings.SpellCheckLanguages = languages.ToList();
        return settings;
    }

    /// <summary>
    /// The repository a class belongs to, resolved the one way — through DictionaryScope, which the
    /// app uses too. Resolving it here separately is how the languages and the accepted words came to
    /// be taken from different copies of a library that is loaded twice.
    /// </summary>
    private Repository? RepositoryForClass(string classId) =>
        DictionaryScope.RepositoryForModel(_libraries, _repositories, classId);

    /// <summary>The single loaded repository, or null when the choice would be a guess.</summary>
    private Repository? SingleRepository() =>
        _repositories.Repositories.Count == 1 ? _repositories.Repositories[0] : null;

    private static IReadOnlyList<string>? LanguagesOf(Repository? repository) =>
        repository?.StyleSettings?.SpellCheckLanguages;

    private static object ToViolationList(IReadOnlyList<ModelicaParser.DataTypes.LogMessage> violations) =>
        violations
            .Select(v => new StyleViolationDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "SpellCheck" : v.Source))
            .ToList();
}

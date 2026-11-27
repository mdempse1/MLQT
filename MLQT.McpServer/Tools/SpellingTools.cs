using System.Reflection;
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
/// (set via mlqt_style_settings). mlqt_edit_class's correct_spelling writes the updated file to disk and refreshes the
/// graph unless preview is set. Typical workflow: mlqt_check with the spelling check (each finding carries its
/// suggestions) → mlqt_edit_class's correct_spelling.
/// </summary>
[Obfuscation]
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

    /// <param name="languages">The languages to check against for this call, in place of the
    /// repository's; null or empty for the repository's. mlqt_check's <c>settings</c> carries them, and
    /// they were dropped on the way here, so a check asked for en_GB alone also accepted "color" (B529).
    /// The accepted words are still the repository's.</param>
    internal object SpellCheck(
        string? classId = null,
        string? source = null,
        IReadOnlyList<string>? languages = null)
    {
        if (!string.IsNullOrWhiteSpace(classId))
        {
            var node = _libraries.GetModelById(classId);
            if (node is null)
                return ToolDiagnostics.ClassNotFound(_libraries, classId);
            if (node.IsParseFailurePlaceholder)
                return new ToolError($"Class '{classId}' failed to parse and cannot be spell-checked.");

            var repository = RepositoryForClass(classId);
            var settings = SpellSettings(Chosen(languages, repository));
            var context = StyleCheckContext.Build(
                settings, _libraries.CombinedGraph, _customDictionary, _dictionaryManager,
                repository?.LocalPath);
            return Result(
                ToFindingList(StyleCheckRunner.Run(node, settings, context)),
                MissingDictionaryNote(settings.SpellCheckLanguages));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            // A snippet belongs to no class, so the only sensible scope is the one loaded repository —
            // and then it is that repository's accepted words as well as its languages. Taking the
            // languages and not the words reported a term the team had accepted as a misspelling.
            var repository = SingleRepository();
            var settings = SpellSettings(Chosen(languages, repository));
            var context = StyleCheckContext.BuildStateless(
                settings, _customDictionary, _dictionaryManager, repository?.LocalPath);
            return Result(
                ToFindingList(StyleCheckRunner.RunStateless(source, settings, context)),
                MissingDictionaryNote(settings.SpellCheckLanguages));
        }

        return new ToolError("Give classId or source.");
    }

    /// <summary>
    /// Every class of a library spell-checked as <see cref="SpellCheck"/> checks one: with the library's
    /// repository's languages (or the call's) and accepted words, each class's findings at lines in
    /// that class. Null <paramref name="libraryId"/> is every library loaded.
    /// </summary>
    /// <remarks>
    /// mlqt_check refused spelling for a library while running its style check, and put the refusal
    /// in the spelling slot of an answer that otherwise looked like a success, so a caller read
    /// "no misspellings" from a check that never ran (B532). Classes the repository's ExcludedLibraries
    /// takes out of scope are not checked, as the style check leaves them. Nothing is recorded as a
    /// finding: the style check, which may run in the same call, records its own.
    /// </remarks>
    internal object SpellCheckLibrary(string? libraryId = null, IReadOnlyList<string>? languages = null)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "spell-checking a library") is { } noLib)
            return noLib;

        List<LoadedLibrary> targets;
        if (libraryId is not null)
        {
            var (library, error) = EntityResolver.ResolveLibrary(_libraries, libraryId);
            if (error is not null)
                return error;
            targets = [library!];
        }
        else
        {
            targets = _libraries.Libraries.ToList();
        }

        var findings = new List<StyleFindingDto>();
        var asked = new List<string>();
        foreach (var library in targets)
        {
            var repositorySettings = RepositorySettings.ForLibrary(_repositories, library);
            var chosen = languages is { Count: > 0 } ? languages : repositorySettings?.SpellCheckLanguages;
            var settings = SpellSettings(chosen);
            asked.AddRange(settings.SpellCheckLanguages);
            var context = StyleCheckContext.Build(
                settings, _libraries.CombinedGraph, _customDictionary, _dictionaryManager,
                DictionaryScope.RootForLibrary(_repositories, library));

            foreach (var id in library.ModelIds)
            {
                if (_libraries.GetModelById(id) is not { IsParseFailurePlaceholder: false } node
                    || repositorySettings?.IsLibraryExcluded(id) == true)
                    continue;
                findings.AddRange(ToFindingList(StyleCheckRunner.Run(node, settings, context)));
            }
        }

        return Result(findings, MissingDictionaryNote(asked.Distinct(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The findings as a result, cut to the number a style check shows. Every one was returned, and a
    /// library check with nothing named covers every library loaded - the dependencies included - so
    /// one call could hand an agent thousands of findings, none of them recorded for paging.
    /// </summary>
    private static SpellCheckResult Result(IReadOnlyList<StyleFindingDto> findings, string? note) =>
        new([.. findings.Take(StyleTools.MaxReturnedFindings)], note, findings.Count,
            findings.Count > StyleTools.MaxReturnedFindings);

    /// <param name="languages">As <see cref="SpellCheck"/>'s: the languages a check was run with, so a
    /// word is judged and corrected against the dictionaries that flagged it.</param>
    internal object SpellingSuggestions(
        string word,
        string? repositoryId = null,
        IReadOnlyList<string>? languages = null)
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

        return Suggestions(word, repository, languages);
    }

    /// <summary>
    /// Suggestions for a word a check flagged in <paramref name="classId"/>, judged with that class's
    /// repository's languages and accepted words - the ones that flagged it. Asked through the single
    /// loaded repository, a word flagged in one of several came back "correct" by the built-in
    /// languages, with nothing to suggest. Text that is no loaded class falls back to that one.
    /// </summary>
    internal SpellSuggestionsResult SuggestionsForFinding(string word, string? classId, IReadOnlyList<string>? languages)
    {
        var repository = (classId is null ? null : RepositoryForClass(classId)) ?? SingleRepository();
        return Suggestions(word, repository, languages);
    }

    private SpellSuggestionsResult Suggestions(string word, Repository? repository, IReadOnlyList<string>? languages)
    {
        var chosen = Chosen(languages, repository);
        var checker = SpellCheckerFactory.Build(
            chosen, _customDictionary.WordsFor(repository?.LocalPath), _dictionaryManager);
        var isCorrect = checker.IsCorrect(word);
        var suggestions = isCorrect ? Array.Empty<string>() : checker.Suggest(word).ToArray();
        return new SpellSuggestionsResult(word, isCorrect, suggestions, MissingDictionaryNote(chosen));
    }

    internal async Task<object> CorrectSpelling(
        string classId,
        string oldWord,
        string newWord,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "correct spelling in this class") is { } readOnlyClass)
            return readOnlyClass;

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

        // Correct the file as it is on disk. The class's stored source is not a safe basis for
        // rewriting it: style checking trims a package's inline standalone children out of its
        // ModelicaCode, so the word is often not in it and rebuilding the file from what was left
        // would write the file back without those classes.
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

        // Never persist broken code: abort if the correction somehow fails to parse.
        var (_, parseErrors) = ModelicaParserHelper.ParseWithErrors(corrected);
        if (parseErrors.Any(e => e.Severity == ModelicaParser.DataTypes.ParserErrorSeverity.FatalParseFailure))
            return new ToolError("Correction was not applied: the result failed to parse.");

        // The word is the only change: the file keeps its own line endings and trailing whitespace, so
        // the edit reads as a one-word diff rather than a reformat of the whole file. Reformatting is
        // what mlqt_format is for, and doing it here meant an agent's spelling fix and a user's
        // produced different diffs for the same correction. The one exception is the rule every write
        // in MLQT keeps (ModelicaFileEncoding.ForFile, B236): a file that did not end with a newline
        // gets one. ForFile is applied here, not left to the write, so the preview is the text that
        // would land on disk rather than the text before the write adds to it.
        corrected = ModelicaFileEncoding.ForFile(
            ctx.FilePath, SpellingCorrector.MatchFileEnding(fileText, corrected));

        if (preview)
            return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: true, PreviewOnly: true, corrected);

        if (FileWritability.RequireWritable(ctx.FilePath, "correct spelling in this file") is { } readOnly)
            return readOnly;

        await ModelicaFileEncoding.WriteAllTextAsync(ctx.FilePath, corrected);
        // Re-parse the file from disk so all its model nodes are rebuilt from the saved content.
        var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);

        return new CorrectSpellingResult(classId, ctx.FilePath, replacements, Changed: true, PreviewOnly: false, corrected);
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

    /// <summary>The call's languages where it names any, else the repository's.</summary>
    private static IReadOnlyList<string>? Chosen(IReadOnlyList<string>? languages, Repository? repository) =>
        languages is { Count: > 0 } ? languages : LanguagesOf(repository);

    /// <summary>
    /// Says so when this machine has no dictionary for a language the settings ask for. The languages
    /// are committed with the repository and the dictionaries are installed per machine, so an agent
    /// working on a box that lacks one gets results that quietly are not the ones the settings
    /// describe.
    /// </summary>
    private string? MissingDictionaryNote(IEnumerable<string>? languages) =>
        DictionaryAvailability.WarningFor(languages, _dictionaryManager);

    private static IReadOnlyList<StyleFindingDto> ToFindingList(
        IReadOnlyList<ModelicaParser.DataTypes.LogMessage> findings) =>
        findings
            .Select(v => new StyleFindingDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "SpellCheck" : v.Source))
            .ToList();
}

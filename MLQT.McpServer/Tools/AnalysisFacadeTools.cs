using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;
using ModelicaParser.SpellChecking;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Two tools of the consolidated set (B503): <c>mlqt_check</c>, every check of a class, a library or
/// source text in one call, and <c>mlqt_dependencies</c>, every question about what uses what. Each is a
/// facade over the current tools it replaces, which it calls rather than repeats.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class AnalysisFacadeTools
{
    /// <summary>The checks <c>mlqt_check</c> runs; <c>style</c> is the default.</summary>
    internal static readonly IReadOnlyList<string> Checks = ["style", "spelling", "references", "resources"];

    /// <summary>The most flagged words <c>mlqt_check</c> looks up suggestions for, in the order found.</summary>
    internal const int MaxSuggestedWords = 25;

    private readonly SessionState _session;
    private readonly StyleTools _style;
    private readonly SpellingTools _spelling;
    private readonly ViewTools _views;
    private readonly ResourceTools _resources;
    private readonly DependencyTools _dependencies;
    private readonly VcsTools _vcs;

    public AnalysisFacadeTools(ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, ICodeReviewService codeReview, ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager, IImpactAnalysisService impact, SessionState session)
    {
        _session = session;
        _style = new StyleTools(libraries, codeReview, repositories, customDictionary, dictionaryManager, session);
        _spelling = new SpellingTools(libraries, repositories, customDictionary, dictionaryManager, resources, session);
        _views = new ViewTools(libraries);
        _resources = new ResourceTools(libraries, resources, session);
        _dependencies = new DependencyTools(libraries, impact, resources, session);
        _vcs = new VcsTools(libraries, repositories, impact, session);
    }

    [McpServerTool(Name = "mlqt_check", ReadOnly = true, OpenWorld = false)]
    [Description("Check Modelica code: one class (classId), a library (libraryId, or nothing for the one " +
                 "loaded), or source text that is not in a file (source). checks picks what to run, default " +
                 "['style']: 'style' (the repository's style rules), 'spelling' (descriptions and documentation, " +
                 "with suggestions for each word flagged), 'references' (type names that do not resolve) and " +
                 "'resources' (files the loaded libraries refer to that are missing). Each check answers as its " +
                 "own tool would; one that does not apply to the target says so in its place. settings " +
                 "overrides the style rules for this call only. limit and offset page references and resources. " +
                 "This is a style and static check, not a compile check - the simulator does that.")]
    public async Task<object> Check(
        [Description("Optional: one class by full id.")] string? classId = null,
        [Description("Optional: one library by name or id. With no target at all, the library loaded.")] string? libraryId = null,
        [Description("Optional: Modelica source text to check instead of a loaded class.")] string? source = null,
        [Description("Which checks, e.g. ['style', 'spelling']. Default ['style'].")] IReadOnlyList<string>? checks = null,
        [Description("Optional: style rules to use for this call, in the shape mlqt_style_settings returns.")]
        StyleSettingsInput? settings = null,
        [Description("For references and resources: most results (default 100).")] int limit = 100,
        [Description("For references and resources: results to skip (default 0).")] int offset = 0)
    {
        var targets = new[] { classId, libraryId, source }.Count(t => !string.IsNullOrWhiteSpace(t));
        if (targets > 1)
            return new ToolError("Give one of classId, libraryId or source, not several.");
        var wanted = (checks is { Count: > 0 } ? checks : ["style"])
            .Select(c => c.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.FirstOrDefault(c => !Checks.Contains(c)) is { } unknown)
            return new ToolError($"'{unknown}' is not a check. Checks: {string.Join(", ", Checks)}.");

        var hasClass = !string.IsNullOrWhiteSpace(classId);
        var hasSource = !string.IsNullOrWhiteSpace(source);

        object? style = null, spelling = null, references = null, resources = null;
        IReadOnlyList<SpellSuggestionsResult>? suggestions = null;
        if (wanted.Contains("style"))
            style = hasClass ? _style.CheckClass(classId!, settings)
                : hasSource ? _style.CheckStyle(source!, settings)
                : await _style.CheckLibrary(libraryId, settings);
        if (wanted.Contains("spelling"))
        {
            spelling = hasClass || hasSource
                ? _spelling.SpellCheck(classId, source)
                : new ToolError("Spelling checks one class or source text; give classId or source.");
            if (spelling is SpellCheckResult spelt)
                suggestions = Suggest(spelt);
        }
        if (wanted.Contains("references"))
            references = hasSource
                ? new ToolError("References are checked in loaded classes, not in source text; give classId or libraryId.")
                : _views.ValidateClassReferences(classId, libraryId, limit, offset);
        if (wanted.Contains("resources"))
            resources = await AnalyseFirstAsync() ?? _resources.GetResourceWarnings(limit, offset);

        return new CheckFacadeResult(style, spelling, suggestions, references, resources);
    }

    // Suggestions for each word flagged, so fixing one needs no call per word.
    private IReadOnlyList<SpellSuggestionsResult> Suggest(SpellCheckResult result) =>
        result.Findings
            .Select(f => SpellingMessage.WordFrom(f.Summary))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSuggestedWords)
            .Select(word => _spelling.SpellingSuggestions(word))
            .OfType<SpellSuggestionsResult>()
            .ToList();

    [McpServerTool(Name = "mlqt_dependencies", ReadOnly = true, OpenWorld = false)]
    [Description("What uses what among the loaded Modelica classes. Give one target. classIds with direction " +
                 "'uses' (what each class refers to) or 'used_by' (what refers to it); transitive=true with " +
                 "'used_by' is everything affected by changing them, through every level. repositoryId: the " +
                 "classes a working copy's changes touch (against revision if given), or with transitive=true " +
                 "everything those changes affect. resourcePath: the classes that refer to a file. It runs the " +
                 "dependency analysis first when the session has not.")]
    public async Task<object> Dependencies(
        [Description("Optional: classes by full id.")] IReadOnlyList<string>? classIds = null,
        [Description("For classIds: 'uses' (default) or 'used_by'.")] string direction = "uses",
        [Description("Follow every level, for 'used_by' and for repositoryId. Default false.")] bool transitive = false,
        [Description("Optional: a repository by name or id, for its working copy's changes.")] string? repositoryId = null,
        [Description("For repositoryId: compare against this revision instead of the working copy's base.")]
        string? revision = null,
        [Description("Optional: a file's resolved path, for the classes that refer to it.")] string? resourcePath = null,
        [Description("For transitive answers: most classes (default 100).")] int limit = 100,
        [Description("For transitive answers: classes to skip (default 0).")] int offset = 0)
    {
        var hasClasses = classIds is { Count: > 0 };
        var targets = (hasClasses ? 1 : 0) + (string.IsNullOrWhiteSpace(repositoryId) ? 0 : 1) +
                      (string.IsNullOrWhiteSpace(resourcePath) ? 0 : 1);
        if (targets != 1)
            return new ToolError("Give exactly one target: classIds, repositoryId or resourcePath.");
        var way = direction?.Trim().ToLowerInvariant();
        if (way is not ("uses" or "used_by"))
            return new ToolError($"direction must be 'uses' or 'used_by', not '{direction}'.");
        if (hasClasses && transitive && way == "uses")
            return new ToolError("transitive is for 'used_by': what changing these classes affects. For what a " +
                                 "class uses, ask with direction 'uses' for each level.");

        if (await AnalyseFirstAsync() is { } notAnalysed)
            return notAnalysed;

        if (!string.IsNullOrWhiteSpace(resourcePath))
            return _resources.FindResourceUsages(resourcePath);
        if (!string.IsNullOrWhiteSpace(repositoryId))
            return transitive
                ? _vcs.AnalyzeChangeImpact(repositoryId, revision, limit, offset)
                : _vcs.GetChangedClasses(repositoryId, revision);
        if (transitive)
            return _dependencies.AnalyzeImpact([.. classIds!], limit, offset);
        return new DependenciesFacadeResult(way, classIds!
            .Select(id => new ClassDependencies(id, way == "uses" ? _dependencies.GetDependencies(id) : _dependencies.FindUsages(id)))
            .ToList());
    }

    /// <summary>Runs the dependency and resource analysis when the session has not, as
    /// <c>mlqt_analyze_dependencies</c> would; null when it has run, else its error.</summary>
    private async Task<ToolError?> AnalyseFirstAsync()
    {
        if (_session.DependenciesAnalyzed)
            return null;
        return await _dependencies.AnalyzeDependencies() as ToolError;
    }
}

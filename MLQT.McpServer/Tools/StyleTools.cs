using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Style/quality checking, settings, and issue retrieval. Style checking is opt-in (nothing runs at
/// load). Rule settings are per-repository: they come from each repo's .mlqt/settings.json (loaded
/// by load_repository), and set_style_settings writes changes back there.
/// </summary>
[McpServerToolType]
public sealed class StyleTools
{
    private const int MaxReturnedViolations = 200;
    private const int MaxIssueLimit = 1000;

    private readonly ILibraryDataService _libraries;
    private readonly ICodeReviewService _codeReview;
    private readonly IRepositoryService _repositories;
    private readonly ICustomDictionaryService _customDictionary;
    private readonly IDictionaryManagerService _dictionaryManager;

    public StyleTools(
        ILibraryDataService libraries,
        ICodeReviewService codeReview,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager)
    {
        _libraries = libraries;
        _codeReview = codeReview;
        _repositories = repositories;
        _customDictionary = customDictionary;
        _dictionaryManager = dictionaryManager;
    }

    [McpServerTool(Name = "get_style_settings")]
    [Description("Get the style-checking rule settings for a repository, read from its " +
                ".mlqt/settings.json (the same file MLQT uses). Returns the on/off rule toggles plus the " +
                "spell-check languages. With one repository loaded, repositoryId is optional. Libraries " +
                "loaded via load_library (no repository) report the built-in defaults. Modify the " +
                "returned settings and pass them to set_style_settings (to persist) or a check tool.")]
    public object GetStyleSettings(
        [Description("Optional repository id (GUID) or name. Omit when a single repository is loaded.")]
        string? repositoryId = null)
    {
        var (repo, error) = ResolveRepo(repositoryId, requireRepo: false);
        if (error is not null)
            return error;

        var settings = repo?.StyleSettings ?? new StyleCheckingSettings();
        var source = repo is not null ? "repository .mlqt/settings.json" : "built-in defaults (no repository loaded)";
        return new StyleSettingsResult(repo?.Id, repo?.Name, source, StyleSettingsInput.From(settings));
    }

    [McpServerTool(Name = "set_style_settings")]
    [Description("Update a repository's style-checking rules and spell-check languages and PERSIST them " +
                "to its .mlqt/settings.json (creating the file if needed), exactly like MLQT. Only the " +
                "rule toggles and spell languages are changed; the naming-convention config and other " +
                "settings are preserved. With one repository loaded, repositoryId is optional. Requires a " +
                "repository — libraries loaded via load_library have no .mlqt/settings.json to write.")]
    public async Task<object> SetStyleSettings(
        [Description("The new settings (rule toggles + spellCheckLanguages). Omitted spellCheckLanguages " +
                     "keeps the current languages.")]
        StyleSettingsInput settings,
        [Description("Optional repository id (GUID) or name. Omit when a single repository is loaded.")]
        string? repositoryId = null)
    {
        if (settings is null)
            return new ToolError("Provide a 'settings' object. Call get_style_settings to see the current shape.");

        var (repo, error) = ResolveRepo(repositoryId, requireRepo: true);
        if (error is not null)
            return error;

        repo!.StyleSettings ??= new StyleCheckingSettings();
        settings.ApplyTo(repo.StyleSettings);
        await _repositories.SaveRepositorySettingsAsync();

        var persisted = !repo.IsSettingsReadOnly;
        return new SetStyleSettingsResult(
            repo.Id, repo.Name, persisted,
            persisted ? System.IO.Path.Combine(repo.LocalPath, ".mlqt", "settings.json") : null,
            persisted ? null : "Could not write .mlqt/settings.json (read-only); the change applies to this session only.",
            StyleSettingsInput.From(repo.StyleSettings));
    }

    [McpServerTool(Name = "check_style")]
    [Description("Run style/spell rules against an arbitrary Modelica source snippet (stateless — no " +
                "library needed) and return the violations. If 'settings' is omitted, the loaded " +
                "repository's settings are used when exactly one is loaded, otherwise all rules are off. " +
                "Reference-validation and icon-inheritance rules need a loaded library and are inert here.")]
    public object CheckStyle(
        [Description("Modelica source code to check.")] string source,
        [Description("Rules to apply. Omit to use the single loaded repository's settings (or all-off).")]
        StyleSettingsInput? settings = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new ToolError("source must be non-empty Modelica code.");

        var effective = settings?.ToSettings() ?? SingleRepoSettings() ?? new StyleCheckingSettings();
        var context = StyleCheckContext.BuildStateless(effective, _customDictionary, _dictionaryManager);
        var violations = StyleCheckRunner.RunStateless(source, effective, context);
        return ToCheckResult(violations, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_class")]
    [Description("Run style/spell rules against a single loaded class and return the violations, which " +
                "are also stored for list_issues. By default the rules come from the class's repository " +
                "(.mlqt/settings.json); pass a 'settings' object to override for this run.")]
    public object CheckClass(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("Rules to apply for this run. Omit to use the class's repository settings.")]
        StyleSettingsInput? settings = null)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be style-checked.");

        var effective = settings?.ToSettings() ?? RepoSettingsForClass(classId);
        var context = StyleCheckContext.Build(effective, _libraries.CombinedGraph, _customDictionary, _dictionaryManager);
        var violations = StyleCheckRunner.Run(node, effective, context);

        _codeReview.RemoveLogMessagesForModels([classId]);
        _codeReview.AddLogMessages(violations);

        return ToCheckResult(violations, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_library")]
    [Description("Run style/spell rules across all classes in a loaded library (or every loaded library " +
                "if library_id is omitted) and return a summary plus the first 200 violations, all stored " +
                "for list_issues. By default each library is checked with its own repository settings " +
                "(.mlqt/settings.json); pass 'settings' to override for every class. Can be slow on a big " +
                "library.")]
    public object CheckLibrary(
        [Description("Optional: one library to check, by its id (GUID from list_libraries) or its name " +
                     "(e.g. 'Modelica'). Omit to check every loaded library. Not a class id.")]
        string? libraryId = null,
        [Description("Rules to apply for this run. Omit to use each library's repository settings.")]
        StyleSettingsInput? settings = null)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "checking a library") is { } noLib)
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

        var graph = _libraries.CombinedGraph;
        var explicitSettings = settings?.ToSettings();
        var all = new ConcurrentBag<LogMessage>();
        var checkedIds = new List<string>();
        var modelsChecked = 0;

        foreach (var library in targets)
        {
            var models = library.ModelIds
                .Select(id => _libraries.GetModelById(id))
                .Where(m => m is not null && !m.IsParseFailurePlaceholder)!
                .Cast<ModelNode>()
                .ToList();
            if (models.Count == 0)
                continue;

            // Each library is checked with its own repository settings unless an override was passed.
            var effective = explicitSettings ?? RepoSettingsForLibrary(library);
            var context = StyleCheckContext.Build(effective, graph, _customDictionary, _dictionaryManager);
            modelsChecked += models.Count;
            checkedIds.AddRange(models.Select(m => m.Id));

            Parallel.ForEach(
                models,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                node =>
                {
                    foreach (var v in StyleCheckRunner.Run(node, effective, context))
                        all.Add(v);
                });
        }

        if (modelsChecked == 0)
            return new ToolError("No checkable classes are loaded (all failed to parse, or none present).");

        var violations = all.ToList();
        _codeReview.RemoveLogMessagesForModels(checkedIds);
        _codeReview.AddLogMessages(violations);

        return ToCheckResult(violations, modelsChecked);
    }

    [McpServerTool(Name = "list_issues")]
    [Description("List issues currently known for the loaded libraries: parse errors (available " +
                "immediately after loading) plus style/spell violations from any check that has been run " +
                "(check_class / check_library). Filter by severity, source ('Parser' or 'StyleChecking'), " +
                "or a specific class id, and page with limit/offset.")]
    public object ListIssues(
        [Description("Filter by severity substring (case-insensitive), e.g. 'Error', 'Warning'.")]
        string? severity = null,
        [Description("Filter by source, e.g. 'Parser' or 'StyleChecking'.")] string? source = null,
        [Description("Filter to a single class id.")] string? classId = null,
        [Description("Include parse errors from loading (default true).")] bool includeParseErrors = true,
        [Description("Max items to return (default 100, max 1000).")] int limit = 100,
        [Description("Items to skip for pagination (default 0).")] int offset = 0)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "listing issues") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxIssueLimit);
        offset = Math.Max(offset, 0);

        var issues = new List<IssueItem>();

        if (includeParseErrors)
        {
            foreach (var node in _libraries.GetAllModels())
            {
                var errors = node.Definition.ParserErrors;
                if (errors.Count == 0)
                    continue;
                var filePath = ResolveFilePath(node);
                foreach (var e in errors)
                {
                    issues.Add(new IssueItem(
                        node.Id, "parse",
                        e.Severity == ParserErrorSeverity.FatalParseFailure ? "FatalParseError" : "SyntaxError",
                        e.Line, e.Message, e.OffendingToken ?? string.Empty, "Parser", filePath));
                }
            }
        }

        foreach (var m in _codeReview.LogMessages)
        {
            issues.Add(new IssueItem(
                m.ModelName, "style", m.Severity, m.LineNumber, m.Summary, m.Details,
                string.IsNullOrEmpty(m.Source) ? "StyleChecking" : m.Source,
                ResolveFilePath(_libraries.GetModelById(m.ModelName))));
        }

        IEnumerable<IssueItem> filtered = issues;
        if (!string.IsNullOrWhiteSpace(severity))
            filtered = filtered.Where(i => i.Severity.Contains(severity, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(source))
            filtered = filtered.Where(i => i.Source.Contains(source, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(classId))
            filtered = filtered.Where(i => string.Equals(i.ModelId, classId, StringComparison.Ordinal));

        var list = filtered
            .OrderBy(i => i.ModelId, StringComparer.Ordinal)
            .ThenBy(i => i.Line)
            .ToList();

        var page = list.Skip(offset).Take(limit).ToList();
        return new IssuesResult(list.Count, offset, page.Count, page);
    }

    // ----- settings resolution helpers -----

    private (Repository? repo, ToolError? error) ResolveRepo(string? repositoryId, bool requireRepo)
    {
        if (repositoryId is not null)
            return EntityResolver.ResolveRepository(_repositories, repositoryId);

        var repos = _repositories.Repositories;
        if (repos.Count == 1)
            return (repos[0], null);
        if (repos.Count == 0)
            return requireRepo
                ? (null, new ToolError("No repository is loaded. Per-repository style settings require a " +
                    "repository — use load_repository. Libraries loaded via load_library have no .mlqt/settings.json."))
                : (null, null);

        return (null, new ToolError(
            "Multiple repositories are loaded — specify repositoryId. Repositories: " +
            string.Join(", ", repos.Select(r => $"'{r.Name}' (id {r.Id})")) + "."));
    }

    private StyleCheckingSettings RepoSettingsForClass(string classId)
    {
        var library = _libraries.Libraries.FirstOrDefault(l => l.ModelIds.Contains(classId));
        return library is not null ? RepoSettingsForLibrary(library) : new StyleCheckingSettings();
    }

    private StyleCheckingSettings RepoSettingsForLibrary(LoadedLibrary library)
    {
        var repo = library.RepositoryId is { } rid ? _repositories.GetRepository(rid) : null;
        return repo?.StyleSettings ?? new StyleCheckingSettings();
    }

    private StyleCheckingSettings? SingleRepoSettings()
        => _repositories.Repositories.Count == 1 ? _repositories.Repositories[0].StyleSettings : null;

    private string? ResolveFilePath(ModelNode? node)
    {
        if (node?.ContainingFileId is null)
            return null;
        return _libraries.CombinedGraph.GetNode<FileNode>(node.ContainingFileId)?.FilePath;
    }

    private static CheckResult ToCheckResult(IReadOnlyList<LogMessage> violations, int modelsChecked)
    {
        var shown = violations
            .Take(MaxReturnedViolations)
            .Select(v => new StyleViolationDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "StyleChecking" : v.Source))
            .ToList();
        return new CheckResult(modelsChecked, violations.Count, shown, violations.Count > shown.Count);
    }
}

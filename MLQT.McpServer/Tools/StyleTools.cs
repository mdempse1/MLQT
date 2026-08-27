using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Style/quality checking, settings, and finding retrieval. Style checking is opt-in (nothing runs at
/// load). Rule settings are per-repository: they come from each repo's .mlqt/settings.json (loaded
/// by load_repository), and set_style_settings writes changes back there.
/// </summary>
[McpServerToolType]
public sealed class StyleTools
{
    private const int MaxReturnedFindings = 200;
    private const int MaxFindingLimit = 1000;

    private readonly ILibraryDataService _libraries;
    private readonly ICodeReviewService _codeReview;
    private readonly IRepositoryService _repositories;
    private readonly ICustomDictionaryService _customDictionary;
    private readonly IDictionaryManagerService _dictionaryManager;
    private readonly SessionState _session;

    public StyleTools(
        ILibraryDataService libraries,
        ICodeReviewService codeReview,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        SessionState session)
    {
        _libraries = libraries;
        _codeReview = codeReview;
        _repositories = repositories;
        _customDictionary = customDictionary;
        _dictionaryManager = dictionaryManager;
        _session = session;
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
                "library needed) and return the findings. If 'settings' is omitted, the loaded " +
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
        var findings = StyleCheckRunner.RunStateless(source, effective, context);
        return ToCheckResult(findings, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_class")]
    [Description("Run style/spell rules against a single loaded class and return the findings, which " +
                "are also stored for list_findings. By default the rules come from the class's repository " +
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
        // A class that failed to parse still has something worth returning: the parse error itself.
        // Refusing outright left the caller unable to tell "no findings" from "never looked".
        if (node.IsParseFailurePlaceholder)
        {
            var parseOnly = ParserErrorReporter.ToLogMessages([node]);
            _codeReview.RemoveLogMessagesForModels([classId]);
            _codeReview.AddLogMessages(parseOnly);
            return ToCheckResult(parseOnly, modelsChecked: 0);
        }

        var effective = settings?.ToSettings() ?? RepoSettingsForClass(classId);
        var context = StyleCheckContext.Build(
            effective, _libraries.CombinedGraph, _customDictionary, _dictionaryManager,
            DictionaryScope.RootForModel(_libraries, _repositories, classId));
        var findings = StyleCheckRunner.Run(node, effective, context);

        // Parse errors are not style rules and are reported whatever the settings say — a class that
        // only partly parsed makes every rule result below it unreliable.
        findings.AddRange(ParserErrorReporter.ToLogMessages([node]));

        _codeReview.RemoveLogMessagesForModels([classId]);
        _codeReview.AddLogMessages(findings);

        return ToCheckResult(findings, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_library")]
    [Description("Run style/spell rules across all classes in a loaded library (or every loaded library " +
                "if library_id is omitted) and return a summary plus the first 200 findings, all stored " +
                "for list_findings. By default each library is checked with its own repository settings " +
                "(.mlqt/settings.json); pass 'settings' to override for every class. If an enabled rule " +
                "needs cross-model dependencies (e.g. unused-class), dependency analysis is run first " +
                "automatically (matching the GUI and CLI). Can be slow on a big library.")]
    public async Task<object> CheckLibrary(
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

        // Style-check the trimmed representation (packages without their standalone children, which have
        // their own nodes) so the count matches the GUI and CLI — via the shared PackageCodeTrimmer so the
        // rule can't drift. The trim mutates stored source, so snapshot the affected packages first and
        // restore them in the finally: this session's edit/query tools rely on the full original source.
        var targetModelIds = targets.SelectMany(l => l.ModelIds).ToHashSet(StringComparer.Ordinal);
        var packageSnapshots = graph.ModelNodes
            .Where(m => m.ClassType == "package" && targetModelIds.Contains(m.Id))
            .ToDictionary(m => m.Id, m => (m.Definition.ModelicaCode, m.Definition.ParsedCode), StringComparer.Ordinal);
        ModelicaGraph.PackageCodeTrimmer.TrimStandaloneChildren(graph, targetModelIds);

        try
        {
            // Auto-run dependency analysis when an enabled rule needs cross-model edges (e.g. unused-class),
            // so the count matches the GUI and CLI, which both do this. Skipped when already analysed or when
            // no such rule is enabled, keeping a plain style-check cheap.
            if (!_session.DependenciesAnalyzed &&
                targets.Any(l => GraphAnalysisRunner.RequiresDependencyAnalysis(explicitSettings ?? RepoSettingsForLibrary(l))))
            {
                await GraphBuilder.AnalyzeDependenciesAsync(graph);
                _session.DependenciesAnalyzed = true;
            }

            foreach (var library in targets)
            {
                // Placeholders (files that failed to parse) are included: LibraryCheckSession skips
                // them for the per-class rules but needs them to report the parse failure. Excluding
                // them here meant the worst case — a file MLQT could not read — was reported as
                // nothing at all.
                var models = library.ModelIds
                    .Select(id => _libraries.GetModelById(id))
                    .Where(m => m is not null)!
                    .Cast<ModelNode>()
                    .ToList();
                if (models.Count == 0)
                    continue;

                // Each library is checked with its own repository settings unless an override was passed.
                var effective = explicitSettings ?? RepoSettingsForLibrary(library);
                modelsChecked += models.Count(m => !m.IsParseFailurePlaceholder);
                checkedIds.AddRange(models.Select(m => m.Id));

                // Go through the same LibraryCheckSession facade the CLI uses so the per-class checks and
                // the whole-graph analyses (package.order, uses hygiene, unused classes) can't drift between
                // the tools. Dependency-requiring analyses only run if analyze_dependencies ran first.
                // Whether the edges are present is read off the graph itself rather than the session
                // flag, so this can't disagree with what the GUI and CLI see for the same library.
                var findings = LibraryCheckSession.Check(
                    graph, models, effective, _customDictionary, _dictionaryManager,
                    honorSuppressions: true);
                foreach (var finding in findings)
                    // Finding.ToLogMessage renders everything as a style warning; a parse diagnostic
                    // has to keep its Error/Fatal severity and its "Parser" source.
                    all.Add(RuleIds.IsParseDiagnostic(finding.RuleId)
                        ? ParserErrorReporter.ToLogMessage(finding)
                        : finding.ToLogMessage());
            }

            if (modelsChecked == 0)
                return new ToolError("No checkable classes are loaded (all failed to parse, or none present).");

            var reported = all.ToList();
            _codeReview.RemoveLogMessagesForModels(checkedIds);
            _codeReview.AddLogMessages(reported);

            return ToCheckResult(reported, modelsChecked);
        }
        finally
        {
            // Restore the full package source so edit/query tools keep working on the real files.
            foreach (var kv in packageSnapshots)
            {
                var node = graph.GetNode<ModelNode>(kv.Key);
                if (node is not null)
                {
                    node.Definition.ModelicaCode = kv.Value.ModelicaCode;
                    node.Definition.ParsedCode = kv.Value.ParsedCode;
                }
            }
        }
    }

    [McpServerTool(Name = "list_findings")]
    [Description("List findings currently known for the loaded libraries: parse errors (available " +
                "immediately after loading) plus style/spell findings from any check that has been run " +
                "(check_class / check_library). Filter by severity, source ('Parser' or 'StyleChecking'), " +
                "or a specific class id, and page with limit/offset.")]
    public object ListFindings(
        [Description("Filter by severity substring (case-insensitive), e.g. 'Error', 'Warning'.")]
        string? severity = null,
        [Description("Filter by source, e.g. 'Parser' or 'StyleChecking'.")] string? source = null,
        [Description("Filter to a single class id.")] string? classId = null,
        [Description("Include parse errors from loading (default true).")] bool includeParseErrors = true,
        [Description("Max items to return (default 100, max 1000).")] int limit = 100,
        [Description("Items to skip for pagination (default 0).")] int offset = 0)
    {
        if (ToolDiagnostics.RequireLibrary(_libraries, "listing findings") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxFindingLimit);
        offset = Math.Max(offset, 0);

        var findings = new List<FindingItem>();

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
                    findings.Add(new FindingItem(
                        node.Id, "parse",
                        e.Severity == ParserErrorSeverity.FatalParseFailure ? "FatalParseError" : "SyntaxError",
                        e.Line, e.Message, e.OffendingToken ?? string.Empty, "Parser", filePath));
                }
            }
        }

        foreach (var m in _codeReview.LogMessages)
        {
            findings.Add(new FindingItem(
                m.ModelName, "style", m.Severity, m.LineNumber, m.Summary, m.Details,
                string.IsNullOrEmpty(m.Source) ? "StyleChecking" : m.Source,
                ResolveFilePath(_libraries.GetModelById(m.ModelName))));
        }

        IEnumerable<FindingItem> filtered = findings;
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
        return new FindingsResult(list.Count, offset, page.Count, page);
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

    private static CheckResult ToCheckResult(IReadOnlyList<LogMessage> findings, int modelsChecked)
    {
        var shown = findings
            .Take(MaxReturnedFindings)
            .Select(v => new StyleFindingDto(v.ModelName, v.Severity, v.LineNumber, v.Summary, v.Details,
                string.IsNullOrEmpty(v.Source) ? "StyleChecking" : v.Source))
            .ToList();
        return new CheckResult(modelsChecked, findings.Count, shown, findings.Count > shown.Count);
    }
}

using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Style/quality checking and issue retrieval. Style checking is opt-in (nothing runs at load);
/// call check_class / check_library to produce style violations, then list_issues to review them
/// alongside parse errors (which are available immediately after loading).
/// </summary>
[McpServerToolType]
public sealed class StyleTools
{
    private const int MaxReturnedViolations = 200;
    private const int MaxIssueLimit = 1000;

    private readonly ILibraryDataService _libraries;
    private readonly IStyleCheckingService _styleChecking;
    private readonly ICodeReviewService _codeReview;
    private readonly ISettingsService _settings;

    public StyleTools(
        ILibraryDataService libraries,
        IStyleCheckingService styleChecking,
        ICodeReviewService codeReview,
        ISettingsService settings)
    {
        _libraries = libraries;
        _styleChecking = styleChecking;
        _codeReview = codeReview;
        _settings = settings;
    }

    [McpServerTool(Name = "get_style_settings")]
    [Description("Get the current style-checking rule settings (all rules default to off). Returns a " +
                "flat object of on/off toggles plus spell-check languages. To re-check with different " +
                "rules, take this object, flip the toggles you want, and pass it as the 'settings' " +
                "argument to check_class or check_library.")]
    public async Task<StyleSettingsInput> GetStyleSettings()
    {
        var current = await _settings.GetAsync("StyleChecking", new StyleCheckingSettings());
        return StyleSettingsInput.From(current);
    }

    [McpServerTool(Name = "check_style")]
    [Description("Run style/spell rules against an arbitrary Modelica source snippet (stateless — no " +
                "library needed) and return the violations. Enable the rules you want via 'settings'. " +
                "Note: reference-validation and icon-inheritance rules need a loaded library and are " +
                "inert here; use check_class for those.")]
    public object CheckStyle(
        [Description("Modelica source code to check.")] string source,
        [Description("Rules to apply. If omitted, the current global settings are used (which may have " +
                     "all rules off).")]
        StyleSettingsInput? settings = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new ToolError("source must be non-empty Modelica code.");

        var resolved = ResolveSettings(settings);
        var violations = StyleCheckRunner.RunStateless(source, resolved, _styleChecking);
        return ToCheckResult(violations, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_class")]
    [Description("Run style/spell rules against a single loaded class and return the violations. The " +
                "results are also stored so they appear in list_issues. Pass a 'settings' object to " +
                "check with specific rules (see get_style_settings); omit it to use the global settings.")]
    public object CheckClass(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("Rules to apply. Omit to use the current global settings.")]
        StyleSettingsInput? settings = null)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return NotFound(classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be style-checked.");

        var resolved = ResolveSettings(settings);
        var violations = StyleCheckRunner.Run(node, resolved, _libraries.CombinedGraph, _styleChecking);

        _codeReview.RemoveLogMessagesForModels([classId]);
        _codeReview.AddLogMessages(violations);

        return ToCheckResult(violations, modelsChecked: 1);
    }

    [McpServerTool(Name = "check_library")]
    [Description("Run style/spell rules across all classes in a loaded library (or all loaded libraries " +
                "if library_id is omitted) and return a summary plus the first 200 violations. All " +
                "violations are stored for retrieval via list_issues. This can be slow on a large " +
                "library. Pass 'settings' to check with specific rules.")]
    public object CheckLibrary(
        [Description("Optional library id (from list_libraries). Omit to check every loaded library.")]
        string? libraryId = null,
        [Description("Rules to apply. Omit to use the current global settings.")]
        StyleSettingsInput? settings = null)
    {
        IReadOnlyList<ModelNode> models;
        if (libraryId is not null)
        {
            var library = _libraries.Libraries.FirstOrDefault(l => l.Id == libraryId);
            if (library is null)
                return new ToolError($"No loaded library with id '{libraryId}'. Call list_libraries.");
            models = library.ModelIds
                .Select(id => _libraries.GetModelById(id))
                .Where(m => m is not null && !m.IsParseFailurePlaceholder)!
                .Cast<ModelNode>()
                .ToList();
        }
        else
        {
            models = _libraries.GetAllModels().Where(m => !m.IsParseFailurePlaceholder).ToList();
        }

        if (models.Count == 0)
            return new ToolError("No checkable classes loaded. Load a library first (list_libraries).");

        var resolved = ResolveSettings(settings);
        var graph = _libraries.CombinedGraph;
        var all = new ConcurrentBag<LogMessage>();

        Parallel.ForEach(
            models,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            node =>
            {
                foreach (var v in StyleCheckRunner.Run(node, resolved, graph, _styleChecking))
                    all.Add(v);
            });

        var violations = all.ToList();
        _codeReview.RemoveLogMessagesForModels(models.Select(m => m.Id));
        _codeReview.AddLogMessages(violations);

        return ToCheckResult(violations, models.Count);
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

    private StyleCheckingSettings ResolveSettings(StyleSettingsInput? settings)
        => settings?.ToSettings()
           ?? _settings.GetAsync("StyleChecking", new StyleCheckingSettings()).GetAwaiter().GetResult();

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

    private static ToolError NotFound(string classId) =>
        new($"No class with id '{classId}'. Ensure a library is loaded and the id is fully-qualified; " +
            "use search_classes to find it.");
}

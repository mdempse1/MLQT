using System.Reflection;
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
/// by mlqt_load), and mlqt_style_settings writes changes back there.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class StyleTools
{
    /// <summary>How many findings a check returns inline, out of however many it stored.</summary>
    /// <remarks>
    /// Internal, with <see cref="MaxFindingLimit"/>, because mlqt_get_guidance quotes both numbers to tell
    /// an agent how to read a result it cannot see the whole of — and a number written out in prose
    /// beside the constant it describes is one that goes stale silently. A test holds the two together.
    /// </remarks>
    internal const int MaxReturnedFindings = 200;

    /// <summary>The largest page mlqt_list_findings will return; a bigger limit is clamped, not refused.</summary>
    internal const int MaxFindingLimit = 1000;

    /// <summary>
    /// Which findings a check or a listing shows against the baseline: every one, only those the
    /// baseline does not hold, or those plus accepted debt in classes the working copy changed.
    /// </summary>
    internal static readonly IReadOnlyList<string> BaselineViews = ["all", "new", "changed"];

    private readonly ILibraryDataService _libraries;
    private readonly ICodeReviewService _codeReview;
    private readonly IRepositoryService _repositories;
    private readonly ICustomDictionaryService _customDictionary;
    private readonly IDictionaryManagerService _dictionaryManager;
    private readonly SessionState _session;
    private readonly IBaselineStatusService? _baselineStatus;

    public StyleTools(
        ILibraryDataService libraries,
        ICodeReviewService codeReview,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        SessionState session,
        IBaselineStatusService? baselineStatus = null)
    {
        _libraries = libraries;
        _codeReview = codeReview;
        _repositories = repositories;
        _customDictionary = customDictionary;
        _dictionaryManager = dictionaryManager;
        _session = session;
        _baselineStatus = baselineStatus;
    }

    internal object GetStyleSettings(
        string? repositoryId = null)
    {
        var (repo, error) = ResolveRepo(repositoryId, requireRepo: false);
        if (error is not null)
            return error;

        var settings = repo?.StyleSettings ?? new StyleCheckingSettings();
        var source = repo is not null ? "repository .mlqt/settings.json" : "built-in defaults (no repository loaded)";
        return new StyleSettingsResult(repo?.Id, repo?.Name, source, StyleSettingsInput.From(settings));
    }

    internal async Task<object> SetStyleSettings(
        StyleSettingsInput settings,
        string? repositoryId = null)
    {
        if (settings is null)
            return new ToolError("Provide a 'settings' object. Call mlqt_style_settings to see the current shape.");

        var (repo, error) = ResolveRepo(repositoryId, requireRepo: true);
        if (error is not null)
            return error;

        repo!.StyleSettings ??= new StyleCheckingSettings();
        settings.ApplyTo(repo.StyleSettings);
        await _repositories.ApplyRepositorySettingsAsync(repo.Id);

        var persisted = !repo.IsSettingsReadOnly;
        return new SetStyleSettingsResult(
            repo.Id, repo.Name, persisted,
            persisted ? System.IO.Path.Combine(repo.LocalPath, ".mlqt", "settings.json") : null,
            persisted ? null : "Could not write .mlqt/settings.json (read-only); the change applies to this session only.",
            StyleSettingsInput.From(repo.StyleSettings));
    }

    internal object CheckStyle(
        string source,
        StyleSettingsInput? settings = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new ToolError("source must be non-empty Modelica code.");

        // A snippet belongs to no class, so the only sensible scope is the one loaded repository — and
        // then it is that repository's accepted words as well as its rules. Taking the rules and not
        // the words reports a term the team has accepted as a misspelling; see mlqt_check's spelling check, which
        // answers this the same way.
        var repository = SingleRepository();
        // All-off with neither, as the description says - which a blank settings object is not once a
        // rule is on by default (B322).
        var effective = settings?.ToSettings() ?? repository?.StyleSettings ?? StyleCheckingSettings.NothingEnabled();
        var context = StyleCheckContext.BuildStateless(
            effective, _customDictionary, _dictionaryManager, repository?.LocalPath);
        var findings = StyleCheckRunner.RunStateless(source, effective, context);
        // Text in no file is in no baseline.
        return ToCheckResult(findings, modelsChecked: 1, baseline: null, view: null);
    }

    internal object CheckClass(
        string classId,
        StyleSettingsInput? settings = null,
        string? baseline = null)
    {
        if (BaselineViewError(baseline) is { } badView)
            return badView;

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
            return ToCheckResult(parseOnly, modelsChecked: 0, CurrentBaseline(), baseline);
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

        return ToCheckResult(findings, modelsChecked: 1, CurrentBaseline(), baseline);
    }

    internal async Task<object> CheckLibrary(
        string? libraryId = null,
        StyleSettingsInput? settings = null,
        string? baseline = null)
    {
        if (BaselineViewError(baseline) is { } badView)
            return badView;
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
        // Two different questions, and conflating them is how the count came to disagree with the
        // CLI's. `checkable` is "was there anything here to read at all", which is what decides
        // whether this call can say anything; `modelsChecked` is what the caller is told, and leaves
        // out the classes an ExcludedLibraries pattern puts out of scope.
        var checkable = 0;
        var modelsChecked = 0;

        // Style-check the trimmed representation (packages without their standalone children, which have
        // their own nodes) so the count matches the GUI and CLI — via the shared PackageCodeTrimmer so the
        // rule can't drift. The trim mutates stored source, so snapshot the affected packages first and
        // restore them in the finally: this session's edit/query tools rely on the full original source.
        var targetModelIds = targets.SelectMany(l => l.ModelIds).ToHashSet(StringComparer.Ordinal);
        var packageSnapshots = graph.ModelNodes
            .Where(m => m.ClassType == "package" && targetModelIds.Contains(m.Id))
            .ToDictionary(m => m.Id, m => m.TakeSourceSnapshot(), StringComparer.Ordinal);
        ModelicaGraph.PackageCodeTrimmer.TrimStandaloneChildren(graph, targetModelIds);

        try
        {
            // Auto-run dependency analysis when an enabled rule needs cross-model edges (e.g. unused-class),
            // so the count matches the GUI and CLI, which both do this. Skipped when already analysed or when
            // no such rule is enabled, keeping a plain style-check cheap.
            if (!_session.DependenciesAnalyzed &&
                targets.Any(l => GraphAnalysisRunner.RequiresDependencyAnalysis(explicitSettings ?? RepoSettingsForLibrary(l))))
            {
                // With the library roots, so modelica:// URIs resolve against every loaded library —
                // the argument the CLI passes here and GraphRefresh passes on the edit path. Left out,
                // this one call built the resource edges from unresolved URIs, so whether a resource
                // resolved depended on whether mlqt_dependencies or mlqt_check ran first.
                await GraphBuilder.AnalyzeDependenciesAsync(graph, GraphRefresh.BuildLibraryInfos(_libraries));
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
                // Classes an ExcludedLibraries pattern takes out of scope are not checked, so they are
                // not counted as checked either — the CLI subtracts them for the reason it gives at
                // its own count: a mistyped library name then shows up as an unexpected number rather
                // than as a quiet pass. Counted here, MSL reported 7,833 classes against the CLI's
                // 6,677 for the identical run, the 1,156 being exactly what the settings excluded.
                var parseable = models.Count(m => !m.IsParseFailurePlaceholder);
                checkable += parseable;
                modelsChecked += parseable - models.Count(
                    m => !m.IsParseFailurePlaceholder && effective.IsLibraryExcluded(m.Id));
                checkedIds.AddRange(models.Select(m => m.Id));

                // Go through the same LibraryCheckSession facade the CLI uses so the per-class checks and
                // the whole-graph analyses (package.order, uses hygiene, unused classes) can't drift between
                // the tools. Dependency-requiring analyses only run if mlqt_dependencies ran first.
                // Whether the edges are present is read off the graph itself rather than the session
                // flag, so this can't disagree with what the GUI and CLI see for the same library.
                //
                // repositoryRoot is the repository the library came out of, because that is where its
                // accepted spellings live. Omitted, every word in .mlqt/dictionary.txt came back as a
                // misspelling and mlqt_check reported thousands of findings the GUI and CLI do not
                // (B166): over MSL, 21,249 against their 18,193, the whole of the difference being
                // MLQT.Spelling.Description and MLQT.Spelling.Documentation. The settings were read
                // from the repository all along — taking those and not the words is the same mistake
                // mlqt_check's spelling check had, and DictionaryScope is the one answer both now go through.
                var findings = LibraryCheckSession.Check(
                    graph, models, effective, _customDictionary, _dictionaryManager,
                    honorSuppressions: true,
                    repositoryRoot: DictionaryScope.RootForLibrary(_repositories, library));
                foreach (var finding in findings)
                    // Finding.ToLogMessage renders a style finding's severity as "Style error/warning/
                    // info"; a parse diagnostic has to keep the bare Error/Fatal and its "Parser"
                    // source, so a style re-run cannot clear it.
                    all.Add(RuleIds.IsParseDiagnostic(finding.RuleId)
                        ? ParserErrorReporter.ToLogMessage(finding)
                        : finding.ToLogMessage());
            }

            if (checkable == 0)
                return new ToolError("No checkable classes are loaded (all failed to parse, or none present).");

            var reported = all.ToList();
            _codeReview.RemoveLogMessagesForModels(checkedIds);
            _codeReview.AddLogMessages(reported);

            return ToCheckResult(reported, modelsChecked, CurrentBaseline(), baseline);
        }
        finally
        {
            // Restore the full package source so edit/query tools keep working on the real files.
            foreach (var kv in packageSnapshots)
            {
                var node = graph.GetNode<ModelNode>(kv.Key);
                if (node is null)
                    continue;

                node.RestoreSource(kv.Value);

                // With one deliberate exception. The findings just recorded were measured against the
                // TRIMMED source, so their line numbers do not describe the text now on the node —
                // and mlqt_list_findings maps them at read time. Reporting them at the class declaration
                // is ClassLocation's own answer for that ("pointing at the right class is always
                // true; pointing at the wrong line looks precise and is not"), so the flag stays
                // down until a reload replaces the node.
                node.SourceMatchesFile = false;
            }
        }
    }

    [McpServerTool(Name = "mlqt_list_findings", ReadOnly = true, OpenWorld = false)]
    [Description("List findings currently known for the loaded Modelica libraries: parse errors (available " +
                "immediately after loading) plus style/spell findings from any check that has been run " +
                "(mlqt_check). Filter by severity, source ('Parser' or 'StyleChecking'), " +
                "or a specific class id, and page with limit/offset. Each item carries two line numbers: " +
                "'line' is the line in 'filePath' - use that pair to edit the file - and 'modelLine' is " +
                "the line within the class's own source, for a caller working from mlqt_get_class's source view. " +
                "Where the repository has a committed baseline (.mlqt/baseline.json), each item's 'status' says " +
                "whether it is New, TouchedDebt (accepted debt in a class the working copy changed) or " +
                "AcceptedDebt; baseline 'new' lists only the New ones.")]
    public object ListFindings(
        [Description("Filter by severity substring (case-insensitive). A style finding carries the " +
                     "severity its rule is configured with, as 'Style error', 'Style warning' or " +
                     "'Style info'; a parse diagnostic is a bare 'Error' or 'Fatal'. So 'error' " +
                     "matches both kinds, and 'Style error' matches only the configured rules.")]
        string? severity = null,
        [Description("Filter by source, e.g. 'Parser' or 'StyleChecking'.")] string? source = null,
        [Description("Filter to a single class id.")] string? classId = null,
        [Description("Include parse errors from loading (default true).")] bool includeParseErrors = true,
        [Description("Max items to return (default 100, max 1000).")] int limit = 100,
        [Description("Items to skip for pagination (default 0).")] int offset = 0,
        [Description(BaselineParameter)] string baseline = "all")
    {
        if (BaselineViewError(baseline) is { } badView)
            return badView;
        // Asked about one class with nothing loaded: name the library that class is in.
        if (classId is not null && _libraries.Libraries.Count == 0)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (ToolDiagnostics.RequireLibrary(_libraries, "listing findings") is { } noLib)
            return noLib;

        limit = Math.Clamp(limit, 1, MaxFindingLimit);
        offset = Math.Max(offset, 0);

        var snapshot = CurrentBaseline();
        var findings = new List<FindingItem>();

        if (includeParseErrors)
        {
            // Derived from the graph, through the same converter the GUI and CLI use, so a parse
            // error reads identically on all three and carries a class-relative line like every
            // other finding. Reading ParserErrors here directly used to give it the *file* line,
            // which then sat next to filePath in one array with style findings counted from the
            // class - one field, two conventions.
            foreach (var finding in ParserErrorReporter.ToFindings(_libraries.GetAllModels()))
            {
                findings.Add(Item(
                    finding.ModelId, "parse",
                    finding.RuleId == RuleIds.ParseFailure ? "FatalParseError" : "SyntaxError",
                    finding.LineNumber, finding.Message, string.Empty, ParserErrorReporter.SourceName,
                    finding.RuleId, snapshot?.StatusOf(ParserErrorReporter.ToLogMessage(finding))));
            }
        }

        foreach (var m in _codeReview.LogMessages)
        {
            // Parse errors are taken from the graph above whether or not a check has run, and a
            // check records them here as well - so reporting both returned every parse error twice
            // once mlqt_check had been called.
            if (string.Equals(m.Source, ParserErrorReporter.SourceName, StringComparison.Ordinal))
                continue;

            findings.Add(Item(
                m.ModelName, "style", m.Severity, m.LineNumber, m.Summary, m.Details,
                string.IsNullOrEmpty(m.Source) ? LogMessage.StyleCheckingSource : m.Source,
                m.RuleId, snapshot?.StatusOf(m)));
        }

        IEnumerable<FindingItem> filtered = findings
            .Where(i => Shows(baseline, i.Status));
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
                    "repository - load its working copy with mlqt_load. A library loaded by its own folder has no " +
                    ".mlqt/settings.json."))
                : (null, null);

        return (null, new ToolError(
            "Multiple repositories are loaded — specify repositoryId. Repositories: " +
            string.Join(", ", repos.Select(r => $"'{r.Name}' (id {r.Id})")) + "."));
    }

    private StyleCheckingSettings RepoSettingsForClass(string classId)
        => RepositorySettings.ForClass(_libraries, _repositories, classId) ?? new StyleCheckingSettings();

    private StyleCheckingSettings RepoSettingsForLibrary(LoadedLibrary library)
        => RepositorySettings.ForLibrary(_repositories, library) ?? new StyleCheckingSettings();

    /// <summary>
    /// The one loaded repository, or null when there is not exactly one — the scope a stateless
    /// snippet check falls back to.
    /// </summary>
    /// <remarks>
    /// Returns the repository rather than just its settings, because a caller needs both halves of it
    /// and taking them from different places is what B166 was: the rules came from the repository and
    /// the accepted spellings did not, so a word the team had accepted was reported as a misspelling.
    /// </remarks>
    private Repository? SingleRepository()
        => _repositories.Repositories.Count == 1 ? _repositories.Repositories[0] : null;

    /// <summary>
    /// One finding, with its class-relative line mapped to the line in the file it sits in. A caller
    /// given a path and a line will edit that line, so the two have to be counted from the same place.
    /// </summary>
    private FindingItem Item(
        string modelId, string category, string severity, int modelLine,
        string summary, string details, string source, string? ruleId, FindingStatus? status)
    {
        var location = ClassFileLocation.Of(_libraries.CombinedGraph, _libraries.GetModelById(modelId));

        return new FindingItem(
            modelId, category, severity,
            location?.FileLine(modelLine) ?? modelLine, modelLine,
            summary, details, source, location?.FilePath, ruleId, status is { } known ? WireNames.Of(known) : null);
    }

    /// <summary>The description of the <c>baseline</c> parameter, the same on every tool that takes it.</summary>
    internal const string BaselineParameter =
        "Against the repository's committed baseline (.mlqt/baseline.json): 'all' (default) every finding, " +
        "each with its status; 'new' only findings the baseline does not hold - what a change introduced; " +
        "'changed' those plus accepted debt in classes the working copy has changed. With no baseline, " +
        "every finding is shown and none has a status.";

    private static ToolError? BaselineViewError(string? view) =>
        view is null || BaselineViews.Contains(view)
            ? null
            : new ToolError($"'{view}' is not a baseline view. Views: {string.Join(", ", BaselineViews)}.");

    /// <summary>
    /// Whether a view shows a finding of this status. One with no status - no baseline holds its
    /// class - is shown by every view: "cannot be classified" is not "already accepted", which is the
    /// desktop app's rule too (<see cref="BaselineStatusSnapshot.IsChangedFromBaseline"/>).
    /// </summary>
    private static bool Shows(string? view, string? status) => view switch
    {
        "new" => status is null or nameof(FindingStatus.New),
        "changed" => status is not nameof(FindingStatus.AcceptedDebt),
        _ => true,
    };

    /// <summary>
    /// The baseline classification as it stands now, or null when no loaded repository has a baseline.
    /// Refreshed for each call rather than read from the last refresh: the caller has usually just
    /// written the files, and which classes are pending commit is half the answer. The classification
    /// is the desktop app's, so the two cannot disagree about a finding.
    /// </summary>
    /// <remarks>
    /// The working-copy status is kept for five minutes and cleared by the file monitor, which this
    /// server never starts. Without clearing it here, a class edited after the first check read as
    /// untouched debt until the five minutes were up.
    /// </remarks>
    private BaselineStatusSnapshot? CurrentBaseline()
    {
        if (_baselineStatus is null)
            return null;

        _repositories.InvalidateWorkingCopyCache();
        _baselineStatus.Refresh();
        var snapshot = _baselineStatus.Snapshot;
        return snapshot.HasBaseline ? snapshot : null;
    }

    private static CheckResult ToCheckResult(
        IReadOnlyList<LogMessage> findings, int modelsChecked, BaselineStatusSnapshot? baseline, string? view)
    {
        var classified = findings
            .Select(f => (Finding: f, Status: baseline?.StatusOf(f) is { } status ? WireNames.Of(status) : null))
            .ToList();
        // Filtered before the cap, so a library carrying thousands of accepted findings still returns
        // the few new ones rather than the first 200 of its debt.
        var kept = classified.Where(c => Shows(view, c.Status)).ToList();
        var shown = kept
            .Take(MaxReturnedFindings)
            .Select(c => new StyleFindingDto(c.Finding.ModelName, c.Finding.Severity, c.Finding.LineNumber,
                c.Finding.Summary, c.Finding.Details,
                string.IsNullOrEmpty(c.Finding.Source) ? LogMessage.StyleCheckingSource : c.Finding.Source,
                c.Finding.RuleId, c.Status))
            .ToList();

        var summary = baseline is null
            ? null
            : new BaselineSummary(
                // A class in a repository with no baseline counts as new, as the CLI counts every
                // finding of a library it has no baseline for.
                classified.Count(c => c.Status is null or nameof(FindingStatus.New)),
                classified.Count(c => c.Status is nameof(FindingStatus.TouchedDebt)),
                classified.Count(c => c.Status is nameof(FindingStatus.AcceptedDebt)),
                classified.Count - kept.Count);
        return new CheckResult(modelsChecked, kept.Count, shown, kept.Count > shown.Count, summary);
    }
}

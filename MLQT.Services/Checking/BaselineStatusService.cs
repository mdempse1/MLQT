using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;
using static MLQT.Services.LoggingService;

namespace MLQT.Services.Checking;

/// <summary>
/// Classifies the desktop app's issue list against each repository's committed baseline, so a user
/// can see only what their working copy has changed rather than the whole standing debt.
///
/// "Touched" here means <b>modified in the working copy and not yet committed</b> — deliberately not
/// the CLI's commit-to-commit <c>--changed-from</c>. In the app the question a user is asking is "what
/// have I done to this library right now", and the answer must not depend on which commit they happen
/// to be sitting on.
/// </summary>
public interface IBaselineStatusService
{
    /// <summary>True when at least one loaded repository has a baseline to compare against.</summary>
    bool HasBaseline { get; }

    /// <summary>Number of files whose changes are pending commit, across all repositories.</summary>
    int TouchedFileCount { get; }

    /// <summary>
    /// Where the issue stands relative to its repository's baseline, or <c>null</c> when there is no
    /// baseline for it — in which case the caller should show it rather than hide it, since "not
    /// classifiable" is not the same as "already accepted".
    /// </summary>
    FindingStatus? StatusOf(LogMessage message);

    /// <summary>
    /// The current classification. Read it once and use that instance for a whole pass — it never
    /// changes under you, where repeated calls through this interface could straddle a refresh.
    /// </summary>
    BaselineStatusSnapshot Snapshot { get; }

    /// <summary>Reloads the baselines and re-reads which files are pending commit.</summary>
    void Refresh();

    /// <summary>Raised after <see cref="Refresh"/> changes anything a view is showing.</summary>
    event Action? OnChanged;
}

/// <summary>
/// An immutable answer to "where does each issue stand". Built in one go and swapped in atomically, so
/// a view reading it while a refresh runs sees the old state or the new one, never a half-built mix.
/// </summary>
public sealed class BaselineStatusSnapshot
{
    public static readonly BaselineStatusSnapshot Empty =
        new(new Dictionary<string, Baseline>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), 0);

    private readonly IReadOnlyDictionary<string, Baseline> _baselineByModel;
    private readonly IReadOnlySet<string> _touchedModels;

    public BaselineStatusSnapshot(
        IReadOnlyDictionary<string, Baseline> baselineByModel,
        IReadOnlySet<string> touchedModels,
        int touchedFileCount)
    {
        _baselineByModel = baselineByModel;
        _touchedModels = touchedModels;
        TouchedFileCount = touchedFileCount;
    }

    public bool HasBaseline => _baselineByModel.Count > 0;

    public int TouchedFileCount { get; }

    /// <inheritdoc cref="IBaselineStatusService.StatusOf"/>
    public FindingStatus? StatusOf(LogMessage message)
    {
        if (message is null || !_baselineByModel.TryGetValue(message.ModelName, out var baseline))
            return null;

        if (!baseline.ContainsFingerprint(message.Fingerprint))
            return FindingStatus.New;

        return _touchedModels.Contains(message.ModelName)
            ? FindingStatus.TouchedDebt
            : FindingStatus.AcceptedDebt;
    }

    /// <summary>True when the issue is something this working copy changed — new, or standing debt in
    /// a file waiting to be committed. Unclassifiable issues count as worth showing: "no baseline for
    /// it" is not the same as "already accepted".</summary>
    public bool IsChangedFromBaseline(LogMessage message)
        => StatusOf(message) is not FindingStatus.AcceptedDebt;

    /// <summary>The models living in <paramref name="absolutePaths"/>, by graph file node.</summary>
    public static HashSet<string> ModelsInFiles(DirectedGraph graph, IEnumerable<string> absolutePaths)
    {
        var wanted = absolutePaths.Select(NormalizePath).ToHashSet(PathComparer);
        var models = new HashSet<string>(StringComparer.Ordinal);
        if (wanted.Count == 0)
            return models;

        foreach (var file in graph.FileNodes)
        {
            if (!wanted.Contains(NormalizePath(file.FilePath)))
                continue;
            foreach (var model in graph.GetModelsInFile(file.Id))
                models.Add(model.Id);
        }
        return models;
    }

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}

/// <inheritdoc cref="IBaselineStatusService"/>
public sealed class BaselineStatusService : IBaselineStatusService
{
    // Reading working-copy status hits the VCS. File activity arrives in bursts (formatting a
    // repository writes thousands of files), so refreshes are throttled with a trailing edge: the
    // first is immediate, the rest collapse into one that runs after the burst.
    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromSeconds(1);

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;

    private volatile BaselineStatusSnapshot _snapshot = BaselineStatusSnapshot.Empty;

    // Identity of the last snapshot announced, so a refresh that changes nothing stays quiet.
    private string? _signature;
    private long _lastRefreshTicks;
    private int _trailingRefreshQueued;

    public BaselineStatusService(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        IFileMonitoringService fileMonitoring)
    {
        _libraries = libraries;
        _repositories = repositories;

        // Self-maintaining: the answer depends on the loaded libraries and on what the working copy
        // has pending, so it follows both rather than relying on every caller to remember.
        //
        // Throttled rather than immediate, because a repository load adds its libraries one at a
        // time and each refresh runs a VCS status query per repository — work thrown away as soon as
        // the next library arrives.
        _libraries.OnLibrariesChanged += RefreshThrottled;
        fileMonitoring.OnRepositoryFileActivity += _ => RefreshThrottled();

        // A commit changes no file content, so the file monitor never fires for it — but it does
        // change which models are pending commit, which is half of this classification.
        _repositories.OnWorkingCopyStatusChanged += _ => RefreshThrottled();

        // The one that actually matters at startup. A library is attached to its repository *after*
        // it finishes loading, so every refresh triggered while loading was in progress looked at a
        // repository that owned no models yet, found no baseline for it, and concluded there was
        // none. Nothing fired again once the last library was attached, so the answer stayed "no
        // baseline" until something called Refresh directly — which is what opening the Code Review
        // tab does, and why leaving it and coming back appeared to fix the count.
        _repositories.OnRepositoryLoadStateChanged += (_, isLoading) =>
        {
            if (!isLoading)
                Refresh();
        };
    }

    /// <summary>Refreshes at most once per <see cref="RefreshThrottle"/>, with a trailing run so the
    /// state after a burst is never the state from the middle of it.</summary>
    private void RefreshThrottled()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastRefreshTicks);

        if (now - last >= RefreshThrottle.TotalMilliseconds)
        {
            Interlocked.Exchange(ref _lastRefreshTicks, now);
            Refresh();
            return;
        }

        if (Interlocked.CompareExchange(ref _trailingRefreshQueued, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(RefreshThrottle);
            Interlocked.Exchange(ref _trailingRefreshQueued, 0);
            Interlocked.Exchange(ref _lastRefreshTicks, Environment.TickCount64);
            Refresh();
        });
    }

    /// <summary>The current classification. Read it once and use that instance — it never changes
    /// under you, and a refresh swaps in a new one.</summary>
    public BaselineStatusSnapshot Snapshot => _snapshot;

    public bool HasBaseline => _snapshot.HasBaseline;

    public int TouchedFileCount => _snapshot.TouchedFileCount;

    public event Action? OnChanged;

    public FindingStatus? StatusOf(LogMessage message) => _snapshot.StatusOf(message);

    public void Refresh()
    {
        var baselineByModel = new Dictionary<string, Baseline>(StringComparer.Ordinal);
        var touchedModels = new HashSet<string>(StringComparer.Ordinal);
        var touchedFiles = 0;

        foreach (var repository in _repositories.Repositories)
        {
            var models = ModelsOf(repository);
            if (models.Count == 0)
                continue;

            var baseline = LoadBaseline(repository);
            if (baseline is not null)
                foreach (var modelId in models)
                    baselineByModel[modelId] = baseline;

            var (changedModels, changedFiles) = PendingCommit(repository);
            touchedModels.UnionWith(changedModels);
            touchedFiles += changedFiles;
        }

        _snapshot = new BaselineStatusSnapshot(baselineByModel, touchedModels, touchedFiles);

        // Only wake the UI when something it displays actually moved — but "what it displays"
        // includes every input to the classification, not just the two headline numbers.
        //
        // Comparing HasBaseline and TouchedFileCount alone missed the case that matters most: which
        // models are pending commit can change while the file count does not, and a re-run of
        // `mlqt baseline update` changes the baseline itself while both stay identical. The snapshot
        // was then correctly replaced and silently never shown, so a view kept rendering the old
        // classification until something unrelated re-rendered it — which is why leaving the Code
        // Review tab and coming back "fixed" the count.
        var signature = Signature(baselineByModel, touchedModels, touchedFiles);
        if (!string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            _signature = signature;
            OnChanged?.Invoke();
        }
    }

    /// <summary>
    /// A cheap identity for everything the classification depends on. The touched models are folded
    /// order-independently rather than sorted — this runs after a VCS status query, and the point is
    /// to be cheaper than the refresh that produced it, not to be a cryptographic digest.
    /// </summary>
    private static string Signature(
        IReadOnlyDictionary<string, Baseline> baselineByModel,
        IReadOnlySet<string> touchedModels,
        int touchedFiles)
    {
        var touchedFold = 0;
        foreach (var model in touchedModels)
            touchedFold ^= StringComparer.Ordinal.GetHashCode(model);

        var baselineFold = 0;
        foreach (var (modelId, baseline) in baselineByModel)
        {
            // The baseline instance is reloaded from disk on every refresh, so identity would always
            // differ; its entry count moves whenever the committed baseline is regenerated, which is
            // the change a view needs to hear about.
            baselineFold ^= StringComparer.Ordinal.GetHashCode(modelId) * 397 ^ baseline.Entries.Count;
        }

        return $"{baselineByModel.Count}|{touchedModels.Count}|{touchedFiles}|{touchedFold}|{baselineFold}";
    }

    private IReadOnlyList<string> ModelsOf(Repository repository) =>
        _libraries.Libraries
            .Where(l => l.RepositoryId == repository.Id)
            .SelectMany(l => l.ModelIds)
            .ToList();

    /// <summary>The repository's committed baseline, or null when it has none or cannot be read.</summary>
    private static Baseline? LoadBaseline(Repository repository)
    {
        if (string.IsNullOrEmpty(repository.LocalPath))
            return null;

        var path = Path.Combine(repository.LocalPath, ".mlqt", "baseline.json");
        if (!File.Exists(path))
            return null;

        try
        {
            return Baseline.Load(path);
        }
        catch (Exception ex)
        {
            // A malformed baseline must not break the issues list; it just means nothing classifies.
            Error("BaselineStatusService", $"Could not read baseline '{path}'", ex);
            return null;
        }
    }

    /// <summary>
    /// The models living in files the working copy has changed — modified, added, renamed, untracked
    /// or conflicted. Deletions are excluded: their models are gone from the graph, so there is
    /// nothing left to classify.
    /// </summary>
    private (HashSet<string> Models, int FileCount) PendingCommit(Repository repository)
    {
        var models = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(repository.VcsRootPath))
            return (models, 0);

        List<VcsWorkingCopyFile> changes;
        try
        {
            changes = _repositories.GetWorkingCopyChanges(repository.Id);
        }
        catch (Exception ex)
        {
            Error("BaselineStatusService", $"Could not read working copy status for {repository.Name}", ex);
            return (models, 0);
        }

        var changedPaths = changes
            .Where(c => c.Status != VcsFileStatus.Deleted)
            .Where(c => c.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            .Select(c => Path.Combine(repository.VcsRootPath, c.Path))
            .ToList();

        return (BaselineStatusSnapshot.ModelsInFiles(_libraries.CombinedGraph, changedPaths), changedPaths.Count);
    }
}

using System.IO;
using MLQT.Shared.Helpers;
using ModelicaParser.Comparison;
using RevisionControl;

namespace MLQT.Shared.Components;

public partial class LibraryBrowser : IDisposable
{
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private IModelChangeClassifier ModelChangeClassifier { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Parameter]
    public bool LibraryOnly { get; set; } = false;

    [Parameter]
    public Repository? Repository { get; set; }

    private List<TreeItemData<ModelNode>> TreeItems { get; set; } = new();
    private HashSet<ModelNode> _selectedNodes = new();
    private string _currentModelName = "";
    private bool _isExpanded = true;
    private bool _isLoading;
    private bool _hasUncommittedChanges;
    private bool _showGitMenuDialog = false;
    private bool _isPushing = false;
    private readonly DialogOptions _dialogOptions = new() { FullWidth = true };

    /// <summary>
    /// Tracks previous parameter values to avoid expensive RefreshTreeItems
    /// on every parent re-render (e.g. when MainLayout.StateHasChanged fires on model change).
    /// </summary>
    private bool _lastLibraryOnly;
    private string? _lastRepositoryId;
    private bool _isInitialized;

    /// <summary>
    /// Tracks which nodes are currently expanded by their model ID.
    /// Used to preserve expansion state when the tree is refreshed.
    /// </summary>
    private HashSet<string> _expandedNodeIds = new();

    /// <summary>
    /// When true, event-driven tree refreshes (from OnTreeDataChanged) are suppressed.
    /// Set during batch VCS operations (revert, branch switch) to prevent multiple
    /// rapid tree rebuilds that cause MudTreeView to flicker and lose expansion state.
    /// </summary>
    private bool _suppressTreeRefresh;

    /// <summary>
    /// Debounce timestamp for OnRepositoryFileActivity — prevents hammering SVN/Git status
    /// on every file change event when many files change rapidly.
    /// </summary>
    private long _lastFileActivityTicks;
    private static readonly long FileActivityDebounceMs = 2000;

    /// <summary>
    /// Maps model IDs to their VCS file status for directly modified models.
    /// </summary>
    private Dictionary<string, VcsFileStatus> _modelVcsStatus = new();

    /// <summary>
    /// What kind of change each model in a changed file carries (B191), by model ID. A model that is
    /// absent was never classified — a repository outside version control, or one whose committed
    /// version could not be read — which is a different thing from
    /// <see cref="ClassChangeKind.Unchanged"/>, and the marker says so.
    /// </summary>
    private IReadOnlyDictionary<string, ClassChangeKind> _modelChangeKinds =
        new Dictionary<string, ClassChangeKind>();

    /// <summary>
    /// The strongest change kind anywhere below each model ID, so a package can say what is waiting
    /// under it without being expanded. <see cref="ClassChangeKind.Unknown"/> is the entry for a
    /// descendant that changed in a way nothing could classify.
    /// </summary>
    private Dictionary<string, ClassChangeKind> _descendantChangeKinds = new();

    /// <summary>
    /// Set of model IDs whose descendants have recorded parser errors. Used to bubble the
    /// error indicator up parent packages so the user can navigate down to find the problem
    /// model without having to expand every branch.
    /// </summary>
    // Shared with every other tree — the set is a property of the project, not of this repository,
    // and the service works it out once (B258).
    private IReadOnlySet<string> _modelsWithDescendantParserErrors = new HashSet<string>();

    //Menu icons - https://www.svgrepo.com/vectors/git
    const string _rebaseIcon = @"<svg width=""24"" height=""24"" viewBox=""0 -960 960 960"" fill=""currentColor"">
            <path d=""m430-30-56-57 73-73H313q-13 35-43.5 57.5T200-80q-50 0-85-35t-35-85q0-39 22.5-69.5T160-313v-334q-35-13-57.5-43.5T80-760q0-50 35-85t85-35q39 0 69.5 22.5T313-800h134l-73-73 56-57 170 170-170 170-56-57 73-73H313q-9 26-28 45t-45 28v334q26 9 45 28t28 45h134l-73-73 56-57 170 170L430-30Zm245-85q-35-35-35-85 0-40 22.5-70.5T720-313v-334q-35-12-57.5-42.5T640-760q0-50 35-85t85-35q50 0 85 35t35 85q0 40-22.5 70.5T800-647v334q35 13 57.5 43.5T880-200q0 50-35 85t-85 35q-50 0-85-35Zm-475-45q17 0 28.5-11.5T240-200q0-17-11.5-28.5T200-240q-17 0-28.5 11.5T160-200q0 17 11.5 28.5T200-160Zm560 0q17 0 28.5-11.5T800-200q0-17-11.5-28.5T760-240q-17 0-28.5 11.5T720-200q0 17 11.5 28.5T760-160ZM200-720q17 0 28.5-11.5T240-760q0-17-11.5-28.5T200-800q-17 0-28.5 11.5T160-760q0 17 11.5 28.5T200-720Zm560 0q17 0 28.5-11.5T800-760q0-17-11.5-28.5T760-800q-17 0-28.5 11.5T720-760q0 17 11.5 28.5T760-720ZM200-200Zm560 0ZM200-760Zm560 0Z""/>
        </svg>";
    const string _pushIcon = @"<svg width=""24"" height=""24"" viewBox=""0 -960 960 960"" fill=""currentColor"">
            <path d=""M320-160v-79h80v-481h-80v-80h80q33 0 56.5 23.5T480-720v480q0 33-23.5 56.5T400-160h-80Zm320 0q-33 0-56.5-23.5T560-240v-480q0-33 23.5-56.5T640-800h200q33 0 56.5 23.5T920-720v480q0 33-23.5 56.5T840-160H640Zm0-79h200v-481H640v481Zm-440-81-57-56 63-64H40v-80h166l-63-63 57-57 160 160-160 160Zm440 81v-481 481Z""/>
        </svg>";
    const string _pullRequestIcon = @"<svg width=""24"" height=""24"" viewBox=""0 0 24 24"" fill=""currentColor"">
            <path style=""stroke:none;fill-rule:nonzero;fill-opacity:1;"" d=""M 18.46875 16.542969 L 18.46875 4.5 C 18.46875 4.292969 18.300781 4.125 18.09375 4.125 L 14.109375 4.125 L 14.109375 2.511719 C 14.109375 2.371094 13.945312 2.292969 13.835938 2.378906 L 10.84375 4.742188 C 10.804688 4.773438 10.78125 4.820312 10.78125 4.875 C 10.78125 4.925781 10.804688 4.972656 10.84375 5.007812 L 13.832031 7.367188 C 13.941406 7.457031 14.105469 7.378906 14.105469 7.234375 L 14.105469 5.625 L 16.777344 5.625 L 16.777344 16.542969 C 15.742188 16.894531 14.996094 17.878906 14.996094 19.03125 C 14.996094 20.480469 16.175781 21.65625 17.621094 21.65625 C 19.070312 21.65625 20.246094 20.480469 20.246094 19.03125 C 20.25 17.878906 19.503906 16.898438 18.46875 16.542969 Z M 17.625 20.15625 C 17.011719 20.144531 16.523438 19.644531 16.523438 19.03125 C 16.523438 18.417969 17.011719 17.917969 17.625 17.90625 C 18.238281 17.917969 18.726562 18.417969 18.726562 19.03125 C 18.726562 19.644531 18.238281 20.144531 17.625 20.15625 Z M 9 4.96875 C 9 3.519531 7.824219 2.34375 6.375 2.34375 C 4.925781 2.34375 3.75 3.519531 3.75 4.96875 C 3.75 6.121094 4.496094 7.101562 5.53125 7.457031 L 5.53125 16.546875 C 4.496094 16.898438 3.75 17.878906 3.75 19.035156 C 3.75 20.480469 4.925781 21.660156 6.375 21.660156 C 7.824219 21.660156 9 20.480469 9 19.035156 C 9 17.878906 8.253906 16.902344 7.21875 16.546875 L 7.21875 7.457031 C 8.253906 7.101562 9 6.121094 9 4.96875 Z M 5.25 4.96875 C 5.261719 4.355469 5.761719 3.867188 6.375 3.867188 C 6.988281 3.867188 7.488281 4.355469 7.5 4.96875 C 7.488281 5.582031 6.988281 6.070312 6.375 6.070312 C 5.761719 6.070312 5.261719 5.582031 5.25 4.96875 Z M 7.5 19.03125 C 7.488281 19.644531 6.988281 20.132812 6.375 20.132812 C 5.761719 20.132812 5.261719 19.644531 5.25 19.03125 C 5.261719 18.417969 5.761719 17.929688 6.375 17.929688 C 6.988281 17.929688 7.488281 18.417969 7.5 19.03125 Z M 7.5 19.03125""/>
        </svg>";

    protected override void OnInitialized()
    {
        NavState.OnChangeModel += OnModelChanged;
        NavState.OnEnableMultiSelect += OnSelectionModeChanged;
        NavState.OnSelectedModelsChanged += OnExternalSelectedModelsChanged;
        NavState.OnVcsFilesChanged += OnVcsFilesChangedHandler;
        LibraryDataService.OnTreeDataChanged += OnTreeDataChanged;
        RepositoryService.OnRepositoryLoadStateChanged += OnRepositoryLoadStateChanged;
        FileMonitoringService.OnRepositoryFileActivity += OnRepositoryFileActivity;
        base.OnInitialized();
    }

    protected override async Task OnParametersSetAsync()
    {
        // Skip expensive refresh if parameters haven't changed.
        // OnParametersSetAsync fires on every parent re-render (e.g. MainLayout.StateHasChanged
        // on model click) even when LibraryBrowser's parameters are unchanged.
        var repoId = Repository?.Id;
        if (_isInitialized && LibraryOnly == _lastLibraryOnly && repoId == _lastRepositoryId)
            return;

        _isInitialized = true;
        _lastLibraryOnly = LibraryOnly;
        _lastRepositoryId = repoId;

        await CheckForUncommittedChangesAsync();
        await RefreshTreeItems();
    }

    /// <summary>
    /// Checks if the repository has uncommitted changes, updates the _hasUncommittedChanges field,
    /// and builds the model-to-VCS-status and model-to-change-kind mappings for tree annotations.
    /// </summary>
    private async Task CheckForUncommittedChangesAsync()
    {
        if (Repository == null || Repository.VcsType == RepositoryVcsType.Local)
        {
            _hasUncommittedChanges = false;
            _modelVcsStatus.Clear();
            _modelChangeKinds = new Dictionary<string, ClassChangeKind>();
            _descendantChangeKinds.Clear();
            return;
        }

        var repository = Repository;
        var repoId = repository.Id;
        var vcsRootPath = repository.VcsRootPath;
        var graph = LibraryDataService.CombinedGraph;

        // Build VCS status mapping on background thread to avoid blocking UI
        // when the working copy cache has expired and SVN status must be queried.
        // Classifying the changes belongs on the same thread for the same reason, and more so: it
        // reads each changed file's committed version out of the repository and parses it.
        var (hasChanges, modelStatus, kinds, descendants) = await Task.Run(() =>
        {
            var changes = RepositoryService.GetWorkingCopyChanges(repoId);
            var status = new Dictionary<string, VcsFileStatus>();

            if (changes == null || changes.Count == 0)
                return (false, status, EmptyKinds, new Dictionary<string, ClassChangeKind>());

            foreach (var change in changes)
            {
                if (!change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                    continue;

                // change.Path is relative to VcsRootPath; normalize separators
                // so Path.Combine works correctly on Windows with Git's forward slashes.
                var nativePath = change.Path.Replace('/', Path.DirectorySeparatorChar);
                var absolutePath = Path.Combine(vcsRootPath, nativePath);
                var fileId = GraphBuilder.GenerateFileId(absolutePath);
                var modelsInFile = graph.GetModelsInFile(fileId);

                foreach (var model in modelsInFile)
                {
                    status[model.Id] = change.Status;
                }
            }

            var changeKinds = ModelChangeClassifier.Classify(repository, changes);
            return (true, status, changeKinds, DescendantKinds(status, changeKinds));
        });

        _hasUncommittedChanges = hasChanges;
        _modelVcsStatus = modelStatus;
        _modelChangeKinds = kinds;
        _descendantChangeKinds = descendants;

        // A filter with nothing left to filter is withdrawn along with its control, or committing
        // while "Affects simulation" is selected leaves the tree hidden behind an empty list and no
        // visible way back to it.
        if (!hasChanges)
            _changeFilter = ChangeFilter.None;
    }

    private static readonly IReadOnlyDictionary<string, ClassChangeKind> EmptyKinds =
        new Dictionary<string, ClassChangeKind>();

    /// <summary>
    /// The strongest change kind anywhere below each ancestor of a changed model.
    /// </summary>
    /// <remarks>
    /// <para>Walks up each changed model's dotted name adding it to every ancestor, and keeps the
    /// highest kind seen — which is what <see cref="ClassChangeKind"/>'s ordering is for. A package
    /// with one reformatted class and one changed equation under it reports the equation, because
    /// that is the one the user has to go and look at.</para>
    ///
    /// <para>Driven by the VCS status rather than by the kinds, so a repository whose changes could
    /// not be classified still bubbles a marker up its packages — which is what MLQT did for
    /// everything before B191, and is still the right answer when nothing better is known.</para>
    /// </remarks>
    internal static Dictionary<string, ClassChangeKind> DescendantKinds(
        IReadOnlyDictionary<string, VcsFileStatus> status,
        IReadOnlyDictionary<string, ClassChangeKind> kinds)
    {
        var descendants = new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);

        foreach (var modelId in status.Keys)
        {
            // A class that is in a changed file but was not itself changed contributes nothing to
            // its ancestors - otherwise every package above a modified package.mo would claim a
            // change that is not there.
            var kind = kinds.TryGetValue(modelId, out var known) ? known : ClassChangeKind.Unknown;
            if (kind == ClassChangeKind.Unchanged)
                continue;

            var lastDot = modelId.LastIndexOf('.');
            while (lastDot > 0)
            {
                var parentId = modelId.Substring(0, lastDot);
                if (!descendants.TryGetValue(parentId, out var existing) || kind > existing)
                    descendants[parentId] = kind;

                lastDot = parentId.LastIndexOf('.');
            }
        }

        return descendants;
    }

    /// <summary>
    /// Which of a repository's models the browser lists (B191).
    /// </summary>
    internal enum ChangeFilter
    {
        /// <summary>The ordinary tree, everything in it.</summary>
        None,

        /// <summary>Every class with an uncommitted change of its own.</summary>
        Changed,

        /// <summary>
        /// The changes a reviewer has to read. Anything not <i>known</i> to be harmless is in here,
        /// including a class MLQT could not compare — leaving those out would be a filter that hides
        /// exactly what it was asked to find.
        /// </summary>
        AffectsSimulation,

        /// <summary>Changes MLQT is prepared to vouch for as layout, wording or graphics.</summary>
        Cosmetic,
    }

    private ChangeFilter _changeFilter = ChangeFilter.None;

    /// <summary>
    /// The classes the current filter selects, with the marker each one would carry in the tree.
    /// </summary>
    /// <remarks>
    /// <para><b>A flat list rather than a pruned tree.</b> The tree loads its children on demand, so
    /// "only the changed classes" would mean expanding every package to find out whether it has any —
    /// which is the whole library. The list is also what the question asks for: which classes did I
    /// change, and which of those matter.</para>
    ///
    /// <para>Driven by the VCS status map, so a class is listed because its file changed, and the
    /// filter then decides on the kind. A class in a changed file that was not itself touched is
    /// not listed at all.</para>
    /// </remarks>
    internal IReadOnlyList<(ModelNode Model, ChangeMarker Marker)> FilteredChanges()
    {
        if (_changeFilter == ChangeFilter.None)
            return [];

        var graph = LibraryDataService.CombinedGraph;
        var matches = new List<(ModelNode Model, ChangeMarker Marker)>();

        foreach (var modelId in _modelVcsStatus.Keys)
        {
            var kind = _modelChangeKinds.TryGetValue(modelId, out var known) ? known : ClassChangeKind.Unknown;
            if (!Selects(_changeFilter, kind))
                continue;

            var model = graph.GetNode<ModelNode>(modelId);
            if (model is null)
                continue;

            var marker = MarkerFor(model);
            if (marker.Shape == ChangeMarkerShape.Chip)
                matches.Add((model, marker));
        }

        return matches.OrderBy(m => m.Model.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Whether a filter wants a class of this kind.</summary>
    internal static bool Selects(ChangeFilter filter, ClassChangeKind kind) => filter switch
    {
        ChangeFilter.Changed => kind != ClassChangeKind.Unchanged,
        ChangeFilter.AffectsSimulation => kind is not (ClassChangeKind.Unchanged or ClassChangeKind.Cosmetic),
        ChangeFilter.Cosmetic => kind == ClassChangeKind.Cosmetic,
        _ => false,
    };

    internal void OnChangeFilterChanged(ChangeFilter filter)
    {
        _changeFilter = filter;
        StateHasChanged();
    }

    /// <summary>
    /// What to draw beside a model in the tree — the one decision, asked by both tree templates.
    /// </summary>
    internal ChangeMarker MarkerFor(ModelNode? model)
    {
        if (model?.Id is not { } id)
            return ChangeMarker.None;

        var status = _modelVcsStatus.TryGetValue(id, out var fileStatus) ? fileStatus : (VcsFileStatus?)null;
        var kind = _modelChangeKinds.TryGetValue(id, out var own) ? own : ClassChangeKind.Unknown;
        var descendants = _descendantChangeKinds.TryGetValue(id, out var below) ? below : ClassChangeKind.Unchanged;

        return ChangeMarker.For(status, kind, descendants);
    }

    /// <summary>
    /// Annotates tree items with VCS file status and descendant change markers
    /// from the cached mappings.
    /// ModelNode instances are shared/cached in the graph, so stale flags from a previous
    /// annotation pass are explicitly cleared before re-annotating.
    /// </summary>
    /// <param name="items">The tree items to annotate.</param>
    private void AnnotateVcsStatus(IEnumerable<TreeItemData<ModelNode>> items)
    {
        foreach (var item in items)
        {
            if (item.Value == null)
                continue;

            // Reset first — the same ModelNode instance may have been annotated previously
            item.Value.FileStatus = null;
            item.Value.HasDescendantChanges = false;

            if (_modelVcsStatus.TryGetValue(item.Value.Id, out var status))
            {
                item.Value.FileStatus = status;
            }

            if (_descendantChangeKinds.ContainsKey(item.Value.Id))
            {
                item.Value.HasDescendantChanges = true;
            }
        }
    }

    public void Dispose()
    {
        NavState.OnChangeModel -= OnModelChanged;
        NavState.OnEnableMultiSelect -= OnSelectionModeChanged;
        NavState.OnSelectedModelsChanged -= OnExternalSelectedModelsChanged;
        NavState.OnVcsFilesChanged -= OnVcsFilesChangedHandler;
        LibraryDataService.OnTreeDataChanged -= OnTreeDataChanged;
        RepositoryService.OnRepositoryLoadStateChanged -= OnRepositoryLoadStateChanged;
        FileMonitoringService.OnRepositoryFileActivity -= OnRepositoryFileActivity;
    }

    /// <summary>
    /// Fired after a VCS operation (merge+commit, update, revert, switch) completes and
    /// the analysis pipeline runs. Re-checks uncommitted changes so that any stale
    /// HasDescendantChanges indicators (e.g. set during pre-commit phase) are cleared.
    /// </summary>
    private async void OnVcsFilesChangedHandler(string repositoryId)
    {
        if (Repository?.Id != repositoryId) return;
        await InvokeAsync(async () =>
        {
            // Rebuild the VCS status mapping from the current working copy state.
            // After a successful commit this will be empty, clearing stale annotations.
            await CheckForUncommittedChangesAsync();
            // Refresh top-level tree items to get fresh ModelNode objects (HasDescendantChanges=false)
            // and re-annotate them from the now-updated status mapping.
            await RefreshTreeItems();
            StateHasChanged();
        });
    }

    /// <summary>
    /// Fired by the file monitor for any file activity in the repository directory, including
    /// non-Modelica files. Debounced to avoid hammering SVN/Git status on rapid changes.
    /// Invalidates the working copy cache and refreshes commit/revert button state.
    /// </summary>
    private async void OnRepositoryFileActivity(string repositoryId)
    {
        if (Repository?.Id != repositoryId) return;

        var now = Environment.TickCount64;
        if (now - _lastFileActivityTicks < FileActivityDebounceMs) return;
        _lastFileActivityTicks = now;

        RepositoryService.InvalidateWorkingCopyCache(repositoryId);
        await InvokeAsync(async () =>
        {
            await CheckForUncommittedChangesAsync();
            StateHasChanged();
        });
    }

    /// <summary>
    /// ServerData callback for lazy loading tree children.
    /// Called by MudTreeView when a node is expanded.
    /// </summary>
    private async Task<IReadOnlyCollection<TreeItemData<ModelNode>>> LoadServerData(ModelNode? parentNode)
    {
        var allItems = ToTreeItems(await LibraryDataService.GetChildModelsAsync(parentNode));

        // If in repository mode, filter to only show items from this repository's libraries
        List<TreeItemData<ModelNode>> items;
        if (Repository != null && parentNode == null)
        {
            items = allItems
                .Where(item => item.Value != null && IsLibraryInRepository(item.Value.LibraryId))
                .ToList();
        }
        else
        {
            items = allItems.ToList();
        }

        // Annotate items with VCS status indicators
        AnnotateVcsStatus(items);

        // Restore expansion state for the loaded items
        RestoreExpansionState(items);

        return items.AsReadOnly();
    }

    /// <summary>
    /// Wraps models from the (UI-agnostic) library data service into MudTreeView items: picks an icon
    /// from the class type and marks the node expandable when the model has children. Children are left
    /// null so they load on demand via ServerData.
    /// </summary>
    private List<TreeItemData<ModelNode>> ToTreeItems(IReadOnlyList<ModelNode> models)
        => models.Select(model => new TreeItemData<ModelNode>
        {
            Value = model,
            Icon = IconForClassType(model.ClassType),
            Expandable = LibraryDataService.ModelHasChildren(model.Id),
            Expanded = false,
            Children = null
        }).ToList();

    private static string IconForClassType(string classType) => classType switch
    {
        "function" => Icons.Material.Filled.Functions,
        "block" => Icons.Material.Filled.ViewModule,
        "connector" => Icons.Material.Filled.Power,
        "record" => Icons.Material.Filled.DataObject,
        "package" => Icons.Material.Filled.FolderOpen,
        _ => Icons.Material.Filled.ModelTraining
    };

    private bool IsLibraryInRepository(string libraryId)
    {
        if (Repository == null)
            return true;

        return Repository.LibraryIds.Contains(libraryId);
    }

    /// <summary>
    /// Called when library data changes to refresh the tree view.
    /// Suppressed during batch VCS operations to prevent rapid tree rebuilds.
    /// </summary>
    private async void OnTreeDataChanged()
    {
        await InvokeAsync(async () =>
        {
            if (_suppressTreeRefresh) return;
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();
            StateHasChanged();
        });
    }

    private void OnRepositoryLoadStateChanged(string repositoryId, bool isLoading)
    {
        if (Repository != null && Repository.Id == repositoryId)
        {
            _isLoading = isLoading;
            InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Refreshes the top-level tree items from the LibraryDataService, preserving the expansion
    /// state of previously expanded nodes — and times each step.
    ///
    /// <para><b>The timing is here because reading did not settle it.</b> Startup stutter was
    /// reported against this path and three plausible causes turned out to be already fixed — the
    /// bulk-load notifications are suppressed to one, the working-copy query is off-thread, and the
    /// tree itself is only top-level. Everything below runs on the dispatcher, which is also the
    /// desktop host's window message pump, so what is wanted is the one that costs tens of
    /// milliseconds and not a fourth guess. B253 was found this way: time the steps and let the next
    /// run say which.</para>
    /// </summary>
    private async Task RefreshTreeItems()
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        var step = System.Diagnostics.Stopwatch.StartNew();

        var allItems = ToTreeItems(await LibraryDataService.GetTopLevelModelsAsync());
        var topLevelMs = step.ElapsedMilliseconds;
        step.Restart();

        if (Repository != null)
        {
            // Filter to only show libraries from this repository
            TreeItems = allItems
                .Where(item => item.Value != null && IsLibraryInRepository(item.Value.LibraryId))
                .OrderBy(item => item.Value?.Name)
                .ToList();
        }
        else
        {
            TreeItems = allItems.OrderBy(item => item.Value?.Name).ToList();
        }

        // Annotate items with VCS status indicators
        AnnotateVcsStatus(TreeItems);
        var annotateMs = step.ElapsedMilliseconds;
        step.Restart();

        // Compute which parent packages contain descendants with parser errors so the
        // warning icon bubbles up the tree and the user can find the problem model.
        RefreshDescendantParserErrors();
        var parserErrorsMs = step.ElapsedMilliseconds;
        step.Restart();

        // Restore expansion state for previously expanded nodes, materialising their children so the
        // rebuilt tree renders a consistent expanded state (icon + children) rather than an "expanded"
        // node with null children that MudTreeView won't auto-load after a programmatic rebuild.
        await RestoreExpansionStateAsync(TreeItems);
        var expansionMs = step.ElapsedMilliseconds;

        // Only when it is worth reading. A tree refresh that costs nothing happens constantly.
        //
        // **"top level" is no longer time on this thread**, and the distinction matters because a
        // number here used to mean a blocked window. `GetTopLevelModelsAsync` runs on the pool now
        // and queues behind any other tree doing the same, so this figure is wall clock — waiting
        // included — while the dispatcher is free. Every other step is still measured here, on the
        // dispatcher, and those are the ones to worry about (B258).
        if (total.ElapsedMilliseconds >= 50)
        {
            var onThisThread = annotateMs + parserErrorsMs + expansionMs;
            LoggingService.Debug(nameof(LibraryBrowser),
                $"Tree refresh for '{Repository?.Name ?? "all"}' took {total.ElapsedMilliseconds}ms "
                + $"({onThisThread}ms of it on the UI thread: annotate {annotateMs}ms, "
                + $"parser errors {parserErrorsMs}ms, expansion {expansionMs}ms; "
                + $"top level {topLevelMs}ms awaited off it)");
        }
    }

    /// <summary>
    /// Rebuilds <see cref="_modelsWithDescendantParserErrors"/> from the current set of
    /// models in the graph. A package id is added to the set if any model whose id begins
    /// with "<packageId>." has parser errors.
    /// </summary>
    private void RefreshDescendantParserErrors()
        => _modelsWithDescendantParserErrors = LibraryDataService.ModelsWithDescendantParserErrors();

    /// <summary>
    /// Builds the hover tooltip for a tree node's parser-error indicator.
    /// </summary>
    private string GetParserErrorTooltip(ModelNode node)
    {
        if (node.HasFatalParseFailure)
            return "This file could not be fully parsed. The contents shown may be incomplete or replaced with a placeholder. See the Findings panel for details.";
        if (node.HasParserErrors)
        {
            var count = node.Definition.ParserErrors.Count;
            return count == 1
                ? "This model has 1 parser error. See the Findings panel for details."
                : $"This model has {count} parser errors. See the Findings panel for details.";
        }
        return "A model inside this package has a parser error.";
    }

    /// <summary>
    /// Recursively restores expansion state for tree items based on _expandedNodeIds.
    /// </summary>
    private void RestoreExpansionState(IEnumerable<ITreeItemData<ModelNode>> items)
    {
        foreach (var item in items)
        {
            if (item.Value != null && _expandedNodeIds.Contains(item.Value.Id))
            {
                item.Expanded = true;
            }

            if (item.Children != null)
            {
                RestoreExpansionState(item.Children);
            }
        }
    }

    /// <summary>
    /// Like <see cref="RestoreExpansionState"/>, but for a programmatic tree rebuild it also
    /// materialises the children of each expanded node (loading them on demand if the rebuilt item
    /// has none). MudTreeView will not auto-invoke its <c>ServerData</c> for a node that is already
    /// marked <c>Expanded</c> when the tree is replaced wholesale, so without this a previously
    /// expanded package renders with the expanded icon but no children until it is clicked — and the
    /// click then collapses it. Pre-loading the children keeps the icon and the rendered rows in sync.
    /// </summary>
    private async Task RestoreExpansionStateAsync(IEnumerable<ITreeItemData<ModelNode>> items)
    {
        foreach (var item in items)
        {
            if (item.Value == null || !_expandedNodeIds.Contains(item.Value.Id))
                continue;

            item.Expanded = true;

            if (item.Children == null && item.Expandable)
            {
                var children = await LoadServerData(item.Value);
                item.Children = children.Cast<ITreeItemData<ModelNode>>().ToList();
            }

            if (item.Children != null)
                await RestoreExpansionStateAsync(item.Children);
        }
    }

    /// <summary>
    /// Called when a tree node's expansion state changes.
    /// Tracks expanded nodes so the state can be preserved during refresh.
    /// </summary>
    private void OnNodeExpandedChanged(ITreeItemData<ModelNode> node, bool expanded)
    {
        node.Expanded = expanded;

        if (node.Value != null)
        {
            if (expanded)
            {
                _expandedNodeIds.Add(node.Value.Id);
            }
            else
            {
                _expandedNodeIds.Remove(node.Value.Id);
            }
        }
    }

    /// <summary>
    /// Persists children loaded on demand by <see cref="LoadServerData"/> back into the
    /// <c>treeItem.Children</c> data tree.
    /// <para>
    /// Required by MudBlazor 9.4+: when a MudTreeView has both <c>ItemTemplate</c> and
    /// <c>Items</c> set, <c>GetSelectableValues()</c> derives the set of selectable values
    /// from the <c>Items</c> data tree (not the rendered components). A child loaded on
    /// demand that isn't written back here is therefore treated as unselectable —
    /// <c>SetSelectedValueAsync</c> resets the selection to default and the click appears to
    /// do nothing. Writing the loaded children back keeps the data tree complete so nested
    /// nodes select on the first click. (In 9.0 selectable values came from the registered
    /// items, so this wiring wasn't needed for selection.)
    /// </para>
    /// </summary>
    private void OnNodeChildrenLoaded(
        ITreeItemData<ModelNode> treeItem, IReadOnlyCollection<ITreeItemData<ModelNode>> children)
    {
        treeItem.Children = children?.ToList();
    }

    private async void OnSelectionModeChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async void OnExternalSelectedModelsChanged()
    {
        SyncSelectedNodes();
        await InvokeAsync(StateHasChanged);
    }

    private void SyncSelectedNodes()
    {
        _selectedNodes = new HashSet<ModelNode>(
            NavState.SelectedModelIDs.Select(id => new ModelNode(id, id)));
    }

    private void OnModelSelected(ModelNode? selectedNode)
    {
        _currentModelName = selectedNode?.Id ?? string.Empty;

        // Flagged across the call because ChangeModelID raises OnChangeModel synchronously, and the
        // handler would otherwise reveal a class the user has this moment clicked on.
        _selectingFromTree = true;
        try
        {
            NavState.ChangeModelID(_currentModelName);
        }
        finally
        {
            _selectingFromTree = false;
        }
    }

    private void OnModelsSelected(IReadOnlyCollection<ModelNode> selectedNodes)
    {
        if (selectedNodes == null || selectedNodes.Count == 0)
        {
            _selectedNodes = new HashSet<ModelNode>();
            NavState.ClearSelectedModels();
            _currentModelName = string.Empty;
        }
        else
        {
            var nonNullNodes = selectedNodes.Where(n => n != null).ToList();
            _selectedNodes = new HashSet<ModelNode>(nonNullNodes);
            var modelIds = nonNullNodes.Select(n => n.Id).ToList();
            NavState.SetSelectedModels(modelIds);
            _currentModelName = modelIds.Count == 1 ? modelIds[0] : $"{modelIds.Count} models selected";
        }
        StateHasChanged();
    }

    private async void OnModelChanged()
    {
        _currentModelName = NavState.ModelID;

        // Opened from somewhere else — a finding, a dependency, the navigation stack — so show where
        // it is (B189). A class opened this way used to appear in the viewer while the tree stayed
        // wherever it was, which left nothing to say what had just been opened or what it sits in.
        if (!_selectingFromTree)
            await RevealAsync(NavState.ModelID);

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Set while this browser is the thing that changed the selection, so the reveal does not fight
    /// the click that caused it: a user who has just collapsed a package and clicked a class
    /// elsewhere should not have it expanded again underneath them.
    /// </summary>
    private bool _selectingFromTree;

    /// <summary>
    /// The ids that have to be open for <paramref name="modelId"/> to be visible, outermost first
    /// and not including the class itself.
    /// </summary>
    /// <remarks>
    /// <para>Walked through <c>ParentModelName</c> rather than by splitting the dotted name, because
    /// containment is what the tree nests by and the two are not always the same thing — a class
    /// reached through a library alias, or one whose name carries dots of its own (a quoted
    /// identifier), would give a chain of packages that do not exist.</para>
    ///
    /// <para>Stops at a name the lookup does not know, and guards against a cycle: a malformed graph
    /// should leave the tree unrevealed, not spin.</para>
    /// </remarks>
    internal static List<string> AncestorChain(string? modelId, Func<string, ModelNode?> lookup)
    {
        var chain = new List<string>();
        if (string.IsNullOrEmpty(modelId))
            return chain;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = lookup(modelId);

        while (current is not null
               && !string.IsNullOrEmpty(current.ParentModelName)
               && seen.Add(current.Id))
        {
            chain.Add(current.ParentModelName!);
            current = lookup(current.ParentModelName!);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Opens the packages above <paramref name="modelId"/> and selects it, if it is in this tree.
    /// </summary>
    /// <remarks>
    /// Several browsers are rendered in repository mode, one per repository, and a class belongs to
    /// one of them — so this returns without touching anything when the chain does not start at one
    /// of this tree's own roots. Otherwise every repository's tree would expand for every class.
    /// </remarks>
    private async Task RevealAsync(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId) || TreeItems.Count == 0)
            return;

        var chain = AncestorChain(modelId, LibraryDataService.GetModelById);
        var rootId = chain.Count > 0 ? chain[0] : modelId;

        if (!TreeItems.Any(item => item.Value?.Id == rootId))
            return;

        foreach (var id in chain)
            _expandedNodeIds.Add(id);

        // The same walk a rebuild uses, and for the same reason: a node marked expanded whose
        // children have never been fetched renders open and empty.
        await RestoreExpansionStateAsync(TreeItems);

        var model = LibraryDataService.GetModelById(modelId);
        if (model is not null)
            _selectedNodes = [model];
    }

    private async Task RefreshRepository()
    {
        if (Repository == null)
            return;

        _isLoading = true;
        StateHasChanged();

        try
        {
            // Pause file monitoring before the VCS update to prevent the flood of file-change
            // events from locking up the UI. The analysis handler (OnVcsFilesChanged) will
            // restart monitoring after formatting is applied.
            FileMonitoringService.StopMonitoring(Repository.Id);

            // Update the repository from the remote if it's a VCS repository
            if (Repository.VcsType != RepositoryVcsType.Local)
            {
                var updateResult = await RepositoryService.UpdateRepositoryAsync(Repository.Id);
                if (!updateResult.Success && !string.IsNullOrEmpty(updateResult.ErrorMessage))
                {
                    Snackbar.Add($"Update failed: {updateResult.ErrorMessage}", Severity.Error);
                }
                else if (updateResult.HasChanges)
                {
                    Snackbar.Add($"Updated to revision {updateResult.NewRevision}", Severity.Success);
                }
                else
                {
                    Snackbar.Add("Already up to date", Severity.Info);
                }
            }

            // Reload library data from disk (re-discover libraries, re-parse changed files).
            // RefreshRepositoryAsync removes and reloads all libraries, so the old expansion
            // state references stale tree items with null Children — clear it to avoid the
            // "expanded but no children visible" MudTreeView glitch.
            await RepositoryService.RefreshRepositoryAsync(Repository.Id);
            _expandedNodeIds.Clear();
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();

            // Trigger background analysis (formatting + dependencies + style + resources).
            // Handler will restart monitoring once formatting is complete.
            NavState.VcsFilesChanged(Repository.Id);
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task ShowHistory() {
        if (Repository == null)
            return;
        var options = new DialogOptions { FullWidth = true };
        var parameters = new DialogParameters<VCSHistory>
        {
            {x => x.RepositoryID, Repository.Id}
        };
        await DialogService.ShowAsync<VCSHistory>("VCS History", parameters, options);
    }

    private async Task ShowSwitchBranchDialog()
    {
        if (Repository == null)
            return;

        var parameters = new DialogParameters<SwitchBranchDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        // Small, not the provider's Large default: the dialog is a list and a sentence, and
        // MudDialog grows to fit its content up to the cap - so selecting a tag, which adds a
        // paragraph explaining detached HEAD, stretched it to most of the screen the moment
        // the user clicked (B193). The content has a width of its own, so this is the belt to
        // that brace.
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small };
        var dialog = await DialogService.ShowAsync<SwitchBranchDialog>("Switch Branch", parameters, options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // SwitchBranchAsync already called RefreshRepositoryAsync internally, which
            // removed and reloaded all libraries from scratch. The old expansion state
            // references stale tree items whose Children are null (lazy-loaded), so
            // setting Expanded=true on them confuses MudTreeView (appears expanded but
            // no children visible; clicking toggle briefly shows then collapses).
            // Clear the expansion state so the tree starts fresh after a branch switch.
            _expandedNodeIds.Clear();
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();
            Snackbar.Add($"Switched to branch: {result.Data}", Severity.Success);
            StateHasChanged();
            NavState.VcsFilesChanged(Repository.Id);
        }
    }

    private async Task ShowCreateBranchDialog()
    {
        if (Repository == null)
            return;

        var parameters = new DialogParameters<CreateBranchDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true };
        var dialog = await DialogService.ShowAsync<CreateBranchDialog>("Create Branch", parameters, options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // Branch was created successfully - refresh repository info
            await RepositoryService.RefreshRepositoryAsync(Repository.Id);
            Snackbar.Add($"Created branch: {result.Data}", Severity.Success);
            StateHasChanged();
        }
    }

    private async Task ShowMergeBranchDialog()
    {
        _showGitMenuDialog = false;
        if (Repository == null)
            return;

        if (Repository.VcsType == RepositoryVcsType.Git)
        {
            var gitParameters = new DialogParameters<GitMergeBranchDialog>
            {
                { x => x.RepositoryId, Repository.Id }
            };
            var gitOptions = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
            var gitDialog = await DialogService.ShowAsync<GitMergeBranchDialog>("Merge Branch", gitParameters, gitOptions);
            var gitResult = await gitDialog.Result;

            if (gitResult != null && !gitResult.Canceled)
            {
                // VcsFilesChanged (formatting + analysis) is fired by GitMergeBranchDialog itself
                // after the merge commit. Refresh the tree to reflect the new state.
                // RefreshRepositoryAsync reloads all libraries — clear stale expansion state.
                await RepositoryService.RefreshRepositoryAsync(Repository.Id);
                _expandedNodeIds.Clear();
                await CheckForUncommittedChangesAsync();
                await RefreshTreeItems();
                StateHasChanged();
            }
            return;
        }

        var parameters = new DialogParameters<MergeBranchDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialog = await DialogService.ShowAsync<MergeBranchDialog>("Merge Branch", parameters, options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // Merge was performed — refresh the tree to show uncommitted merge changes.
            // VcsFilesChanged (formatting + analysis) is fired by MergeBranchDialog itself
            // after the commit dialog closes, so that formatting runs on committed files only.
            // RefreshRepositoryAsync reloads all libraries — clear stale expansion state.
            await RepositoryService.RefreshRepositoryAsync(Repository.Id);
            _expandedNodeIds.Clear();
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();
            StateHasChanged();
        }
    }

    private async Task ShowCommitChangesDialog()
    {
        if (Repository == null)
            return;

        // Format changed files before opening the commit dialog to ensure
        // formatting rules are applied to any files modified since last formatting
        Snackbar.Add("Applying code formatting to changed files...", Severity.Normal);
        await NavState.FormatChangedFilesForCommitAsync(Repository.Id);
        // Invalidate cache so commit dialog gets fresh VCS status after formatting
        RepositoryService.InvalidateWorkingCopyCache(Repository.Id);
        await CheckForUncommittedChangesAsync();

        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialog = await DialogService.ShowAsync<CommitChangesDialog>("Commit Changes", parameters, options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // A commit doesn't change file content, only VCS status — no need to reload libraries.
            // Just invalidate the working copy cache and refresh the tree status markers.
            RepositoryService.InvalidateWorkingCopyCache(Repository.Id);
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();
            Snackbar.Add("Changes committed successfully.", Severity.Success);
            StateHasChanged();
        }
    }

    private async Task ShowRevertFilesDialog()
    {
        if (Repository == null)
            return;

        var parameters = new DialogParameters<RevertFilesDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };

        // Suppress event-driven tree refreshes for the entire dialog lifetime.
        // RevertFilesAsync calls RefreshRepositoryAsync internally before the dialog
        // closes, firing multiple OnTreeDataChanged events. Without suppression those
        // events each replace TreeItems while MudTreeView is mid-load, causing the
        // "expand → briefly shows children → collapses" flicker.
        _suppressTreeRefresh = true;
        DialogResult? result;
        try
        {
            var dialog = await DialogService.ShowAsync<RevertFilesDialog>("Revert Files", parameters, options);
            result = await dialog.Result;
        }
        finally
        {
            _suppressTreeRefresh = false;
        }

        if (result != null && !result.Canceled)
        {
            var revertedRelativePaths = result.Data as List<string> ?? new List<string>();

            // Reload only the reverted .mo files in the graph — avoids a full library reload
            // for what may be a single-file revert. ReloadFileAsync handles both modified files
            // (re-parses from disk) and formerly-added files deleted by the revert (removes
            // their models from the graph).
            bool hasMoChanges = false;
            foreach (var relativePath in revertedRelativePaths)
            {
                if (relativePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                {
                    hasMoChanges = true;
                    var fullPath = Path.Combine(Repository.VcsRootPath, relativePath);
                    await LibraryDataService.ReloadFileAsync(fullPath);
                }
                else if (Path.GetFileName(relativePath).Equals("package.order", StringComparison.OrdinalIgnoreCase))
                {
                    // package.order affects library structure — needs full pipeline but not a full
                    // library reload; flag so VcsFilesChanged is fired below.
                    hasMoChanges = true;
                }
            }

            // Collect affected model IDs from the now-updated graph for model viewer refresh.
            var affectedModelIds = new HashSet<string>();
            foreach (var relativePath in revertedRelativePaths)
            {
                if (!relativePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                    continue;
                var fullPath = Path.Combine(Repository.VcsRootPath, relativePath);
                var fileId = GraphBuilder.GenerateFileId(fullPath);
                foreach (var model in LibraryDataService.CombinedGraph.GetModelsInFile(fileId))
                    affectedModelIds.Add(model.Id);
            }

            // Check VCS status first so AnnotateVcsStatus in RefreshTreeItems uses fresh data.
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();

            // Invalidate render cache and refresh model viewer with fresh graph content.
            if (!string.IsNullOrEmpty(NavState.ModelID) && affectedModelIds.Contains(NavState.ModelID))
            {
                NavState.ModelContentChanged(affectedModelIds.ToList());
                NavState.ChangeModelID(NavState.ModelID);
            }

            Snackbar.Add("Files reverted successfully.", Severity.Success);
            StateHasChanged();

            if (hasMoChanges)
            {
                // Use VcsModelsChanged (analysis-only, no formatting) so the reverted files are
                // not immediately re-formatted back to a different state. The committed content
                // must be preserved exactly as-is.
                NavState.VcsModelsChanged(Repository.Id, affectedModelIds.ToList());
            }
            // else: only non-Modelica files were reverted — no analysis pipeline needed.
        }
    }

    private void ShowGitMenuDialog() {
        _showGitMenuDialog = !_showGitMenuDialog;
    }

    private async Task RebaseBranch()
    {
        _showGitMenuDialog = false;

        if (Repository == null)
            return;

        var parameters = new DialogParameters<GitRebaseDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialog = await DialogService.ShowAsync<GitRebaseDialog>("Rebase Branch", parameters, options);
        var result = await dialog.Result;

        if (result != null && !result.Canceled)
        {
            // RefreshRepositoryAsync reloads all libraries — clear stale expansion state.
            await RepositoryService.RefreshRepositoryAsync(Repository.Id);
            _expandedNodeIds.Clear();
            await CheckForUncommittedChangesAsync();
            await RefreshTreeItems();
            StateHasChanged();
        }
    }

    private async Task PushToRemote()
    {
        _showGitMenuDialog = false;

        if (Repository == null)
            return;

        _isPushing = true;
        StateHasChanged();

        try
        {
            var result = await RepositoryService.PushAsync(Repository.Id);

            if (result.Success)
            {
                Snackbar.Add("Push successful.", Severity.Success);
                await RepositoryService.RefreshRepositoryAsync(Repository.Id);
                StateHasChanged();
            }
            else
            {
                var message = result.ErrorMessage ?? "Push failed.";
                Snackbar.Add($"Push failed: {message}", Severity.Error);
            }
        }
        finally
        {
            _isPushing = false;
            StateHasChanged();
        }
    }

    private async Task CreatePullRequest()
    {
        _showGitMenuDialog = false;

        if (Repository == null)
            return;

        var parameters = new DialogParameters<CreatePullRequestDialog>
        {
            { x => x.RepositoryId, Repository.Id }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        await DialogService.ShowAsync<CreatePullRequestDialog>("Create Pull Request", parameters, options);
    }
}

using RevisionControl;
using System.Runtime.CompilerServices;

namespace MLQT.Shared.Dialogs;

public partial class VCSHistory
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter]
    public string? RepositoryID { get; set; } = null;

    private Repository? _repository;
    private List<VcsLogEntry> _logEntries = new();
    private VcsLogOptions _currentOptions = VcsLogOptions.DefaultPastWeek();
    private bool _isLoading;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private VcsLogEntry? _selectedEntry;
    private bool _showChangedFiles;
    private bool _isLoadingChangedFiles;
    private List<VcsChangedFile> _changedFiles = new();
    private string? _changedFilesRevision;
    private bool _confirmingCheckout;
    private bool _isCheckingOut;
    private string? _checkoutResultMessage;
    private bool _checkoutResultSuccess;

    // ── Commit graph ──────────────────────────────────────────────────────────

    private const int GraphLaneWidth = 14;
    private const int GraphRowHeight = 36;
    private const int GraphDotRadius = 4;

    private static readonly string[] GraphColors =
    [
        "var(--mud-palette-primary)",
        "#F44336", "#4CAF50", "#FF9800", "#9C27B0",
        "#00BCD4", "#795548", "#E91E63", "#607D8B", "#FFEB3B"
    ];

    private class CommitGraphRow
    {
        public string Revision { get; init; } = "";
        public string SvgPaths { get; init; } = "";
    }

    private List<CommitGraphRow> _graphRows = [];
    private Dictionary<string, CommitGraphRow> _graphRowByRevision = [];
    private int _graphWidth;

    private void BuildCommitGraph()
    {
        _graphRows.Clear();
        _graphRowByRevision.Clear();
        _graphWidth = 0;

        if (_repository?.VcsType != RepositoryVcsType.Git || _logEntries.Count == 0)
            return;

        var activeLanes = new List<string?>();
        int maxLanes = 0;

        var rowInfos = new List<(int commitLane, List<string?> lanesBefore, List<string?> lanesAfter)>();

        foreach (var entry in _logEntries)
        {
            var lanesBefore = activeLanes.ToList();

            // Find or assign a lane for this commit
            int commitLane = activeLanes.IndexOf(entry.Revision);
            if (commitLane < 0)
            {
                int empty = activeLanes.IndexOf(null);
                if (empty >= 0)
                {
                    commitLane = empty;
                    activeLanes[empty] = entry.Revision;
                }
                else
                {
                    commitLane = activeLanes.Count;
                    activeLanes.Add(entry.Revision);
                }
            }

            // Null out any duplicate slots pointing to this commit
            for (int i = 0; i < activeLanes.Count; i++)
                if (i != commitLane && activeLanes[i] == entry.Revision)
                    activeLanes[i] = null;

            // Advance activeLanes past this commit
            var parents = entry.ParentRevisions;
            if (parents.Count == 0)
            {
                activeLanes[commitLane] = null;
            }
            else
            {
                var fp = parents[0];
                int fpExisting = -1;
                for (int i = 0; i < activeLanes.Count; i++)
                    if (i != commitLane && activeLanes[i] == fp) { fpExisting = i; break; }

                activeLanes[commitLane] = fpExisting >= 0 ? null : fp;

                for (int p = 1; p < parents.Count; p++)
                {
                    var parent = parents[p];
                    if (activeLanes.Contains(parent)) continue;
                    int empty = activeLanes.IndexOf(null);
                    if (empty >= 0)
                        activeLanes[empty] = parent;
                    else
                        activeLanes.Add(parent);
                }
            }

            while (activeLanes.Count > 0 && activeLanes[^1] == null)
                activeLanes.RemoveAt(activeLanes.Count - 1);

            var lanesAfter = activeLanes.ToList();
            int rowMax = Math.Max(Math.Max(lanesBefore.Count, lanesAfter.Count), commitLane + 1);
            maxLanes = Math.Max(maxLanes, rowMax);

            rowInfos.Add((commitLane, lanesBefore, lanesAfter));
        }

        _graphWidth = Math.Max(1, maxLanes) * GraphLaneWidth;

        for (int i = 0; i < _logEntries.Count; i++)
        {
            var entry = _logEntries[i];
            var (commitLane, lanesBefore, lanesAfter) = rowInfos[i];
            var svgPaths = BuildRowSvgPaths(entry.Revision, commitLane, lanesBefore, lanesAfter,
                IsCurrentRevision(entry), entry.ParentRevisions);
            var row = new CommitGraphRow { Revision = entry.Revision, SvgPaths = svgPaths };
            _graphRows.Add(row);
            _graphRowByRevision[entry.Revision] = row;
        }
    }

    private static string BuildRowSvgPaths(string revision, int commitLane,
        List<string?> lanesBefore, List<string?> lanesAfter, bool isCurrent, List<string> parents)
    {
        const int H = GraphRowHeight;
        const int W = GraphLaneWidth;
        const int midY = H / 2;

        int cx(int lane) => lane * W + W / 2;
        string laneColor(int lane) => GraphColors[lane % GraphColors.Length];

        var sb = new System.Text.StringBuilder();

        // Pass-throughs and convergence lines
        for (int i = 0; i < lanesBefore.Count; i++)
        {
            if (lanesBefore[i] == null || i == commitLane) continue;

            if (lanesBefore[i] == revision)
            {
                // Extra lane converging to commitLane: top-half bezier
                var x1 = cx(i); var x2 = cx(commitLane);
                sb.Append($"<path d='M {x1} 0 Q {x1} {midY} {x2} {midY}' stroke='{laneColor(i)}' fill='none' stroke-width='1.5'/>");
            }
            else
            {
                var after = i < lanesAfter.Count ? lanesAfter[i] : null;
                if (after == lanesBefore[i])
                {
                    var x = cx(i);
                    sb.Append($"<path d='M {x} 0 L {x} {H}' stroke='{laneColor(i)}' fill='none' stroke-width='1.5'/>");
                }
            }
        }

        // Commit lane: incoming top-half line
        if (commitLane < lanesBefore.Count && lanesBefore[commitLane] == revision)
        {
            var x = cx(commitLane);
            sb.Append($"<path d='M {x} 0 L {x} {midY}' stroke='{laneColor(commitLane)}' fill='none' stroke-width='1.5'/>");
        }

        // Outgoing lines from dot to parents
        if (parents.Count > 0)
        {
            var fp = parents[0];
            var fpInAfter = commitLane < lanesAfter.Count ? lanesAfter[commitLane] : null;
            if (fpInAfter == fp)
            {
                var x = cx(commitLane);
                sb.Append($"<path d='M {x} {midY} L {x} {H}' stroke='{laneColor(commitLane)}' fill='none' stroke-width='1.5'/>");
            }
            else
            {
                int fpLane = lanesAfter.IndexOf(fp);
                if (fpLane >= 0)
                {
                    var x1 = cx(commitLane); var x2 = cx(fpLane);
                    sb.Append($"<path d='M {x1} {midY} Q {x2} {midY} {x2} {H}' stroke='{laneColor(fpLane)}' fill='none' stroke-width='1.5'/>");
                }
            }

            for (int p = 1; p < parents.Count; p++)
            {
                int pLane = lanesAfter.IndexOf(parents[p]);
                if (pLane < 0) continue;
                var x1 = cx(commitLane); var x2 = cx(pLane);
                sb.Append($"<path d='M {x1} {midY} Q {x2} {midY} {x2} {H}' stroke='{laneColor(pLane)}' fill='none' stroke-width='1.5'/>");
            }
        }

        // Commit dot
        var dotX = cx(commitLane);
        var dotR = isCurrent ? GraphDotRadius + 1 : GraphDotRadius;
        var strokeW = isCurrent ? 2 : 1;
        sb.Append($"<circle cx='{dotX}' cy='{midY}' r='{dotR}' fill='{laneColor(commitLane)}' stroke='white' stroke-width='{strokeW}'/>");

        return sb.ToString();
    }

    private async Task ShowFileDiffAsync(VcsChangedFile file)
    {
        if (_selectedEntry == null || _repository == null)
            return;

        // Hide the popover so it doesn't overlay the diff dialog
        _showChangedFiles = false;
        StateHasChanged();

        var options = new DialogOptions { FullWidth = true, MaxWidth = MaxWidth.Large };
        var parameters = new DialogParameters<RevisionDiffDialog>
        {
            { x => x.RepositoryId, _repository.Id },
            { x => x.FilePath, file.Path },
            { x => x.Revision, _selectedEntry.Revision },
            { x => x.ShortRevision, _selectedEntry.ShortRevision },
            { x => x.ChangeType, file.ChangeType }
        };

        var dialogRef = await DialogService.ShowAsync<RevisionDiffDialog>("Revision Diff", parameters, options);
        await dialogRef.Result;

        // Restore the popover after the diff dialog closes
        _showChangedFiles = true;
        StateHasChanged();
    }

    private void Close() => MudDialog.Close();

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (RepositoryID != null)
        {
            _repository = RepositoryService.GetRepository(RepositoryID);
        }

        if (_repository != null && _repository.VcsType != RepositoryVcsType.Local)
        {
            // Initialize date picker with default values
            _fromDate = _currentOptions.Since?.DateTime;
            _toDate = _currentOptions.Until?.DateTime;

            await LoadLogEntriesAsync();
        }
    }

    private async Task LoadLogEntriesAsync()
    {
        if (_repository == null || _isLoading)
            return;

        _isLoading = true;
        StateHasChanged();

        try
        {
            // Run log fetching and graph building on background thread to avoid blocking UI
            var repository = _repository;
            await Task.Run(() =>
            {
                _logEntries = RepositoryService.GetLogEntries(repository.Id, _currentOptions);
                _selectedEntry = _logEntries.FirstOrDefault(e => IsCurrentRevision(e));
                BuildCommitGraph();
            });
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private bool IsCurrentRevision(VcsLogEntry entry)
    {
        if (_repository?.CurrentRevision == null)
            return false;

        // For Git, compare full SHA or prefix
        // For SVN, compare revision numbers
        if (int.TryParse(entry.Revision, out int thisRevision)) {
            if (int.TryParse(_repository.CurrentRevision, out int currentRevision)) {
                //Both revision numbers are integers so this is SVN
                return thisRevision == currentRevision;
            }
        }
        //Must be Git as at least 1 revision isn't an integer
        return entry.Revision == _repository.CurrentRevision ||
                _repository.CurrentRevision.StartsWith(entry.Revision) ||
                entry.Revision.StartsWith(_repository.CurrentRevision);
    }

    private string GetRevisionTooltip(VcsLogEntry entry)
    {
        var tooltip = entry.Revision;
        if (IsCurrentRevision(entry))
        {
            tooltip += " (current)";
        }
        return tooltip;
    }

    private string GetRowClass(VcsLogEntry entry, int index)
    {
        var classes = new System.Text.StringBuilder();
        if (IsCurrentRevision(entry))
            classes.Append("current-revision-row ");
        if (_showChangedFiles && entry == _selectedEntry)
            classes.Append("popover-source-row ");
        return classes.ToString().TrimEnd();
    }

    private void OnSelectedEntryChanged(VcsLogEntry? entry)
    {
        _selectedEntry = entry;
        // Future: Enable/disable action buttons based on selection
    }

    private async Task OnRowClick(TableRowClickEventArgs<VcsLogEntry> args)
    {
        if (args.Item == null || _repository == null)
            return;

        _selectedEntry = args.Item;
        await LoadChangedFilesAsync(args.Item.Revision, args.Item.ShortRevision);
    }

    private async Task LoadChangedFilesAsync(string revision, string shortRevision)
    {
        if (_repository == null)
            return;

        _confirmingCheckout = false;
        _checkoutResultMessage = null;
        _isLoadingChangedFiles = true;
        _showChangedFiles = true;
        StateHasChanged();

        try
        {
            _changedFiles = await Task.Run(() =>
                RepositoryService.GetChangedFiles(_repository.Id, revision));
            _changedFilesRevision = shortRevision;
        }
        finally
        {
            _isLoadingChangedFiles = false;
            StateHasChanged();
        }
    }

    private void CloseChangedFilesPopover()
    {
        _showChangedFiles = false;
        _confirmingCheckout = false;
        _checkoutResultMessage = null;
    }

    private async Task ApplyDateFilter()
    {
        _currentOptions.Since = _fromDate.HasValue
            ? new DateTimeOffset(_fromDate.Value)
            : null;
        _currentOptions.Until = _toDate.HasValue
            ? new DateTimeOffset(_toDate.Value.AddDays(1).AddSeconds(-1)) // End of day
            : null;

        await LoadLogEntriesAsync();
    }

    private async Task ResetDateFilter()
    {
        _currentOptions = VcsLogOptions.DefaultPastWeek();
        _fromDate = _currentOptions.Since?.DateTime;
        _toDate = null;

        await LoadLogEntriesAsync();
    }

    private async Task LoadMore()
    {
        _currentOptions.MaxEntries += 50;
        _currentOptions.Since = null; // Remove date filter
        await LoadLogEntriesAsync();
    }

    private async Task LoadAll()
    {
        _currentOptions.MaxEntries = 500;
        _currentOptions.Since = null; // Remove date filter
        _fromDate = null;

        await LoadLogEntriesAsync();
    }

    private void CheckoutRevision()
    {
        _confirmingCheckout = true;
        _checkoutResultMessage = null;
    }

    private void CancelCheckout()
    {
        _confirmingCheckout = false;
    }

    private async Task ConfirmCheckoutAsync()
    {
        if (_selectedEntry == null || _repository == null)
            return;

        _confirmingCheckout = false;
        _isCheckingOut = true;
        StateHasChanged();

        try
        {
            // Pause file monitoring before checkout to prevent the large number of file-change
            // events from locking up the UI. The analysis handler will restart monitoring.
            FileMonitoringService.StopMonitoring(_repository.Id);

            var result = await RepositoryService.CheckoutRevisionAsync(_repository.Id, _selectedEntry.Revision);
            _checkoutResultSuccess = result.Success;
            _checkoutResultMessage = result.Success
                ? $"Checked out revision {_selectedEntry.ShortRevision}."
                : result.ErrorMessage ?? "Checkout failed.";

            if (result.Success)
            {
                // Reload _repository info and library data from the newly checked-out files
                _repository = RepositoryService.GetRepository(_repository.Id);
                await RepositoryService.RefreshRepositoryAsync(_repository!.Id);
                await LoadLogEntriesAsync();

                // Trigger background analysis (formatting + dependencies + style + resources).
                // Handler will restart monitoring once formatting is complete.
                NavState.VcsFilesChanged(_repository.Id);
            }
            else
            {
                // Checkout failed — restart monitor so future edits are still tracked
                if (!string.IsNullOrEmpty(_repository.VcsRootPath))
                    FileMonitoringService.StartMonitoring(_repository.Id, _repository.VcsRootPath);
            }
        }
        finally
        {
            _isCheckingOut = false;
            StateHasChanged();
        }
    }
}

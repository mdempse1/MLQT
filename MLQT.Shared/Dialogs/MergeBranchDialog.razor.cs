using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class MergeBranchDialog : IDisposable
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    /// <summary>
    /// What the merge did to the working copy. The browser reloads and analyses from this once the
    /// dialog closes, however it was closed (B296).
    /// </summary>
    [Parameter]
    public VcsDialogOutcome Outcome { get; set; } = new();

    // Held from the start of the merge until it is committed or abandoned. Ended by the dialog
    // closing too, which is the way out a cancel in the conflict phase used to miss.
    private MonitorPause? _pause;

    private enum MergePhase
    {
        CheckingState,
        DirtyWorkingCopy,
        ReadyToMerge,
        Merging,
        ConflictResolution
    }

    private BranchSelector? _branchSelector;
    private string? _selectedBranch;
    private string? _currentBranch;
    private MergePhase _phase = MergePhase.CheckingState;
    private List<VcsWorkingCopyFile> _dirtyFiles = [];
    private Dictionary<string, ConflictFileState> _conflictStates = [];
    private HashSet<string> _treeConflictPaths = [];
    private VcsMergeResult? _mergeResult;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isWorking = false;

    private bool AllResolved => VcsConflictRules.AllResolved(_conflictStates);

    private Repository? _repository;

    protected override async Task OnInitializedAsync()
    {
        _repository = RepositoryService.GetRepository(RepositoryId);
        _currentBranch = _repository?.CurrentBranch ?? "unknown";
        await CheckWorkingCopyState();
    }

    private async Task CheckWorkingCopyState()
    {
        _phase = MergePhase.CheckingState;
        _errorMessage = null;
        StateHasChanged();

        var changes = await Task.Run(() => RepositoryService.GetWorkingCopyChanges(RepositoryId));

        _dirtyFiles = VcsConflictRules.BlockingChanges(changes);

        _phase = _dirtyFiles.Count > 0 ? MergePhase.DirtyWorkingCopy : MergePhase.ReadyToMerge;
        StateHasChanged();
    }

    private async Task CommitFirst()
    {
        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.Outcome, Outcome }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialogRef = await DialogService.ShowAsync<CommitChangesDialog>("Commit Changes", parameters, options);
        await dialogRef.Result;

        // Re-check: user may have committed some or all changes
        await CheckWorkingCopyState();
    }

    private async Task RevertAll()
    {
        _isWorking = true;
        _errorMessage = null;
        StateHasChanged();

        VcsOperationResult result;
        try
        {
            result = await RepositoryService.CleanWorkspaceAsync(RepositoryId);
        }
        finally
        {
            _isWorking = false;
        }

        if (result.Success)
        {
            Outcome.WorkingCopyChanged = true;
            await StartMerge();
        }
        else
        {
            _errorMessage = result.ErrorMessage ?? "Revert failed.";
            StateHasChanged();
        }
    }

    private async Task StartMerge()
    {
        if (string.IsNullOrEmpty(_selectedBranch))
            return;

        _phase = MergePhase.Merging;
        _statusMessage = $"Merging from {_selectedBranch}...";
        _errorMessage = null;
        StateHasChanged();

        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository != null)
            _pause = MonitorPause.Begin(FileMonitoringService, RepositoryService.GetRepositoriesSharingWorkingCopy(RepositoryId));

        try
        {
            _mergeResult = await RepositoryService.MergeBranchAsync(RepositoryId, _selectedBranch);

            if (!_mergeResult.Success && !_mergeResult.HasConflicts)
            {
                _errorMessage = _mergeResult.ErrorMessage ?? "Merge failed.";
                _phase = MergePhase.ReadyToMerge;
                return;
            }

            if (_mergeResult.HasConflicts)
            {
                _treeConflictPaths = [.. _mergeResult.TreeConflictedFiles];
                _conflictStates = VcsConflictRules.CarryForward(
                    _mergeResult.ConflictedFiles.Concat(_mergeResult.TreeConflictedFiles).Distinct(),
                    _conflictStates);
                Outcome.WorkingCopyChanged = true;
                Outcome.LeftInProgress = true;
                _phase = MergePhase.ConflictResolution;
                return;
            }

            if (!_mergeResult.HasChanges)
            {
                Snackbar.Add("No changes to merge — already up to date.", Severity.Info);
                MudDialog?.Close(DialogResult.Ok(_mergeResult));
                return;
            }

            // Success, no conflicts — commit it, then close
            Outcome.WorkingCopyChanged = true;
            _pause?.Dispose();
            await CommitAndClose();
        }
        finally
        {
            // Conflicts keep the monitor off while the user resolves them; every other outcome is
            // finished with it.
            if (_phase != MergePhase.ConflictResolution)
                _pause?.Dispose();
            _statusMessage = null;
            StateHasChanged();
        }
    }

    private async Task ResolveFile(string filePath, ConflictResolutionChoice choice)
    {
        _isWorking = true;
        StateHasChanged();

        try
        {
            var result = await RepositoryService.ResolveConflictAsync(RepositoryId, filePath, choice);

            if (result.Success)
                _conflictStates[filePath] = ConflictFileState.Resolved;
            else
                _errorMessage = $"Could not resolve {Path.GetFileName(filePath)}: {result.ErrorMessage}";
        }
        finally
        {
            _isWorking = false;
            StateHasChanged();
        }
    }

    private void SetEditingExternally(string filePath)
    {
        _conflictStates[filePath] = ConflictFileState.EditingExternally;
        StateHasChanged();
    }

    private async Task CommitMerge()
    {
        _pause?.Dispose();
        await CommitAndClose();
    }

    /// <summary>
    /// Offers the commit, then closes. Closed afterwards rather than before: the browser reloads and
    /// starts the formatting pipeline when this dialog closes, and the formatter must not run before
    /// the commit or it puts its own changes into the merge commit. This used to close first and
    /// start the pipeline itself after the commit - while the browser was reloading every library
    /// over it.
    /// </summary>
    private async Task CommitAndClose()
    {
        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.InitialCommitMessage, BuildMergeCommitMessage() },
            { x => x.Outcome, Outcome }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialogRef = await DialogService.ShowAsync<CommitChangesDialog>("Commit Merged Changes", parameters, options);
        await dialogRef.Result;

        Outcome.LeftInProgress = false;
        MudDialog?.Close(DialogResult.Ok(_mergeResult));
    }

    private string BuildMergeCommitMessage()
    {
        // Whether to include a revision range is this dialog's question, not the message builder's:
        // it is the half that needs the repository.
        var isSvn = RepositoryService.GetRepository(RepositoryId)?.VcsType == RepositoryVcsType.SVN;

        return MergeCommitMessage.Build(
            _mergeResult?.SourceBranch,
            _selectedBranch,
            _currentBranch,
            isSvn ? _mergeResult?.StartRevision : null,
            isSvn ? _mergeResult?.EndRevision : null);
    }

    private string GetRelativePath(string filePath) =>
        VcsConflictRules.RelativeTo(RepositoryService.GetRepository(RepositoryId)?.LocalPath, filePath);


    private async Task ShowDiff(string filePath)
    {
        var parameters = new DialogParameters<ConflictDiffDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.FilePath, filePath }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true, MaxWidth = MaxWidth.Large };
        await DialogService.ShowAsync<ConflictDiffDialog>("Conflict Diff", parameters, options);
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }

    /// <summary>Starts the monitor again if the dialog closes with the merge unfinished.</summary>
    public void Dispose() => _pause?.Dispose();
}

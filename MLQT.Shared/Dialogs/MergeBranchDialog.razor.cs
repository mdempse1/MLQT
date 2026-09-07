using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class MergeBranchDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

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
            { x => x.RepositoryId, RepositoryId }
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

        var result = await RepositoryService.CleanWorkspaceAsync(RepositoryId);

        _isWorking = false;

        if (result.Success)
        {
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
            FileMonitoringService.StopMonitoring(RepositoryId);

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
                _phase = MergePhase.ConflictResolution;
                return;
            }

            if (!_mergeResult.HasChanges)
            {
                Snackbar.Add("No changes to merge — already up to date.", Severity.Info);
                MudDialog?.Close(DialogResult.Ok(_mergeResult));
                return;
            }

            // Success, no conflicts — open commit dialog
            MudDialog?.Close(DialogResult.Ok(_mergeResult));
            await ShowCommitDialog();
        }
        finally
        {
            if (_phase != MergePhase.ConflictResolution)
            {
                if (repository != null)
                    FileMonitoringService.StartMonitoring(RepositoryId, repository.VcsRootPath);
            }
            _statusMessage = null;
            StateHasChanged();
        }
    }

    private async Task ResolveFile(string filePath, ConflictResolutionChoice choice)
    {
        _isWorking = true;
        StateHasChanged();

        var result = await RepositoryService.ResolveConflictAsync(RepositoryId, filePath, choice);

        if (result.Success)
        {
            _conflictStates[filePath] = ConflictFileState.Resolved;
        }
        else
        {
            _errorMessage = $"Could not resolve {Path.GetFileName(filePath)}: {result.ErrorMessage}";
        }

        _isWorking = false;
        StateHasChanged();
    }

    private void SetEditingExternally(string filePath)
    {
        _conflictStates[filePath] = ConflictFileState.EditingExternally;
        StateHasChanged();
    }

    private async Task CommitMerge()
    {
        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository != null)
            FileMonitoringService.StartMonitoring(RepositoryId, repository.VcsRootPath);

        MudDialog?.Close(DialogResult.Ok(_mergeResult));
        await ShowCommitDialog();
    }

    private async Task ShowCommitDialog()
    {
        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.InitialCommitMessage, BuildMergeCommitMessage() }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialogRef = await DialogService.ShowAsync<CommitChangesDialog>("Commit Merged Changes", parameters, options);
        await dialogRef.Result;

        // Trigger the formatting + analysis pipeline only after the commit is complete.
        // Doing this before the commit would format uncommitted merge changes, which could
        // introduce non-merge changes into the merge commit.
        NavState.VcsFilesChanged(RepositoryId);
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
}

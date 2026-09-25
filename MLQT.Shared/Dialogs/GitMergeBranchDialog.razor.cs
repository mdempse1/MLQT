using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class GitMergeBranchDialog : IDisposable
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
        ConflictResolution,
        PushPrompt
    }

    private BranchSelector? _branchSelector;
    private string? _selectedBranch;
    private string? _currentBranch;
    private MergePhase _phase = MergePhase.CheckingState;
    private List<VcsWorkingCopyFile> _dirtyFiles = [];
    private Dictionary<string, ConflictFileState> _conflictStates = [];
    private VcsMergeResult? _mergeResult;
    private VcsOperationResult? _pushResult;
    private string? _errorMessage;
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

            if (!_mergeResult.HasChanges)
            {
                Snackbar.Add("No changes to merge — already up to date.", Severity.Info);
                MudDialog?.Close(DialogResult.Ok(_mergeResult));
                return;
            }

            Outcome.WorkingCopyChanged = true;

            if (_mergeResult.HasConflicts)
            {
                _conflictStates = VcsConflictRules.CarryForward(_mergeResult.ConflictedFiles, _conflictStates);
                Outcome.LeftInProgress = true;
                _phase = MergePhase.ConflictResolution;
                return;
            }

            // Clean merge — LibGit2Sharp auto-created the merge commit. The browser reloads and
            // analyses once the dialog closes.
            _phase = MergePhase.PushPrompt;
        }
        finally
        {
            // Conflicts keep the monitor off while the user resolves them; every other outcome is
            // finished with it.
            if (_phase != MergePhase.ConflictResolution)
                _pause?.Dispose();
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

        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.InitialCommitMessage, BuildMergeCommitMessage() },
            { x => x.Outcome, Outcome }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialogRef = await DialogService.ShowAsync<CommitChangesDialog>("Commit Merged Changes", parameters, options);
        await dialogRef.Result;

        // Every conflict is resolved, committed or not, so the formatter may run once the dialog
        // closes and the browser has reloaded.
        Outcome.LeftInProgress = false;
        _phase = MergePhase.PushPrompt;
        StateHasChanged();
    }

    private async Task PushToRemote()
    {
        _isWorking = true;
        StateHasChanged();

        try
        {
            _pushResult = await RepositoryService.PushAsync(RepositoryId);
        }
        finally
        {
            _isWorking = false;
        }

        if (_pushResult.Success)
            MudDialog?.Close(DialogResult.Ok(_mergeResult));
        else
            StateHasChanged(); // show error — user can retry or skip
    }

    private void SkipPush()
    {
        MudDialog?.Close(DialogResult.Ok(_mergeResult));
    }

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

    private string BuildMergeCommitMessage() =>
        MergeCommitMessage.Build(_mergeResult?.SourceBranch, _selectedBranch, _currentBranch);

    private string GetRelativePath(string filePath) =>
        VcsConflictRules.RelativeTo(RepositoryService.GetRepository(RepositoryId)?.LocalPath, filePath);

}

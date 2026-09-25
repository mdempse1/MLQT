using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class GitRebaseDialog : IDisposable
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
    /// What the rebase did to the working copy. The browser reloads and analyses from this once the
    /// dialog closes, however it was closed (B296).
    /// </summary>
    [Parameter]
    public VcsDialogOutcome Outcome { get; set; } = new();

    // Held from the start of the rebase until it is finished or abandoned. Ended by the dialog
    // closing too, which is the way out a cancel in the conflict phase used to miss.
    private MonitorPause? _pause;

    // Revert all discarded the user's changes before the rebase, which an abort does not bring back.
    private bool _discardedChanges;

    private enum RebasePhase
    {
        CheckingState,
        DirtyWorkingCopy,
        ReadyToRebase,
        Rebasing,
        ConflictResolution,
        PushPrompt
    }

    // Alias so the markup @if (_phase != MudDialogPhase.PushPrompt) compiles cleanly
    private static class MudDialogPhase
    {
        public const RebasePhase PushPrompt = RebasePhase.PushPrompt;
    }

    private BranchSelector? _branchSelector;
    private string? _selectedBranch;
    private string? _currentBranch;
    private RebasePhase _phase = RebasePhase.CheckingState;
    private List<VcsWorkingCopyFile> _dirtyFiles = [];
    private Dictionary<string, ConflictFileState> _conflictStates = [];
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
        _phase = RebasePhase.CheckingState;
        _errorMessage = null;
        StateHasChanged();

        var changes = await Task.Run(() => RepositoryService.GetWorkingCopyChanges(RepositoryId));

        _dirtyFiles = VcsConflictRules.BlockingChanges(changes);

        _phase = _dirtyFiles.Count > 0 ? RebasePhase.DirtyWorkingCopy : RebasePhase.ReadyToRebase;
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
            _discardedChanges = true;
            Outcome.WorkingCopyChanged = true;
            await CheckWorkingCopyState();
            if (_phase == RebasePhase.ReadyToRebase)
                await StartRebase();
        }
        else
        {
            _errorMessage = result.ErrorMessage ?? "Revert failed.";
            StateHasChanged();
        }
    }

    private async Task StartRebase()
    {
        if (string.IsNullOrEmpty(_selectedBranch))
            return;

        _phase = RebasePhase.Rebasing;
        _errorMessage = null;
        StateHasChanged();

        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository != null)
            _pause = MonitorPause.Begin(FileMonitoringService, repository);

        try
        {
            var rebaseResult = await RepositoryService.RebaseAsync(RepositoryId, _selectedBranch);

            if (!string.IsNullOrEmpty(rebaseResult.ErrorMessage))
            {
                _errorMessage = rebaseResult.ErrorMessage;
                _phase = RebasePhase.ReadyToRebase;
                return;
            }

            Outcome.WorkingCopyChanged = true;

            if (rebaseResult.HasConflicts)
            {
                ApplyConflicts(rebaseResult.ConflictedFiles);
                Outcome.LeftInProgress = true;
                _phase = RebasePhase.ConflictResolution;
                return;
            }

            // Clean rebase complete. The browser reloads and analyses once the dialog closes.
            _phase = RebasePhase.PushPrompt;
        }
        finally
        {
            // Conflicts keep the monitor off while the user resolves them - an edit to a conflicted
            // file is not a change to format. Every other outcome is finished with it.
            if (_phase != RebasePhase.ConflictResolution)
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

    private async Task ContinueRebase()
    {
        _isWorking = true;
        _errorMessage = null;
        StateHasChanged();

        VcsMergeResult result;
        try
        {
            result = await RepositoryService.ContinueRebaseAsync(RepositoryId);
        }
        finally
        {
            _isWorking = false;
        }

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            _errorMessage = result.ErrorMessage;
            StateHasChanged();
            return;
        }

        if (result.HasConflicts)
        {
            // Another commit in the rebase sequence has conflicts — update the list
            ApplyConflicts(result.ConflictedFiles);
            StateHasChanged();
            return;
        }

        // All commits replayed successfully. The browser reloads and analyses once the dialog closes.
        Outcome.LeftInProgress = false;
        _pause?.Dispose();
        _phase = RebasePhase.PushPrompt;
        StateHasChanged();
    }

    private async Task AbortRebase()
    {
        _isWorking = true;
        StateHasChanged();

        VcsOperationResult result;
        try
        {
            result = await RepositoryService.AbortRebaseAsync(RepositoryId);
        }
        finally
        {
            _isWorking = false;
            _pause?.Dispose();
        }

        if (result.Success)
        {
            // Back where it started. The libraries were never reloaded from the half-rebased working
            // copy, so there is nothing to reload now - unless Revert all discarded changes first.
            Outcome.LeftInProgress = false;
            Outcome.WorkingCopyChanged = _discardedChanges;
            MudDialog?.Cancel();
        }
        else
        {
            _errorMessage = result.ErrorMessage ?? "Abort failed.";
            StateHasChanged();
        }
    }

    private async Task ForcePushToRemote()
    {
        _isWorking = true;
        StateHasChanged();

        try
        {
            _pushResult = await RepositoryService.ForcePushAsync(RepositoryId);
        }
        finally
        {
            _isWorking = false;
        }

        if (_pushResult.Success)
            MudDialog?.Close(DialogResult.Ok(true));
        else
            StateHasChanged(); // show error — user can retry or skip
    }

    private void SkipPush()
    {
        MudDialog?.Close(DialogResult.Ok(true));
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }

    /// <summary>Starts the monitor again if the dialog closes with the rebase unfinished.</summary>
    public void Dispose() => _pause?.Dispose();

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

    private void ApplyConflicts(List<string> conflictedFiles) =>
        _conflictStates = VcsConflictRules.CarryForward(conflictedFiles, _conflictStates);

    private string GetRelativePath(string filePath) =>
        VcsConflictRules.RelativeTo(RepositoryService.GetRepository(RepositoryId)?.LocalPath, filePath);

}

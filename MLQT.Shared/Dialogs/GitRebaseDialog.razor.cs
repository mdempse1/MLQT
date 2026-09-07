using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class GitRebaseDialog
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

    private enum ConflictFileState { Unresolved, EditingExternally, Resolved }

    private BranchSelector? _branchSelector;
    private string? _selectedBranch;
    private string? _currentBranch;
    private RebasePhase _phase = RebasePhase.CheckingState;
    private List<VcsWorkingCopyFile> _dirtyFiles = [];
    private Dictionary<string, ConflictFileState> _conflictStates = [];
    private VcsOperationResult? _pushResult;
    private string? _errorMessage;
    private bool _isWorking = false;

    private bool AllResolved => _conflictStates.Count > 0 && _conflictStates.Values.All(s => s == ConflictFileState.Resolved);

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

        _dirtyFiles = changes.Where(f => f.Status is
            VcsFileStatus.Modified or VcsFileStatus.Added or
            VcsFileStatus.Deleted or VcsFileStatus.Untracked or
            VcsFileStatus.Conflicted).ToList();

        _phase = _dirtyFiles.Count > 0 ? RebasePhase.DirtyWorkingCopy : RebasePhase.ReadyToRebase;
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
            FileMonitoringService.StopMonitoring(RepositoryId);

        try
        {
            var rebaseResult = await RepositoryService.RebaseAsync(RepositoryId, _selectedBranch);

            if (!string.IsNullOrEmpty(rebaseResult.ErrorMessage))
            {
                _errorMessage = rebaseResult.ErrorMessage;
                _phase = RebasePhase.ReadyToRebase;
                return;
            }

            if (rebaseResult.HasConflicts)
            {
                ApplyConflicts(rebaseResult.ConflictedFiles);
                _phase = RebasePhase.ConflictResolution;
                return;
            }

            // Clean rebase complete
            NavState.VcsFilesChanged(RepositoryId);
            _phase = RebasePhase.PushPrompt;
        }
        finally
        {
            if (_phase != RebasePhase.ConflictResolution)
            {
                if (repository != null)
                    FileMonitoringService.StartMonitoring(RepositoryId, repository.VcsRootPath);
            }
            StateHasChanged();
        }
    }

    private async Task ResolveFile(string filePath, ConflictResolutionChoice choice)
    {
        _isWorking = true;
        StateHasChanged();

        var result = await RepositoryService.ResolveConflictAsync(RepositoryId, filePath, choice);

        if (result.Success)
            _conflictStates[filePath] = ConflictFileState.Resolved;
        else
            _errorMessage = $"Could not resolve {Path.GetFileName(filePath)}: {result.ErrorMessage}";

        _isWorking = false;
        StateHasChanged();
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

        var result = await RepositoryService.ContinueRebaseAsync(RepositoryId);
        _isWorking = false;

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

        // All commits replayed successfully
        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository != null)
            FileMonitoringService.StartMonitoring(RepositoryId, repository.VcsRootPath);

        NavState.VcsFilesChanged(RepositoryId);
        _phase = RebasePhase.PushPrompt;
        StateHasChanged();
    }

    private async Task AbortRebase()
    {
        _isWorking = true;
        StateHasChanged();

        var result = await RepositoryService.AbortRebaseAsync(RepositoryId);
        _isWorking = false;

        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository != null)
            FileMonitoringService.StartMonitoring(RepositoryId, repository.VcsRootPath);

        if (result.Success)
        {
            NavState.VcsFilesChanged(RepositoryId);
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

        _pushResult = await RepositoryService.ForcePushAsync(RepositoryId);
        _isWorking = false;

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

    /// <summary>
    /// Replaces the conflict state dictionary with the new set of conflicted files,
    /// preserving Resolved state for any files that were already resolved.
    /// </summary>
    private void ApplyConflicts(List<string> conflictedFiles)
    {
        var updated = new Dictionary<string, ConflictFileState>();
        foreach (var fp in conflictedFiles)
        {
            updated[fp] = _conflictStates.TryGetValue(fp, out var existing)
                ? existing
                : ConflictFileState.Unresolved;
        }
        _conflictStates = updated;
    }

    private string GetRelativePath(string filePath)
    {
        var repository = RepositoryService.GetRepository(RepositoryId);
        if (repository?.LocalPath != null)
        {
            try { return Path.GetRelativePath(repository.LocalPath, filePath); }
            catch { }
        }
        return filePath;
    }

    private static string StatusIcon(VcsFileStatus status) => status switch
    {
        VcsFileStatus.Modified   => Icons.Material.Filled.Edit,
        VcsFileStatus.Added      => Icons.Material.Filled.Add,
        VcsFileStatus.Deleted    => Icons.Material.Filled.Delete,
        VcsFileStatus.Untracked  => Icons.Material.Filled.HelpOutline,
        VcsFileStatus.Conflicted => Icons.Material.Filled.Warning,
        _                        => Icons.Material.Filled.Circle
    };

    private static Color StatusColor(VcsFileStatus status) => status switch
    {
        VcsFileStatus.Modified   => Color.Warning,
        VcsFileStatus.Added      => Color.Success,
        VcsFileStatus.Deleted    => Color.Error,
        VcsFileStatus.Untracked  => Color.Default,
        VcsFileStatus.Conflicted => Color.Error,
        _                        => Color.Default
    };
}

using RevisionControl;
using RevisionControl.Interfaces;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class GitMergeBranchDialog
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
        ConflictResolution,
        PushPrompt
    }

    private enum ConflictFileState { Unresolved, EditingExternally, Resolved }

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
        _phase = MergePhase.CheckingState;
        _errorMessage = null;
        StateHasChanged();

        var changes = await Task.Run(() => RepositoryService.GetWorkingCopyChanges(RepositoryId));

        _dirtyFiles = changes.Where(f => f.Status is
            VcsFileStatus.Modified or VcsFileStatus.Added or
            VcsFileStatus.Deleted or VcsFileStatus.Untracked or
            VcsFileStatus.Conflicted).ToList();

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

            if (!_mergeResult.HasChanges)
            {
                Snackbar.Add("No changes to merge — already up to date.", Severity.Info);
                MudDialog?.Close(DialogResult.Ok(_mergeResult));
                return;
            }

            if (_mergeResult.HasConflicts)
            {
                _conflictStates = _mergeResult.ConflictedFiles
                    .ToDictionary(f => f, _ => ConflictFileState.Unresolved);
                _phase = MergePhase.ConflictResolution;
                return;
            }

            // Clean merge — LibGit2Sharp auto-created the merge commit
            NavState.VcsFilesChanged(RepositoryId);
            _phase = MergePhase.PushPrompt;
        }
        finally
        {
            if (_phase != MergePhase.ConflictResolution)
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

        var parameters = new DialogParameters<CommitChangesDialog>
        {
            { x => x.RepositoryId, RepositoryId },
            { x => x.InitialCommitMessage, BuildMergeCommitMessage() }
        };
        var options = new DialogOptions { CloseOnEscapeKey = true, FullWidth = true };
        var dialogRef = await DialogService.ShowAsync<CommitChangesDialog>("Commit Merged Changes", parameters, options);
        await dialogRef.Result;

        // Trigger formatting + analysis pipeline after commit
        NavState.VcsFilesChanged(RepositoryId);
        _phase = MergePhase.PushPrompt;
        StateHasChanged();
    }

    private async Task PushToRemote()
    {
        _isWorking = true;
        StateHasChanged();

        _pushResult = await RepositoryService.PushAsync(RepositoryId);

        _isWorking = false;

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

    private string BuildMergeCommitMessage()
    {
        var branch = _mergeResult?.SourceBranch ?? _selectedBranch ?? "unknown";
        var target = _currentBranch ?? "working copy";
        return $"Merge '{branch}' into {target}";
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

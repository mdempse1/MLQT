using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class CommitChangesDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    [Parameter]
    public string InitialCommitMessage { get; set; } = "";

    private Repository? _repository;
    private bool _isCommitting = false;
    private bool _commitSucceeded = false;
    private List<string> _skippedFiles = new();
    private string? _statusMessage;
    private string _commitMessage = "";
    private string _commitIssueId = "";
    private bool CanCommit => _selectedFilesCount > 0 &&
                              !string.IsNullOrWhiteSpace(_commitMessage) &&
                              (_repository?.StyleSettings?.CommitRequiresIssueNumber != true || !string.IsNullOrEmpty(_commitIssueId));
                              
    private int _selectedFilesCount = 0;
    public List<VcsWorkingCopyFile> _changedFiles = new ();
    private string? _errorMessage;
    public bool _isLoading = true;
    private ChangeReview? _changeReviewComponent;

    protected override async Task OnInitializedAsync()
    {
        _repository = RepositoryService.GetRepository(RepositoryId) ?? new Repository { Name = "Unknown", LocalPath = "" };
        _commitMessage = InitialCommitMessage;
    }

    private async Task CommitChanges()
    {
        if (!CanCommit)
            return;

        _isCommitting = true;
        _errorMessage = null;
        _statusMessage = "Committing...";
        StateHasChanged();

        var progress = new Progress<string>(msg =>
        {
            InvokeAsync(() =>
            {
                _statusMessage = msg;
                StateHasChanged();
            });
        });

        try
        {
            var selectedFiles = _changeReviewComponent!.GetSelectedFiles();
            var message = BuildCommitMessage();

            var result = await RepositoryService.CommitAsync(RepositoryId, message, selectedFiles, progress);

            if (result.Success)
            {
                if (result.SkippedFiles.Count > 0)
                {
                    _commitSucceeded = true;
                    _skippedFiles = result.SkippedFiles;
                    return;
                }
                MudDialog?.Close(DialogResult.Ok(result.NewRevision));
                return;
            }

            if (result.IsOutOfDate)
            {
                _statusMessage = "Working copy is out of date — updating...";
                StateHasChanged();

                var updateResult = await RepositoryService.UpdateRepositoryAsync(RepositoryId);
                if (!updateResult.Success)
                {
                    _errorMessage = $"Update failed: {updateResult.ErrorMessage}";
                    return;
                }

                _statusMessage = "Retrying commit...";
                StateHasChanged();

                if (_changeReviewComponent != null)
                    await _changeReviewComponent.LoadChanges();

                result = await RepositoryService.CommitAsync(RepositoryId, message, _changeReviewComponent?.GetSelectedFiles(), progress);

                if (result.Success)
                {
                    if (result.SkippedFiles.Count > 0)
                    {
                        _commitSucceeded = true;
                        _skippedFiles = result.SkippedFiles;
                        return;
                    }
                    MudDialog?.Close(DialogResult.Ok(result.NewRevision));
                    return;
                }
            }

            _errorMessage = result.ErrorMessage ?? "Commit failed.";
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isCommitting = false;
            _statusMessage = null;
        }
    }

    private string BuildCommitMessage()
    {
        if (_repository!.StyleSettings?.CommitRequiresIssueNumber == true)
        {
            return _repository.StyleSettings!.IssueNumberAtEnd
                ? _commitMessage + "\n" + _commitIssueId
                : _commitIssueId + "\n" + _commitMessage;
        }
        return _commitMessage;
    }

    private async Task CommitSkippedFiles()
    {
        _commitSucceeded = false;
        _skippedFiles = new();
        _errorMessage = null;
        if (_changeReviewComponent != null)
            await _changeReviewComponent.LoadChanges();
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }
}

using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class CommitChangesDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    [Parameter]
    public string InitialCommitMessage { get; set; } = "";

    /// <summary>
    /// Told when committing had to update the working copy first, which rewrites files the libraries
    /// were loaded from. The caller reloads from it once the dialog closes (B296).
    /// </summary>
    [Parameter]
    public VcsDialogOutcome Outcome { get; set; } = new();

    private Repository? _repository;
    private bool _isCommitting = false;
    private bool _commitSucceeded = false;
    private List<string> _skippedFiles = new();
    private string? _statusMessage;
    private string _commitMessage = "";
    private string _commitIssueId = "";
    private bool CanCommit =>
        CanCommitWith(_selectedFilesCount, _commitMessage, _commitIssueId, _repository?.StyleSettings);

    /// <summary>
    /// Whether a commit is allowed: something selected, a message, and an issue number when the
    /// repository requires one.
    /// </summary>
    /// <remarks>
    /// This and <see cref="ComposeCommitMessage"/> are the whole of the commit-message policy, and
    /// this dialog is the only place that implements it. Pure and separate from the dialog's state so
    /// both halves of the policy can be held to a test — the rule is a repository setting a user
    /// turned on expecting it to be enforced, and nothing else in the solution would notice if it
    /// stopped being.
    /// </remarks>
    internal static bool CanCommitWith(
        int selectedFiles, string? message, string? issueId, StyleCheckingSettings? settings) =>
        selectedFiles > 0 &&
        !string.IsNullOrWhiteSpace(message) &&
        (settings?.CommitRequiresIssueNumber != true || !string.IsNullOrEmpty(issueId));
                              
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

                // An update like any other: the monitor held off while it writes, and the caller
                // told, so the libraries are reloaded from what it wrote. It used to do neither, and
                // the graph went on describing the files from before the update.
                VcsUpdateResult updateResult;
                var repository = RepositoryService.GetRepository(RepositoryId);
                using (repository is null ? null : MonitorPause.Begin(FileMonitoringService, repository))
                {
                    updateResult = await RepositoryService.UpdateRepositoryAsync(RepositoryId);
                }

                if (updateResult.HasChanges)
                    Outcome.WorkingCopyChanged = true;

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

    private string BuildCommitMessage() =>
        ComposeCommitMessage(_commitMessage, _commitIssueId, _repository?.StyleSettings);

    /// <summary>
    /// The message that is actually committed: the issue number goes on its own line, before the
    /// message or after it as <see cref="StyleCheckingSettings.IssueNumberAtEnd"/> says, and is left
    /// out entirely when the repository does not require one.
    /// </summary>
    internal static string ComposeCommitMessage(
        string message, string issueId, StyleCheckingSettings? settings)
    {
        if (settings?.CommitRequiresIssueNumber != true)
            return message;

        return settings.IssueNumberAtEnd
            ? message + "\n" + issueId
            : issueId + "\n" + message;
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

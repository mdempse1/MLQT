using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class RevertFilesDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    /// <summary>
    /// Told which files a revert was asked to change, before it runs: a revert that fails part way
    /// has still changed some of them, and the caller reloads them however the dialog closes (B296).
    /// </summary>
    [Parameter]
    public VcsDialogOutcome Outcome { get; set; } = new();

    private Repository? _repository;
    private int _selectedFilesCount = 0;
    public List<VcsWorkingCopyFile> _changedFiles = new ();
    private string? _errorMessage;
    public bool _isLoading = true;
    private ChangeReview? _changeReviewComponent;
    private bool CanRevert => _selectedFilesCount > 0;
    private bool _isReverting = false;

    protected override async Task OnInitializedAsync()
    {
        _repository = RepositoryService.GetRepository(RepositoryId) ?? new Repository { Name = "Unknown", LocalPath = "" };        
    }

    private async Task RevertFiles()
    {
        if (!CanRevert)
            return;

        _isReverting = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            var selectedFiles = _changeReviewComponent!.GetSelectedFiles();
            Outcome.FilesTouched.AddRange(selectedFiles);

            // With the monitor held off for the working copy, as every VCS operation that writes
            // is. Watched, each reverted file was reported as the user's change - 17,000 of them for
            // a large revert - and each batch dropped the cached working-copy status, though the
            // library browser reloads exactly the reverted files itself once the dialog closes.
            VcsOperationResult result;
            using (MonitorPause.Begin(FileMonitoringService, RepositoryService.GetRepositoriesSharingWorkingCopy(RepositoryId)))
            {
                result = await RepositoryService.RevertFilesAsync(RepositoryId, selectedFiles);
            }

            if (result.Success)
            {
                MudDialog?.Close(DialogResult.Ok(selectedFiles));
            }
            else
            {
                _errorMessage = result.ErrorMessage ?? "Revert failed.";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isReverting = false;
        }
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }
}

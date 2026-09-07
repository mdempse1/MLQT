using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class RevertFilesDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

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
            var result = await RepositoryService.RevertFilesAsync(RepositoryId, selectedFiles);

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

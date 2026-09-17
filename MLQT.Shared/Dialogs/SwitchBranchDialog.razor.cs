namespace MLQT.Shared.Dialogs;

public partial class SwitchBranchDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    private string? _selectedBranch;
    private bool _isSwitching = false;
    private bool _hasChanges = false;
    private string? _errorMessage;
    private string? _statusMessage;

    protected override async Task OnInitializedAsync()
    {
        await CheckForChanges();
    }

    private async Task CheckForChanges()
    {
        try
        {
            await Task.Run(() =>
            {
                var changes = RepositoryService.GetWorkingCopyChanges(RepositoryId);
                _hasChanges = changes.Count > 0;
            });
        }
        catch
        {
            // Ignore errors checking for changes
        }
    }

    private async Task SwitchBranch()
    {
        if (string.IsNullOrEmpty(_selectedBranch))
            return;

        _isSwitching = true;
        _errorMessage = null;
        _statusMessage = "Switching branch...";
        StateHasChanged();

        try
        {
            var result = await RepositoryService.SwitchBranchAsync(RepositoryId, _selectedBranch);

            if (!result.Success)
            {
                _errorMessage = result.ErrorMessage ?? "Failed to switch branch.";
                return;
            }

            // Branch switched — close. LibraryBrowser fires VcsFilesChanged which runs
            // the formatting + analysis pipeline in MainLayout.
            MudDialog?.Close(DialogResult.Ok(_selectedBranch));
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isSwitching = false;
            _statusMessage = null;
        }
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }
}

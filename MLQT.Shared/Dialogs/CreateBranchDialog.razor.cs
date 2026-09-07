using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class CreateBranchDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    private string _branchName = "";
    private bool _switchToBranch = true;
    private bool _isLoading = true;
    private bool _isCreating = false;
    private bool _branchExists = false;
    private string? _currentBranch;
    private List<VcsBranchInfo> _existingBranches = new();
    private string? _errorMessage;

    private bool CanCreate => !string.IsNullOrWhiteSpace(_branchName) && !_branchExists && IsValidBranchName(_branchName);

    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentBranch();
    }

    private async Task LoadCurrentBranch()
    {
        _isLoading = true;
        StateHasChanged();

        try
        {
            await Task.Run(() =>
            {
                var repository = RepositoryService.GetRepository(RepositoryId);
                _currentBranch = repository?.CurrentBranch;
                _existingBranches = RepositoryService.GetBranches(RepositoryId, includeRemote: true);
            });
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void OnBranchNameChanged(string newName)
    {
        _branchName = newName;
        _branchExists = _existingBranches.Any(b =>
            b.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) ||
            b.Name.Equals($"origin/{newName}", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsValidBranchName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Basic branch name validation
        // Can't start with -, can't contain .., can't end with .lock, etc.
        if (name.StartsWith("-") || name.StartsWith("."))
            return false;

        if (name.Contains("..") || name.Contains("~") || name.Contains("^") ||
            name.Contains(":") || name.Contains("?") || name.Contains("*") ||
            name.Contains("[") || name.Contains("\\") || name.Contains(" "))
            return false;

        if (name.EndsWith(".lock") || name.EndsWith("/") || name.EndsWith("."))
            return false;

        return true;
    }

    private async Task CreateBranch()
    {
        if (!CanCreate)
            return;

        _isCreating = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            var result = await RepositoryService.CreateBranchAsync(RepositoryId, _branchName, _switchToBranch);

            if (result.Success)
            {
                MudDialog?.Close(DialogResult.Ok(_branchName));
            }
            else
            {
                _errorMessage = result.ErrorMessage ?? "Failed to create branch.";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isCreating = false;
        }
    }

    private void Cancel()
    {
        MudDialog?.Cancel();
    }
}

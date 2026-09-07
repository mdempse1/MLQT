namespace MLQT.Shared.Dialogs;

public partial class CreatePullRequestDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    private enum PrPhase { Checking, NeedsPush, Ready }

    private PrPhase _phase = PrPhase.Checking;
    private string? _currentBranch;
    private string? _selectedBaseBranch;
    private bool _isWorking;
    private string? _statusMessage;
    private string? _errorMessage;

    protected override async Task OnInitializedAsync()
    {
        var repository = RepositoryService.GetRepository(RepositoryId);
        _currentBranch = repository?.CurrentBranch ?? "unknown";

        var isPushed = await RepositoryService.IsBranchPushedAsync(RepositoryId);
        _phase = isPushed ? PrPhase.Ready : PrPhase.NeedsPush;
        StateHasChanged();
    }

    private async Task OpenPullRequest()
    {
        _errorMessage = null;
        _isWorking = true;
        StateHasChanged();

        try
        {
            // Push the branch if it hasn't been pushed yet
            if (_phase == PrPhase.NeedsPush)
            {
                _statusMessage = "Pushing branch to remote...";
                StateHasChanged();

                var pushResult = await RepositoryService.PushAsync(RepositoryId);
                if (!pushResult.Success)
                {
                    _errorMessage = pushResult.ErrorMessage ?? "Push failed.";
                    return;
                }
            }

            // Get the pull request URL for the selected base branch
            _statusMessage = "Opening pull request...";
            StateHasChanged();

            var url = await RepositoryService.GetPullRequestUrlAsync(RepositoryId, _selectedBaseBranch);
            if (string.IsNullOrEmpty(url))
            {
                _errorMessage = "Could not determine the pull request URL for this repository. " +
                                "Please visit your repository's website to create the pull request manually.";
                return;
            }

            // Open in the default browser
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

            Snackbar.Add("Pull request page opened in browser.", Severity.Success);
            MudDialog?.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            _errorMessage = ex.Message;
        }
        finally
        {
            _isWorking = false;
            _statusMessage = null;
            StateHasChanged();
        }
    }

    private void Cancel() => MudDialog?.Cancel();
}

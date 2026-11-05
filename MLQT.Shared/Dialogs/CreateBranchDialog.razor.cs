using RevisionControl;

namespace MLQT.Shared.Dialogs;

public partial class CreateBranchDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFileMonitoringService FileMonitoringService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    /// <summary>
    /// Told when switching to the new branch rewrote files, which it does when the working copy was
    /// behind HEAD (an SVN branch is copied from HEAD). The caller reloads from it once the dialog
    /// closes, however it closes (B296).
    /// </summary>
    [Parameter]
    public VcsDialogOutcome Outcome { get; set; } = new();

    private string _branchName = "";
    private bool _switchToBranch = true;
    private bool _isLoading = true;
    private bool _isCreating = false;
    private bool _branchExists = false;
    private string? _currentBranch;
    private List<VcsBranchInfo> _existingBranches = new();
    private string? _errorMessage;
    private bool _isSvn;
    private IReadOnlyList<string>? _svnBranchDirectories;

    /// <summary>
    /// What the name field says about the name it wants.
    /// </summary>
    /// <remarks>
    /// An SVN branch is a folder, and where it goes is not obvious: a bare name goes in the
    /// repository's first branch directory, and a name with a folder of its own is used as typed
    /// when that folder exists (<c>SvnRevisionControlSystem.BranchPathFor</c>). Saying so before
    /// the user types is what stops <c>branches/x</c> being a surprise.
    /// </remarks>
    internal static string HelperTextFor(bool isSvn, IReadOnlyList<string>? svnBranchDirectories)
    {
        if (!isSvn)
            return "Enter a name for the new branch";

        var container = svnBranchDirectories is { Count: > 1 } ? svnBranchDirectories[1].Trim('/') : "branches";
        return $"A name such as my-feature is created in ^/{container}/. " +
               "A name that starts with a folder of the repository, such as branches/my-feature, is created there.";
    }

    /// <summary>What the switch check box adds, for the version control system in use.</summary>
    internal static string? SwitchNoteFor(bool isSvn) =>
        isSvn ? "Uncommitted changes in the working copy are carried over to the new branch, not committed." : null;

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
                _isSvn = repository?.VcsType == RepositoryVcsType.SVN;
                _svnBranchDirectories = repository?.StyleSettings?.SvnBranchDirectories;
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

    /// <summary>
    /// Whether a name is one git will accept as a branch.
    /// </summary>
    /// <remarks>
    /// A subset of git's check-ref-format rules, applied here so the dialog can say no while the
    /// user is still typing. Rejecting a valid name is the more visible failure; accepting an
    /// invalid one means git refuses it later with a message about ref formats that says nothing
    /// about which character was the problem.
    /// </remarks>
    internal static bool IsValidBranchName(string name)
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
            // The switch, when there is one, runs with the monitor held off for the whole working
            // copy, as every other switch does. Watched, the files svn touched were each reported as
            // the user's change, and each batch of them dropped the cached working-copy status, so
            // the commit that usually follows a new branch had to ask SVN all over again.
            VcsOperationResult result;
            using (_switchToBranch
                       ? MonitorPause.Begin(FileMonitoringService, RepositoryService.GetRepositoriesSharingWorkingCopy(RepositoryId))
                       : null)
            {
                result = await RepositoryService.CreateBranchAsync(RepositoryId, _branchName, _switchToBranch);
            }

            if (result.WorkingCopyChanged)
                Outcome.WorkingCopyChanged = true;

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

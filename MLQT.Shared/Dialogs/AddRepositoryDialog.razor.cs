namespace MLQT.Shared.Dialogs;

public partial class AddRepositoryDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private IStyleCheckingService StyleCheckingService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    private int _activeIndex = 0;
    private string _path = "";
    private string _url = "";
    private string _checkoutPath = "";
    private bool _isReferenceOnly;

    // True when the chosen folder cannot be written to, which decides the checkbox for the user the
    // first time rather than leaving them to discover it from failed writes all session.
    private bool _readOnlyOnDisk;

    private RepositoryVcsType? _detectedVcsType = null;
    private string? _detectedVcsRoot = null;
    private bool _isLocal = true;
    private bool _isLoading = false;
    private string _loadingMessage = "";
    private string? _errorMessage;
    private string? _addedRepositoryId;

    private bool CanProceed => _activeIndex switch
    {
        0 => !string.IsNullOrWhiteSpace(_path),
        1 => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_checkoutPath),
        _ => false
    };

    private void OnPathChanged(string newPath)
    {
        _path = newPath;
        _errorMessage = null;

        if (!string.IsNullOrWhiteSpace(newPath))
        {
            var (vcsType, isLocal) = RepositoryService.DetectVcsType(newPath);
            _detectedVcsType = vcsType;
            _isLocal = isLocal;

            if (isLocal && vcsType != RepositoryVcsType.Local)
            {
                var root = RepositoryService.FindVcsRoot(newPath);
                _detectedVcsRoot = !string.Equals(root, newPath, StringComparison.OrdinalIgnoreCase) ? root : null;
            }
            else
            {
                _detectedVcsRoot = null;
            }

            // Offer the answer the filesystem already gives: a folder MLQT cannot write into is one it
            // could never keep settings, a baseline or accepted spellings in. Only ever ticks the box —
            // a user who has unticked it for a writable folder is not overruled by a later keystroke.
            _readOnlyOnDisk = !MLQT.Services.Helpers.DirectoryWritability.CanWriteInto(newPath);
            if (_readOnlyOnDisk)
                _isReferenceOnly = true;
        }
        else
        {
            _detectedVcsType = null;
            _detectedVcsRoot = null;
            _isLocal = true;
            _readOnlyOnDisk = false;
        }
    }

    private void OnURLChanged(string newPath)
    {
        _url = newPath;
        _errorMessage = null;

        if (!string.IsNullOrWhiteSpace(newPath))
        {
            var (vcsType, isLocal) = RepositoryService.DetectVcsType(newPath);
            _detectedVcsType = vcsType;
            _isLocal = isLocal;
        }
        else
        {
            _detectedVcsType = null;
            _isLocal = true;
        }
    }

    private async Task BrowseForFolder()
    {
        try
        {
            var folder = await FilePickerService.PickFolderAsync("Select Repository");
            if (!string.IsNullOrEmpty(folder))
            {
                OnPathChanged(folder);
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to browse: {ex.Message}";
        }
    }

    private async Task BrowseForCheckoutFolder()
    {
        try
        {
            var folder = await FilePickerService.PickFolderAsync("Select Checkout Location");
            if (!string.IsNullOrEmpty(folder))
            {
                _checkoutPath = folder;
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to browse: {ex.Message}";
        }
    }

    private async Task NextStep()
    {
        _errorMessage = null;
        // Add repository and load libraries
        await AddRepository();
    }

    private async Task AddRepository()
    {
        _isLoading = true;
        _loadingMessage = "Loading selected libraries...";
        StateHasChanged();

        //Need to check _path, _url and _checkoutPath here before proceeding
        if (_activeIndex==0) {
            if (string.IsNullOrEmpty(_path)) 
            {
                _errorMessage = "You need to select a directory before you can add a repository";
                return;
            } 
            else if (!Directory.Exists(_path)) 
            {
                _errorMessage = $"The directory selected does not exist ({@_path}). You need to select a directory on this machine";
                return;
            }
        }
        else 
        {
            if (string.IsNullOrEmpty(_url)) 
            {
                _errorMessage = "You need to specify a url to checkout the repository from";
                return;
            } 
            if (string.IsNullOrEmpty(_checkoutPath)) 
            {
                _errorMessage = "You need to specify a directory that the repository can be checked out to";
                return;
            } 
            if (!Directory.Exists(_checkoutPath)) 
            {
                try 
                {
                    Directory.CreateDirectory(_checkoutPath);
                }
                catch (Exception ex) 
                {
                    _errorMessage = $"The specified checkout directory does not exist and failed to create it.\n{ex.ToString()}";
                    return;
                }
            }
        }

        try
        {
            var result = await RepositoryService.AddRepositoryAsync(
                _activeIndex == 0 ? _path : _url,
                _isLocal ? null : _checkoutPath,
                null,
                isReferenceOnly: _isReferenceOnly);

            if (result.Success && result.Repository != null)
            {
                _addedRepositoryId = result.Repository.Id;

                foreach (var warning in result.Warnings)
                {
                    Snackbar.Add(warning, MudBlazor.Severity.Warning);
                }
            }
            else
            {
                _errorMessage = result.ErrorMessage ?? "Failed to add repository.";
            }

            if (_addedRepositoryId != null)
            {
                var discoveredLibraries = result.DiscoveredLibraries;
                var selectedLibraries = new HashSet<string>(discoveredLibraries.Select(d => d.RelativePath));
                await RepositoryService.LoadLibrariesAsync(_addedRepositoryId, selectedLibraries);
                var repository = RepositoryService.GetRepository(_addedRepositoryId);

                //Start style checking
                // Offload to a background thread: StartBackgroundChecking does
                // sync-over-async work (settings + custom dictionary load) whose
                // continuations would post back to the Blazor UI sync context. Running
                // it inline here — on the UI thread after the awaits above — deadlocks.
                if (repository != null)
                    await Task.Run(() => StyleCheckingService.StartBackgroundChecking(repository));
            }

            MudDialog?.Close(DialogResult.Ok(_addedRepositoryId));
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

    private void Cancel()
    {
        // If we already added the repository but user cancels, remove it
        if (_addedRepositoryId != null)
        {
            var repo = RepositoryService.GetRepository(_addedRepositoryId);
            if (repo != null && repo.LibraryIds.Count == 0)
            {
                RepositoryService.RemoveRepository(_addedRepositoryId, false);
            }
        }
        MudDialog?.Cancel();
    }

    private void OnTabChanged(int activeIndex) {
        _activeIndex = activeIndex;
        if (activeIndex == 0 && string.IsNullOrEmpty(_path))
        {
            _detectedVcsType = null;
            _detectedVcsRoot = null;
        }
        else if (activeIndex == 1 && string.IsNullOrEmpty(_url))
        {
            _detectedVcsType = null;
            _detectedVcsRoot = null;
        }
    }
}

using Nextended.Core.COM;
using Nextended.Core.DeepClone;
using ModelicaParser.StyleRules;

namespace MLQT.Shared.Components;

public partial class SettingsRepositories : IDisposable
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;
    [Inject] private ISettingsService SettingsService { get; set; } = null!;
    [Inject] private IDictionaryManagerService DictionaryManagerService { get; set; } = null!;
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    /// <summary>
    /// Marking a repository reference only takes it out of checking, coverage and formatting, and
    /// stops MLQT writing into it. Applied to the live repository at once so the rest of the app stops
    /// treating it as the user's own code without waiting for Apply — which is also what makes the
    /// panel below it disappear.
    /// </summary>
    private void OnReferenceOnlyChanged(bool value)
    {
        if (_selectedItem is null)
            return;

        _selectedItem.IsReferenceOnly = value;
        StateHasChanged();
    }

    private List<Repository> _repositories = new();
    private Repository _selectedItem = null!;
    private Repository _backupItem = null!;
    private StyleCheckingSettings SelectedSettings => _selectedItem.StyleSettings ??= new StyleCheckingSettings();
    private bool _editRepository = false;
    private readonly DialogOptions _dialogOptions = new() { FullWidth = false };
    private List<DictionaryInfo> _availableDictionaries = new();
    private string _newRepoExceptionName = "";
    private string _newBranchDirectory = "";
    private string _newExcludedLibrary = "";

    // Project management state
    private List<ProjectProfile> _projects = new();
    private string? _activeProjectId;
    private bool _showProjectNameInput;
    private string _projectNameInput = "";
    private string _projectNameLabel = "";

    // Rename state (inline within expansion panel title)
    private string? _renamingProjectId;
    private string _renameInput = "";

    // Helper to get repo count for active project from live data
    private IList<RepositorySettingsEntry> _activeRepos =>
        _repositories.Select(r => new RepositorySettingsEntry { Name = r.Name, VcsType = r.VcsType.ToString(), LocalPath = r.LocalPath, VcsRootPath = r.VcsRootPath }).ToList();

    protected override void OnInitialized()
    {
        NavState.OnSaveSettings += SaveSettings;
        RepositoryService.OnRepositoriesChanged += OnRepositoriesChanged;
        DictionaryManagerService.OnDictionariesChanged += OnDictionariesChanged;
        _availableDictionaries = DictionaryManagerService.GetAvailableDictionaries().ToList();
        RefreshProjects();
        OnRepositoriesChanged();
        base.OnInitialized();
    }

    /// <summary>
    /// Unsubscribes from the singleton services this component listens to.
    ///
    /// <para>Blazor calls this because the component declares <c>@implements IDisposable</c> at the
    /// top of the file. It did not: the method was here, named <c>OnDispose</c>, <c>protected</c>, and
    /// called by nothing — so every handler stayed on the singleton after the component was gone.
    /// The settings tabs are re-created on every switch (MudTabs renders only the active panel), so
    /// the subscriptions accumulated for the life of the process: <b>Save Settings</b> ran once per
    /// instance ever created, and each dead one raised <c>StateHasChanged</c> on itself.</para>
    /// </summary>
    public void Dispose()
    {
        RepositoryService.OnRepositoriesChanged -= OnRepositoriesChanged;
        NavState.OnSaveSettings -= SaveSettings;
        DictionaryManagerService.OnDictionariesChanged -= OnDictionariesChanged;
    }

    private void RefreshProjects()
    {
        _projects = RepositoryService.GetProjects().ToList();
        var activeProject = RepositoryService.GetActiveProject();
        _activeProjectId = activeProject?.Id ?? _projects.FirstOrDefault()?.Id;
    }

    private async void SaveSettings()
    {
        try
        {
            await RepositoryService.SaveRepositorySettingsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving settings: {ex.Message}");
        }
    }

    private async void OnRepositoriesChanged()
    {
        await InvokeAsync(() =>
        {
            _repositories.Clear();
            _repositories.AddRange(RepositoryService.Repositories);
            StateHasChanged();
        });
    }

    // ========== Project Management ==========

    private void CreateNewProject()
    {
        _projectNameInput = "";
        _projectNameLabel = "New Project Name";
        _showProjectNameInput = true;
    }

    private async Task ConfirmProjectName()
    {
        _showProjectNameInput = false;

        if (string.IsNullOrWhiteSpace(_projectNameInput))
            return;

        var newProject = RepositoryService.CreateProject(_projectNameInput.Trim());
        RefreshProjects();
        StateHasChanged();

        // Automatically load the new empty project
        NavState.ProjectSwitchStarting();
        await RepositoryService.SwitchProjectAsync(newProject.Id);
        RefreshProjects();
        await InvokeAsync(() => Snackbar.Add($"Project '{newProject.Name}' created and loaded", Severity.Success));
        StateHasChanged();
    }

    private void CancelProjectName()
    {
        _showProjectNameInput = false;
    }

    private void StartRenameProject(ProjectProfile project)
    {
        _renamingProjectId = project.Id;
        _renameInput = project.Name;
    }

    private void ConfirmRename()
    {
        if (_renamingProjectId != null && !string.IsNullOrWhiteSpace(_renameInput))
        {
            RepositoryService.RenameProject(_renamingProjectId, _renameInput.Trim());
            RefreshProjects();
        }
        _renamingProjectId = null;
        StateHasChanged();
    }

    private void CancelRename()
    {
        _renamingProjectId = null;
        StateHasChanged();
    }

    private async Task LoadProject(string projectId)
    {
        // Signal MainLayout to show progress dialog immediately before loading starts
        NavState.ProjectSwitchStarting();
        await RepositoryService.SwitchProjectAsync(projectId);
        RefreshProjects();
        StateHasChanged();
    }

    private async Task DeleteProject(ProjectProfile project)
    {
        if (_projects.Count <= 1)
            return;

        var parameters = new DialogParameters<ConfirmDeleteProjectDialog>
        {
            { x => x.ProjectName, project.Name }
        };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true };
        var dialog = await DialogService.ShowAsync<ConfirmDeleteProjectDialog>("Delete Project", parameters, options);
        var result = await dialog.Result;

        if (result == null || result.Canceled)
            return;

        var wasActive = project.Id == _activeProjectId;
        var deleted = RepositoryService.DeleteProject(project.Id);
        if (!deleted)
            return;

        if (wasActive)
        {
            // Switch to the first remaining project
            RefreshProjects();
            var firstProject = _projects.FirstOrDefault();
            if (firstProject != null)
            {
                NavState.ProjectSwitchStarting();
                await RepositoryService.SwitchProjectAsync(firstProject.Id);
                RefreshProjects();
            }
        }
        else
        {
            RefreshProjects();
        }

        await InvokeAsync(() => Snackbar.Add("Project deleted", Severity.Success));
        StateHasChanged();
    }

    // ========== Repository Management ==========

    private void OnRepoRowClick(TableRowClickEventArgs<Repository> args)
    {
        if (args.Item != null)
            OnRepoClick(args.Item);
    }

    private void OnRepoClick(Repository repo)
    {
        _selectedItem = repo;
        _ = SelectedSettings; // ensure StyleSettings is initialized before CloneDeep
        _backupItem = _selectedItem.CloneDeep();
        _editRepository = true;
        StateHasChanged();
    }

    private async Task ConfirmChanges()
    {
        var oldSettings = _backupItem.StyleSettings ?? new StyleCheckingSettings();
        var newSettings = SelectedSettings;

        bool styleSettingsChanged = newSettings.ChecksDifferFrom(oldSettings);
        bool formattingChanged = newSettings.ApplyFormattingRules &&
                                 (!oldSettings.ApplyFormattingRules || newSettings.FormattingDiffersFrom(oldSettings));

        _editRepository = false;
        await RepositoryService.SaveRepositorySettingsAsync();

        if (styleSettingsChanged || formattingChanged)
            NavState.RepositorySettingsApplied(_selectedItem.Id, formattingChanged, styleSettingsChanged);

        StateHasChanged();
    }

    private void FormatAllFiles()
    {
        _editRepository = false;
        NavState.RepositorySettingsApplied(_selectedItem.Id, formattingChanged: true, styleSettingsChanged: false);
        Snackbar.Add("Full format started for all files in repository...", Severity.Normal);
        StateHasChanged();
    }

    private void CancelChanges()
    {
        _editRepository = false;
        _selectedItem.Name = _backupItem.Name;
        _selectedItem.RemotePath = _backupItem.RemotePath;
        _selectedItem.LocalPath = _backupItem.LocalPath;
        _selectedItem.StyleSettings = _backupItem.StyleSettings;
        StateHasChanged();
    }

    // The rules shown in the data-driven "Static analysis" section: everything in the catalog whose
    // category is rendered from the catalog. Which categories those are is declared in
    // RuleSettingsLayout alongside the rules this dialog places by hand, so one list answers "where
    // is this rule set?" for every rule — and a rule with no answer fails RuleSettingsLayoutTests
    // instead of quietly appearing nowhere.
    private static readonly IReadOnlyList<RuleDefinition> AnalysisRules =
        RuleCatalog.Configurable
            .Where(r => RuleSettingsLayout.CatalogDrivenCategories.Contains(r.Category))
            .OrderBy(r => r.Category, StringComparer.Ordinal)
            .ThenBy(r => r.Title, StringComparer.Ordinal)
            .ToList();

    private void RemoveRepository()
    {
        _editRepository = false;
        if (_selectedItem == null)
            return;
        RepositoryService.RemoveRepository(_selectedItem.Id, true);
    }

    private void OnInitialEqFirst(bool value)
    {
        SelectedSettings.InitialEQAlgoFirst = value;
        if (value)
            SelectedSettings.InitialEQAlgoLast = false;
        StateHasChanged();
    }

    private void OnInitialEqLast(bool value)
    {
        SelectedSettings.InitialEQAlgoLast = value;
        if (value)
            SelectedSettings.InitialEQAlgoFirst = false;
        StateHasChanged();
    }

    private void OnRepoLanguagesChanged(IEnumerable<string> selected)
    {
        SelectedSettings.SpellCheckLanguages = selected.ToList();
    }

    /// <summary>
    /// The dictionaries to offer: the ones installed here, plus any language the settings already
    /// ask for that is not.
    ///
    /// <para>A language with no item behind it is one the user cannot see in the list and cannot
    /// remove, and the setting is committed — so it is listed, marked, and left to them to decide
    /// about.</para>
    /// </summary>
    private List<(string Code, string Label)> LanguageOptions
    {
        get
        {
            var options = _availableDictionaries
                .Select(d => (Code: d.LanguageCode, Label: d.IsBundled ? d.DisplayName : $"{d.DisplayName} (imported)"))
                .ToList();

            var known = options.Select(o => o.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in SelectedSettings.SpellCheckLanguages)
            {
                if (!string.IsNullOrWhiteSpace(code) && known.Add(code))
                    options.Add((code, $"{code} (not installed)"));
            }

            return options;
        }
    }

    /// <summary>What an empty selection means, so nobody reads it as "no spell checking".</summary>
    private string LanguageHelperText =>
        SelectedSettings.SpellCheckLanguages.Count == 0
            ? "None selected — the bundled en_US and en_GB are used."
            : "Saved to the repository's .mlqt/settings.json, so CI checks with the same dictionaries.";

    /// <summary>
    /// Says so when this machine has no dictionary for a language the settings ask for. The languages
    /// are committed but the dictionaries are installed per machine, so this is the gap between a
    /// colleague's results and yours — the CLI has always warned about it.
    /// </summary>
    private string? MissingLanguageWarning =>
        DictionaryAvailability.WarningFor(SelectedSettings.SpellCheckLanguages, DictionaryManagerService);

    private async Task ImportRepoLanguageDictionary()
    {
        var result = await FilePickerService.PickModelicaFileAsync(".aff");
        if (result?.FilePath == null)
            return;

        var affPath = result.FilePath;
        var dicPath = Path.ChangeExtension(affPath, ".dic");

        if (!File.Exists(dicPath))
        {
            Snackbar.Add($"Missing matching .dic file: {Path.GetFileName(dicPath)}", Severity.Error);
            return;
        }

        var langCode = await DictionaryManagerService.ImportDictionaryAsync(affPath, dicPath);
        if (langCode != null)
        {
            // A new list rather than an Add: the select redraws off the instance, and the change
            // detection that decides whether to re-check compares against a clone of the old one.
            if (!SelectedSettings.SpellCheckLanguages.Contains(langCode))
                SelectedSettings.SpellCheckLanguages = [.. SelectedSettings.SpellCheckLanguages, langCode];

            Snackbar.Add($"Imported dictionary: {langCode}", Severity.Success);
        }
        else
        {
            Snackbar.Add("Failed to import dictionary", Severity.Error);
        }
    }

    private void OnRepoPresetChanged(string presetName)
    {
        var preset = NamingConventionPresets.All.FirstOrDefault(p => p.Name == presetName);
        if (preset.Factory != null)
        {
            SelectedSettings.NamingConvention = preset.Factory();
        }
        else
        {
            SelectedSettings.NamingConvention.PresetName = "Custom";
        }
        StateHasChanged();
    }

    private void OnRepoNamingStyleChanged(Action applyChange)
    {
        applyChange();
        SelectedSettings.NamingConvention.PresetName = "Custom";
        StateHasChanged();
    }

    private void AddRepoExceptionName()
    {
        var name = _newRepoExceptionName.Trim();
        if (!string.IsNullOrEmpty(name) && !SelectedSettings.NamingConvention.ExceptionNames.Contains(name))
        {
            SelectedSettings.NamingConvention.ExceptionNames.Add(name);
            _newRepoExceptionName = "";
            StateHasChanged();
        }
    }

    private void RemoveRepoExceptionName(string name)
    {
        SelectedSettings.NamingConvention.ExceptionNames.Remove(name);
        StateHasChanged();
    }

    private void AddExcludedLibrary()
    {
        var name = _newExcludedLibrary.Trim();
        if (!string.IsNullOrEmpty(name) &&
            !SelectedSettings.ExcludedLibraries.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            SelectedSettings.ExcludedLibraries.Add(name);
            _newExcludedLibrary = "";
            StateHasChanged();
        }
    }

    private void RemoveExcludedLibrary(string name)
    {
        SelectedSettings.ExcludedLibraries.Remove(name);
        StateHasChanged();
    }

    private void AddBranchDirectory()
    {
        var dir = _newBranchDirectory.Trim();
        if (!string.IsNullOrEmpty(dir) && !SelectedSettings.SvnBranchDirectories.Contains(dir))
        {
            SelectedSettings.SvnBranchDirectories.Add(dir);
            _newBranchDirectory = "";
            StateHasChanged();
        }
    }

    private void RemoveBranchDirectory(string dir)
    {
        SelectedSettings.SvnBranchDirectories.Remove(dir);
        StateHasChanged();
    }

    private List<string> GetRepoPatterns(string slotKey)
    {
        if (!SelectedSettings.NamingConvention.AdditionalPatterns.TryGetValue(slotKey, out var patterns))
        {
            patterns = new List<string>();
            SelectedSettings.NamingConvention.AdditionalPatterns[slotKey] = patterns;
        }
        return patterns;
    }

    private void OnRepoPatternsChanged(string slotKey, List<string> patterns)
    {
        SelectedSettings.NamingConvention.AdditionalPatterns[slotKey] = patterns;
        SelectedSettings.NamingConvention.PresetName = "Custom";
        StateHasChanged();
    }

    private async void OnDictionariesChanged()
    {
        await InvokeAsync(() =>
        {
            _availableDictionaries = DictionaryManagerService.GetAvailableDictionaries().ToList();
            StateHasChanged();
        });
    }
}

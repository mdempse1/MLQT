namespace MLQT.Shared.Dialogs;

public partial class ProjectSelectionDialog
{
    [Inject] private ISettingsService SettingsService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    private List<ProjectProfile> _projects = new();
    private string? _selectedProject;
    private bool _showNewProjectControls = false;

    protected override async Task OnInitializedAsync()
    {
        // Read projects directly from settings: this dialog runs before IRepositoryService has
        // loaded them.
        var settings = await SettingsService.GetAsync("Repositories", new RepositorySettingsCollection());
        _projects = settings.Projects;

        // Preselect the active project, or the first one when the saved ActiveProjectId names a
        // project that is no longer there — a deleted or renamed project leaves exactly that state.
        _selectedProject = (_projects.FirstOrDefault(p => p.Id == settings.ActiveProjectId)
                            ?? _projects.FirstOrDefault())?.Id;
    }

    private void Select()
    {
        MudDialog?.Close(DialogResult.Ok(_selectedProject));
    }

    /// <summary>
    /// Why the name being typed cannot be used, or <c>null</c> when it can.
    ///
    /// <para>Evaluated while the field has focus (it is <c>Immediate</c>), so the reason appears as
    /// the user types rather than after they commit. Only meaningful while the new-project controls
    /// are showing: outside that, <c>_selectedProject</c> holds the id of the radio selection rather
    /// than a name being composed.</para>
    /// </summary>
    internal string? NewProjectNameError =>
        _showNewProjectControls ? ProjectNameRules.Validate(_selectedProject, _projects) : null;

    private void ConfirmProjectName()
    {
        // The button is disabled while this is non-null, so reaching here with one means the click
        // arrived some other way.
        if (NewProjectNameError is not null)
            return;

        _showNewProjectControls = false;

        if (string.IsNullOrWhiteSpace(_selectedProject))
            return;

        // Add a placeholder project so it appears in the radio list.
        // Use the name as the ID — MainLayout detects this isn't a real project ID
        // and calls CreateProject with it.
        var newProject = new ProjectProfile { Id = _selectedProject.Trim(), Name = _selectedProject.Trim() };
        _projects.Add(newProject);
        _selectedProject = newProject.Id;
    }
}

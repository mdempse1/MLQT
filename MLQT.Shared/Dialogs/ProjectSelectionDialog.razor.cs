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
    /// What clicking a row does: pick that project and load it, in one action (B194).
    ///
    /// <para>The radio alone only moved the selection, so the only way to get out of this dialog was
    /// the button — and a startup list whose rows do nothing reads as broken rather than as modal.
    /// It sets the selection explicitly rather than relying on the click reaching the radio first,
    /// because the row is the outer element and the order the two handlers run in is not something
    /// to depend on.</para>
    /// </summary>
    private void SelectAndLoad(string projectId)
    {
        _selectedProject = projectId;
        Select();
    }

    private void ConfirmProjectName()
    {
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

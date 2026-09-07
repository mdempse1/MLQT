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

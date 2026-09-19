namespace MLQT.Shared.Components;

public partial class CurrentModelDisplay : IDisposable
{
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;

    private string _currentModelName = "";
    private string _currentModelFileName = "";

    protected override void OnInitialized()
    {
        NavState.OnChangeModel += OnModelSelected;
        NavState.OnSelectedModelsChanged += OnSelectedModelsChanged;
    }

    private async void OnModelSelected()
    {
        _currentModelName = NavState.ModelID;
        _currentModelFileName = FilePathOf(NavState.ModelID);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Names the class the back button would return to, so the user knows before pressing it. A
    /// plain "Back" says nothing after three or four moves, which is when it is actually wanted.
    /// </summary>
    private string BackTooltip =>
        NavState.Back.Count == 0 ? "Back" : $"Back to {NavState.Back[0]}";

    private void GoBack() => NavState.GoBack();

    private void GoForward() => NavState.GoForward();

    private async void OnSelectedModelsChanged()
    {
        _currentModelName = string.Join(", ", NavState.SelectedModelIDs);
        _currentModelFileName = FilePathOf(NavState.ModelID);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The path of the file a class is stored in, or empty when the class is unknown or its file
    /// node is not in the graph.
    /// </summary>
    internal string FilePathOf(string modelId)
    {
        var modelNode = LibraryDataService.CombinedGraph.GetNode<ModelNode>(modelId);
        if (modelNode?.ContainingFileId is null)
            return string.Empty;

        var fileNode = LibraryDataService.CombinedGraph.GetNode<FileNode>(modelNode.ContainingFileId);
        return fileNode?.FilePath ?? string.Empty;
    }

    public void Dispose()
    {
        NavState.OnChangeModel -= OnModelSelected;
        NavState.OnSelectedModelsChanged -= OnSelectedModelsChanged;
    }
}

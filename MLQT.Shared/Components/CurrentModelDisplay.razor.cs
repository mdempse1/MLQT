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

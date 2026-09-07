using Microsoft.AspNetCore.Components;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;

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
        var modelNode = LibraryDataService.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>(NavState.ModelID);
        if (modelNode?.ContainingFileId != null)
        {
            var fileNode = LibraryDataService.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(modelNode.ContainingFileId);
            _currentModelFileName = fileNode?.FilePath ?? string.Empty;
        }
        else
            _currentModelFileName = string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private async void OnSelectedModelsChanged()
    {
        _currentModelName = string.Join(", ", NavState.SelectedModelIDs);
        _currentModelFileName = string.Empty;
        var modelNode = LibraryDataService.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>(NavState.ModelID);
        if (modelNode?.ContainingFileId != null)
        {
            var fileNode = LibraryDataService.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(modelNode.ContainingFileId);
            _currentModelFileName = fileNode?.FilePath ?? string.Empty;
        }
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        NavState.OnChangeModel -= OnModelSelected;
        NavState.OnSelectedModelsChanged -= OnSelectedModelsChanged;
    }
}

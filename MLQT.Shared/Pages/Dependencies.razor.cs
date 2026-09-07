namespace MLQT.Shared.Pages;

public partial class Dependencies : IAsyncDisposable
{
    [Inject] private IFilePickerService FilePickerService { get; set; } = null!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;
    [Inject] private AppState NavState { get; set; } = null!;
    [Inject] private ILibraryDataService LibraryDataService { get; set; } = null!;
    [Inject] private IImpactAnalysisService ImpactAnalysisService { get; set; } = null!;

    // Analysis result from service
    private ImpactAnalysisResult? _analysisResult;
    private List<NetworkNode> _networkNodes = new();
    private List<NetworkEdge> _networkEdges = new();
    private List<ImpactDetail> _impactDetails = new();
    private int _impactedModelsCount = 0;
    private string _searchString = "";

    // Graph component data (generic, not domain-specific)
    private CytoscapeGraph? _cytoscapeGraph;
    private List<DiagramNode> _graphNodes = new();
    private List<DiagramEdge> _graphEdges = new();
    private string _selectedLayout = "cose";

    // Deferred analysis state
    private bool _isRunningAnalysis = false;

    // Highlighting state
    private string? _highlightedNodeId = null;
    private HashSet<string> _connectedNodeIds = new();

    protected override void OnInitialized()
    {
        // Enable multi-select mode when Dependencies page is shown
        NavState.ChangeSelectionMode(SelectionMode.MultiSelection);
        NavState.OnSelectedModelsChanged += OnSelectedModelsChanged;
        NavState.OnDeferredAnalysisCompleted += OnDeferredAnalysisCompleted;
        NavState.SetSelectedModels(new List<string>() { NavState.ModelID });

        base.OnInitialized();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && NavState.SelectedModelIDs.Count > 0)
        {
            AnalyzeDependencies();
            StateHasChanged();
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    public async ValueTask DisposeAsync()
    {
        // Disable multi-select mode when leaving Dependencies page
        NavState.ChangeSelectionMode(SelectionMode.SingleSelection);
        NavState.OnSelectedModelsChanged -= OnSelectedModelsChanged;
        NavState.OnDeferredAnalysisCompleted -= OnDeferredAnalysisCompleted;
        // CytoscapeGraph component manages its own JS disposal via IAsyncDisposable
    }

    private async void OnDeferredAnalysisCompleted()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async Task RunDependencyAnalysisAsync()
    {
        _isRunningAnalysis = true;
        StateHasChanged();
        try
        {
            await NavState.RunDeferredDependenciesAsync();
        }
        finally
        {
            _isRunningAnalysis = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async void OnSelectedModelsChanged()
    {
        await InvokeAsync(() =>
        {
            _highlightedNodeId = null;
            _connectedNodeIds.Clear();
            AnalyzeDependencies();
            StateHasChanged();
        });
    }

    private void AnalyzeDependencies()
    {
        _networkNodes.Clear();
        _networkEdges.Clear();
        _impactDetails.Clear();
        _graphNodes.Clear();
        _graphEdges.Clear();
        _searchString = string.Empty;

        if (NavState.SelectedModelIDs.Count == 0)
        {
            _impactedModelsCount = 0;
            return;
        }

        // Use the service to perform the analysis
        _analysisResult = ImpactAnalysisService.AnalyzeImpact(
            LibraryDataService.CombinedGraph,
            NavState.SelectedModelIDs);

        // Copy results for rendering and tooltip logic
        _networkNodes = _analysisResult.Nodes;
        _networkEdges = _analysisResult.Edges;
        _impactDetails = _analysisResult.ImpactDetails;
        _impactedModelsCount = _analysisResult.ImpactedModelsCount;

        // Map to generic graph types for CytoscapeGraph component
        _graphNodes = _networkNodes.Select(n => new DiagramNode
        {
            Id = n.Id,
            Label = n.ShortName,
            FullName = n.FullName,
            Color = n.Color,
            BorderColor = n.BorderColor
        }).ToList();

        _graphEdges = _networkEdges.Select(e => new DiagramEdge
        {
            FromId = e.FromId,
            ToId = e.ToId
        }).ToList();
    }

    private async Task OnNodeClicked(string nodeId)
    {
        if (_highlightedNodeId == nodeId)
        {
            await ClearHighlight();
            _searchString = string.Empty;
        }
        else
        {
            _highlightedNodeId = nodeId;
            _connectedNodeIds = ImpactAnalysisService.GetConnectedNodes(_networkEdges, nodeId);
            if (_cytoscapeGraph != null)
                await _cytoscapeGraph.HighlightNodeAsync(nodeId);
            _searchString = nodeId;
            StateHasChanged();
        }
    }

    private async Task ClearHighlight()
    {
        _highlightedNodeId = null;
        _connectedNodeIds.Clear();
        if (_cytoscapeGraph != null)
            await _cytoscapeGraph.ClearHighlightAsync();
        StateHasChanged();
    }

    private async Task OnLayoutChanged(string layoutName)
    {
        _selectedLayout = layoutName;
        if (_cytoscapeGraph != null)
            await _cytoscapeGraph.RelayoutAsync(layoutName);
    }

    private async Task OnImpactRowClicked(string modelId)
    {
        await OnNodeClicked(modelId);
    }

    private async Task OnImpactTableRowClicked(TableRowClickEventArgs<ImpactDetail> args)
    {
        if (args.Item != null)
            await OnImpactRowClicked(args.Item.ModelId);
    }

    private string GetImpactRowClass(ImpactDetail impact, int index) =>
        impact.ModelId == _highlightedNodeId ? "impact-row-selected" : string.Empty;

    private string MakeShortName(string name)
    {
        var idx = name.IndexOf(".") + 1;
        return name.Length <= 40 || name.IndexOf(".") == -1 || name.Count(c => c == '.') <= 1 ?
                name :
                name.Substring(0, name.IndexOf(".", idx)) + "..." + name.Substring(name.LastIndexOf("."));
    }

    private List<string> MakeShortNames(List<string> names)
    {
        List<string> shortNames = new();
        foreach (var name in names)
        {
            shortNames.Add(MakeShortName(name));
        }
        return shortNames;
    }

    private bool FilterFunc1(ImpactDetail element) => FilterFunc(element, _searchString);

    private bool FilterFunc(ImpactDetail element, string searchString)
    {
        if (string.IsNullOrWhiteSpace(searchString))
            return true;
 
        var stringsLowerCase = searchString.ToLower().Split(' ');
        if (stringsLowerCase.Any(element.ModelId.ToLower().Contains))
            return true;
        if (stringsLowerCase.Any(string.Join(" ", element.ImpactedBy).ToLower().Contains))
            return true;

        return false;
    }
}

using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the ImpactAnalysisService class.
/// </summary>
public class ImpactAnalysisServiceTests
{
    private DirectedGraph CreateGraphWithDependencies()
    {
        var graph = new DirectedGraph();

        // Create models
        var model1 = new ModelNode("Model1", new ModelDefinition("Model1", "model Model1 end Model1;"));
        var model2 = new ModelNode("Model2", new ModelDefinition("Model2", "model Model2 end Model2;"));
        var model3 = new ModelNode("Model3", new ModelDefinition("Model3", "model Model3 end Model3;"));
        var model4 = new ModelNode("Model4", new ModelDefinition("Model4", "model Model4 end Model4;"));

        model1.ClassType = "model";
        model2.ClassType = "model";
        model3.ClassType = "model";
        model4.ClassType = "model";

        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddNode(model3);
        graph.AddNode(model4);

        // Model2 uses Model1, Model3 uses Model2, Model4 uses Model1
        graph.AddModelUsesModel("Model2", "Model1");
        graph.AddModelUsesModel("Model3", "Model2");
        graph.AddModelUsesModel("Model4", "Model1");

        return graph;
    }

    [Fact]
    public void AnalyzeImpact_WithEmptySelection_ReturnsEmptyResult()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string>());

        Assert.Empty(result.Nodes);
        Assert.Empty(result.Edges);
        Assert.Empty(result.ImpactDetails);
        Assert.Equal(0, result.ImpactedModelsCount);
    }

    [Fact]
    public void AnalyzeImpact_WithInvalidModelId_ReturnsEmptyResult()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "NonExistent" });

        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void AnalyzeImpact_FindsDirectDependents()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        // Model2 and Model4 directly depend on Model1
        // Model3 transitively depends on Model1 through Model2
        Assert.Equal(3, result.ImpactedModelsCount);
    }

    [Fact]
    public void AnalyzeImpact_FindsTransitiveDependents()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        // Model3 should be found as it transitively depends on Model1 via Model2
        var impactedIds = result.ImpactDetails.Select(d => d.ModelId).ToList();
        Assert.Contains("Model3", impactedIds);
    }

    [Fact]
    public void AnalyzeImpact_CreatesNetworkNodes()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        // Should have nodes for Model1 (selected) plus impacted models
        Assert.True(result.Nodes.Count >= 1);
        Assert.Contains(result.Nodes, n => n.Id == "Model1" && n.IsSelected);
    }

    [Fact]
    public void AnalyzeImpact_SelectedNodeHasCorrectColor()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        var selectedNode = result.Nodes.First(n => n.Id == "Model1");
        Assert.Equal("var(--mud-palette-success)", selectedNode.Color);
    }

    [Fact]
    public void AnalyzeImpact_ImpactedNodesHaveCorrectColor()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        var impactedNode = result.Nodes.FirstOrDefault(n => n.Id == "Model2");
        if (impactedNode != null)
        {
            Assert.Equal("var(--mud-palette-warning)", impactedNode.Color);
            Assert.True(impactedNode.IsImpacted);
            Assert.False(impactedNode.IsSelected);
        }
    }

    [Fact]
    public void AnalyzeImpact_CreatesNetworkEdges()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        Assert.NotEmpty(result.Edges);
        Assert.Contains(result.Edges, e => e.FromId == "Model1" && e.ToId == "Model2");
    }

    [Fact]
    public void AnalyzeImpact_SetsNodePositions()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        foreach (var node in result.Nodes)
        {
            Assert.True(node.X > 0);
            Assert.True(node.Y > 0);
        }
    }

    [Fact]
    public void AnalyzeImpact_SetsEdgeCoordinates()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        foreach (var edge in result.Edges)
        {
            Assert.True(edge.X1 >= 0);
            Assert.True(edge.Y1 >= 0);
            Assert.True(edge.X2 >= 0);
            Assert.True(edge.Y2 >= 0);
        }
    }

    [Fact]
    public void AnalyzeImpact_CreatesImpactDetails()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        Assert.NotEmpty(result.ImpactDetails);
        var model2Detail = result.ImpactDetails.FirstOrDefault(d => d.ModelId == "Model2");
        Assert.NotNull(model2Detail);
        Assert.Contains("Model1", model2Detail.ImpactedBy);
    }

    [Fact]
    public void AnalyzeImpact_SetsSvgDimensions()
    {
        var service = new ImpactAnalysisService();
        var graph = CreateGraphWithDependencies();

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" }, 800, 600);

        Assert.True(result.SvgWidth >= 700);
        Assert.True(result.SvgHeight >= 450);
    }

    [Fact]
    public void GetConnectedNodes_ReturnsConnectedNodes()
    {
        var service = new ImpactAnalysisService();
        var edges = new List<NetworkEdge>
        {
            new NetworkEdge { FromId = "A", ToId = "B" },
            new NetworkEdge { FromId = "A", ToId = "C" },
            new NetworkEdge { FromId = "D", ToId = "A" }
        };

        var connected = service.GetConnectedNodes(edges, "A");

        Assert.Equal(3, connected.Count);
        Assert.Contains("B", connected);
        Assert.Contains("C", connected);
        Assert.Contains("D", connected);
    }

    [Fact]
    public void GetConnectedNodes_WithNoConnections_ReturnsEmpty()
    {
        var service = new ImpactAnalysisService();
        var edges = new List<NetworkEdge>
        {
            new NetworkEdge { FromId = "A", ToId = "B" }
        };

        var connected = service.GetConnectedNodes(edges, "C");

        Assert.Empty(connected);
    }

    [Fact]
    public void AnalyzeImpact_NodeShortNameIsTruncated()
    {
        var service = new ImpactAnalysisService();
        var graph = new DirectedGraph();

        var longNameModel = new ModelNode("VeryLongModelName", new ModelDefinition(
            "VeryLongModelName",
            "model VeryLongModelName end VeryLongModelName;"));
        longNameModel.ClassType = "model";
        graph.AddNode(longNameModel);

        var result = service.AnalyzeImpact(graph, new List<string> { "VeryLongModelName" });

        var node = result.Nodes.FirstOrDefault(n => n.Id == "VeryLongModelName");
        Assert.NotNull(node);
        Assert.True(node.ShortName.Length <= 9); // 7 chars + ".."
    }

    [Fact]
    public void AnalyzeImpact_UsesFullNameForQualifiedIds()
    {
        var service = new ImpactAnalysisService();
        var graph = new DirectedGraph();

        var model = new ModelNode("Package.SubPackage.Model", new ModelDefinition(
            "Model",
            "model Model end Model;"));
        model.ClassType = "model";
        graph.AddNode(model);

        var result = service.AnalyzeImpact(graph, new List<string> { "Package.SubPackage.Model" });

        var node = result.Nodes.First();
        Assert.Equal("Package.SubPackage.Model", node.FullName);
        Assert.Equal("Model", node.ShortName);
    }

    [Fact]
    public void AnalyzeImpact_ExcludesPlainPackageFromImpactedModels()
    {
        var service = new ImpactAnalysisService();
        var graph = new DirectedGraph();

        var model1 = new ModelNode("Model1", new ModelDefinition("Model1", "model Model1 end Model1;"));
        model1.ClassType = "model";

        var plainPkg = new ModelNode("SomePkg", new ModelDefinition("SomePkg", "package SomePkg end SomePkg;"));
        plainPkg.ClassType = "package";

        graph.AddNode(model1);
        graph.AddNode(plainPkg);

        // SomePkg "uses" Model1 (e.g. an edge exists in the graph)
        graph.AddModelUsesModel("SomePkg", "Model1");

        var result = service.AnalyzeImpact(graph, new List<string> { "Model1" });

        // Plain package must not appear as impacted
        Assert.DoesNotContain(result.ImpactDetails, d => d.ModelId == "SomePkg");
        Assert.DoesNotContain(result.Nodes, n => n.Id == "SomePkg");
    }

}

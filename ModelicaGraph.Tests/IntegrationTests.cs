using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Integration tests that verify end-to-end functionality of the ModelicaGraph library.
/// </summary>
public class IntegrationTests
{
    private readonly string _testFilesPath;

    public IntegrationTests()
    {
        _testFilesPath = Path.Combine(AppContext.BaseDirectory, "TestFiles");
    }

    [Fact]
    public void CompleteWorkflow_LoadAnalyzeQuery_Works()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = @"
            package MyLibrary
              model Component1
                Real x;
              equation
                x = 1.0;
              end Component1;

              model Component2
                Component1 comp1;
                Real y;
              equation
                y = comp1.x + 1.0;
              end Component2;

              model System
                Component1 c1;
                Component2 c2;
                Real z;
              equation
                z = c1.x + c2.y;
              end System;
            end MyLibrary;
        ";

        // Act - Load
        var fileNode = GraphBuilder.LoadModelicaFile(graph, "MyLibrary.mo", content);

        // Assert - Verify loading
        Assert.Equal(4, graph.ModelNodes.Count()); // MyLibrary + 3 components
        Assert.Single(graph.FileNodes);

        // Act - Analyze dependencies
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();

        // Assert - Verify dependencies
        var systemModel = graph.ModelNodes.First(m => m.Definition.Name == "System");
        var component2Model = graph.ModelNodes.First(m => m.Definition.Name == "Component2");

        // System uses Component1 and Component2
        var systemDeps = graph.GetUsedModels(systemModel.Id).ToList();
        Assert.Contains(systemDeps, m => m.Definition.Name == "Component1");
        Assert.Contains(systemDeps, m => m.Definition.Name == "Component2");

        // Component2 uses Component1
        var comp2Deps = graph.GetUsedModels(component2Model.Id).ToList();
        Assert.Contains(comp2Deps, m => m.Definition.Name == "Component1");

        // Act - Query reverse dependencies
        var component1Model = graph.ModelNodes.First(m => m.Definition.Name == "Component1");
        var usedBy = graph.GetModelUsedBy(component1Model.Id).ToList();

        // Assert - Component1 is used by Component2 and System
        Assert.Equal(2, usedBy.Count);
        Assert.Contains(usedBy, m => m.Definition.Name == "Component2");
        Assert.Contains(usedBy, m => m.Definition.Name == "System");
    }

    [Fact]
    public void MultipleFiles_WithCrossReferences_ResolvesCorrectly()
    {
        // Arrange
        var graph = new DirectedGraph();

        var file1Content = @"
            model BaseModel
              Real x;
            equation
              x = 1.0;
            end BaseModel;
        ";

        var file2Content = @"
            model DerivedModel
              BaseModel base;
              Real y;
            equation
              y = base.x * 2.0;
            end DerivedModel;
        ";

        // Act
        GraphBuilder.LoadModelicaFile(graph, "Base.mo", file1Content);
        GraphBuilder.LoadModelicaFile(graph, "Derived.mo", file2Content);
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();

        // Assert
        var derivedModel = graph.ModelNodes.First(m => m.Definition.Name == "DerivedModel");
        var baseModel = graph.ModelNodes.First(m => m.Definition.Name == "BaseModel");

        var dependencies = graph.GetUsedModels(derivedModel.Id).ToList();
        Assert.Contains(baseModel, dependencies);
    }

    [Fact]
    public void LargeGraph_PerformanceTest()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = new System.Text.StringBuilder();
        content.AppendLine("package LargeLibrary");

        // Generate 100 models
        for (int i = 0; i < 100; i++)
        {
            content.AppendLine($"  model Model{i}");
            content.AppendLine($"    Real x{i};");

            // Add dependencies to previous models
            if (i > 0)
            {
                int prevModel = i - 1;
                content.AppendLine($"    Model{prevModel} comp{prevModel};");
            }

            content.AppendLine("  end Model" + i + ";");
            content.AppendLine();
        }

        content.AppendLine("end LargeLibrary;");

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        GraphBuilder.LoadModelicaFile(graph, "Large.mo", content.ToString());
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();
        sw.Stop();

        // Assert
        Assert.Equal(101, graph.ModelNodes.Count()); // 100 models + package
        Assert.True(sw.ElapsedMilliseconds < 5000, "Loading and analysis should complete in under 5 seconds");

        // Verify some dependencies were created
        var lastModel = graph.ModelNodes.First(m => m.Definition.Name == "Model99");
        var deps = graph.GetUsedModels(lastModel.Id).ToList();
        Assert.NotEmpty(deps);
    }

    [Fact]
    public void NodeRemoval_MaintainsGraphIntegrity()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");

        graph.AddNode(fileNode);
        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddFileContainsModel("file1", "model1");
        graph.AddFileContainsModel("file1", "model2");
        graph.AddModelUsesModel("model1", "model2");

        // Act
        graph.RemoveNode("model1");

        // Assert
        Assert.Equal(2, graph.NodeCount); // file + model2
        // Note: RemoveNode doesn't automatically update ContainedModelIds in FileNode
        Assert.Empty(graph.GetModelUsedBy("model2")); // No incoming edges to model2
    }

    [Fact]
    public void TypedProperties_PersistThroughGraphOperations()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = @"
            package TestPkg
              model TestModel
                Real x;
              end TestModel;
            end TestPkg;
        ";

        // Act
        GraphBuilder.LoadModelicaFile(graph, "test.mo", content);

        var packageNode = graph.ModelNodes.First(m => m.Definition.Name == "TestPkg");

        // Query and verify typed properties persist
        var queriedNode = graph.GetNode<ModelNode>(packageNode.Id);

        // Assert
        Assert.NotNull(queriedNode);
        Assert.Equal("package", queriedNode.ClassType);
        Assert.NotNull(queriedNode.NestedChildrenOrder);
        Assert.Contains("TestModel", queriedNode.NestedChildrenOrder);
    }

    [Fact]
    public void EmptyGraph_AllOperations_HandleGracefully()
    {
        // Arrange
        var graph = new DirectedGraph();

        // Act & Assert - Should not throw
        Assert.Empty(graph.FileNodes);
        Assert.Empty(graph.ModelNodes);
        Assert.Equal(0, graph.NodeCount);
        Assert.Null(graph.GetNode("anything"));
        Assert.Empty(graph.GetOutgoingNodes("anything"));
        Assert.Empty(graph.GetIncomingNodes("anything"));
        Assert.False(graph.RemoveNode("anything"));
        Assert.False(graph.RemoveEdge("anything", "anything"));

        graph.Clear(); // Should not throw on empty graph
    }
}

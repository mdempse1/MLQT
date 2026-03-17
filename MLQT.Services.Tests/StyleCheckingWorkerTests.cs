using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the StyleCheckingWorker class.
/// </summary>
public class StyleCheckingWorkerTests
{
    private static DirectedGraph CreateGraphWithModel(string modelId, string modelCode)
    {
        var graph = new DirectedGraph();
        var modelIds = GraphBuilder.LoadModelicaFile(graph, $"{modelId}.mo", modelCode);
        return graph;
    }

    [Fact]
    public void QueuedCount_InitiallyZero()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings();
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        Assert.Equal(0, worker.QueuedCount);
    }

    [Fact]
    public void AddToQueue_IncreasesQueuedCount()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings();
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        worker.AddToQueue("model1");
        worker.AddToQueue("model2");

        Assert.Equal(2, worker.QueuedCount);
    }

    [Fact]
    public void CancelProcessing_DoesNotThrow()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings();
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        worker.CancelProcessing();
    }

    [Fact]
    public async Task StartProcessing_EmptyQueue_FiresWorkCompletedEvent()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings();
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");
        var completedRepoName = "";
        var tcs = new TaskCompletionSource<bool>();

        worker.OnWorkCompleted += (sender, repoName) =>
        {
            completedRepoName = repoName;
            tcs.TrySetResult(true);
        };

        worker.StartProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Equal("TestRepo", completedRepoName);
    }

    [Fact]
    public async Task StartProcessing_WithModels_ProcessesAndFiresEvents()
    {
        var modelCode = "model TestModel Real x; end TestModel;";
        var graph = CreateGraphWithModel("TestModel", modelCode);
        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        var violationsFound = new List<LogMessage>();
        var progressChangedCount = 0;
        var tcs = new TaskCompletionSource<bool>();

        worker.OnViolationFound += (sender, violations) => violationsFound.AddRange(violations);
        worker.OnProgressChanged += () => progressChangedCount++;
        worker.OnWorkCompleted += (sender, repoName) => tcs.TrySetResult(true);

        worker.AddToQueue("TestModel");
        worker.StartProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(5000));

        Assert.True(tcs.Task.IsCompleted);
        Assert.NotEmpty(violationsFound); // Should find missing description
        Assert.True(progressChangedCount > 0);
    }

    [Fact]
    public async Task StartProcessing_SkipsAlreadyCheckedModels()
    {
        var modelCode = "model TestModel Real x; end TestModel;";
        var graph = CreateGraphWithModel("TestModel", modelCode);

        // Mark model as already checked
        var modelNode = graph.GetNode<ModelNode>("TestModel");
        modelNode!.Definition.StyleRulesChecked = true;

        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        var violationsFound = new List<LogMessage>();
        var tcs = new TaskCompletionSource<bool>();

        worker.OnViolationFound += (sender, violations) => violationsFound.AddRange(violations);
        worker.OnWorkCompleted += (sender, repoName) => tcs.TrySetResult(true);

        worker.AddToQueue("TestModel");
        worker.StartProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(5000));

        Assert.Empty(violationsFound); // Should skip since already checked
    }

    [Fact]
    public async Task StartProcessing_NonExistentModel_DoesNotThrow()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings();
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");
        var tcs = new TaskCompletionSource<bool>();

        worker.OnWorkCompleted += (sender, repoName) => tcs.TrySetResult(true);

        worker.AddToQueue("NonExistentModel");
        worker.StartProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.True(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task StartProcessing_WithNoViolations_CompletesSuccessfully()
    {
        var modelCode = "model CleanModel \"A well described model.\" Real x; end CleanModel;";
        var graph = CreateGraphWithModel("CleanModel", modelCode);
        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        var violationsFound = new List<LogMessage>();
        var tcs = new TaskCompletionSource<bool>();

        worker.OnViolationFound += (sender, violations) => violationsFound.AddRange(violations);
        worker.OnWorkCompleted += (sender, repoName) => tcs.TrySetResult(true);

        worker.AddToQueue("CleanModel");
        worker.StartProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.True(tcs.Task.IsCompleted);
    }

    [Fact]
    public async Task CancelProcessing_StopsProcessing()
    {
        var graph = new DirectedGraph();
        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        var worker = new StyleCheckingWorker(graph, settings, "TestRepo");

        // Add many models
        for (int i = 0; i < 100; i++)
        {
            worker.AddToQueue($"model{i}");
        }

        var tcs = new TaskCompletionSource<bool>();
        worker.OnWorkCompleted += (sender, repoName) => tcs.TrySetResult(true);

        worker.StartProcessing();
        worker.CancelProcessing();

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        // After cancel, it should complete quickly (processing stops)
        Assert.True(tcs.Task.IsCompleted);
    }
}

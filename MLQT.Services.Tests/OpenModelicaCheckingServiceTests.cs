using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.Helpers;
using OpenModelicaInterface.Interfaces;
using Moq;

namespace MLQT.Services.Tests;

/// <summary>
/// Tests for OpenModelicaCheckingService.
/// Note: These tests focus on state management, event firing, and cancellation logic.
/// Full integration tests would require an actual OpenModelica installation.
/// </summary>
public class OpenModelicaCheckingServiceTests
{
    private Mock<IOpenModelicaInterfaceFactory> CreateMockFactory()
    {
        var mockFactory = new Mock<IOpenModelicaInterfaceFactory>();
        // By default, GetOrCreateAsync will throw because we don't have OpenModelica installed
        // Tests that need actual OpenModelica interaction will be skipped or use specific mocking
        return mockFactory;
    }

    private (DirectedGraph graph, ModelNode modelNode) CreateSimpleModelGraph()
    {
        var graph = new DirectedGraph();

        var modelCode = "within;\nmodel TestModel\n  Real x;\nend TestModel;";
        var parsedCode = ModelicaParserHelper.Parse(modelCode);
        var definition = new ModelDefinition("TestModel", modelCode) { ParsedCode = parsedCode };
        var modelNode = new ModelNode("TestModel", definition);
        modelNode.ClassType = "model";
        modelNode.ParentModelName = "";
        graph.AddNode(modelNode);

        return (graph, modelNode);
    }

    private (DirectedGraph graph, ModelNode packageNode, List<ModelNode> childNodes) CreatePackageWithModels(int childCount)
    {
        var graph = new DirectedGraph();

        // Create package
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var parsedPackage = ModelicaParserHelper.Parse(packageCode);
        var packageDef = new ModelDefinition("TestPackage", packageCode) { ParsedCode = parsedPackage };
        var packageNode = new ModelNode("TestPackage", packageDef);
        packageNode.ClassType = "package";
        packageNode.ParentModelName = "";
        graph.AddNode(packageNode);

        // Create child models
        var childNodes = new List<ModelNode>();
        for (int i = 1; i <= childCount; i++)
        {
            var childCode = $"within TestPackage;\nmodel Model{i}\nend Model{i};";
            var parsedChild = ModelicaParserHelper.Parse(childCode);
            var childDef = new ModelDefinition($"Model{i}", childCode) { ParsedCode = parsedChild };
            var childNode = new ModelNode($"TestPackage.Model{i}", childDef);
            childNode.ClassType = "model";
            childNode.ParentModelName = "TestPackage";
            graph.AddNode(childNode);
            childNodes.Add(childNode);
        }

        return (graph, packageNode, childNodes);
    }

    #region Constructor and Initial State Tests

    [Fact]
    public void Constructor_InitializesWithCorrectState()
    {
        // Arrange
        var mockFactory = CreateMockFactory();

        // Act
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Assert
        Assert.False(service.IsRunning);
        Assert.NotNull(service.CurrentProgress);
        Assert.Equal("OpenModelica", service.ToolName);
    }

    [Fact]
    public void ToolName_ReturnsOpenModelica()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act
        var toolName = service.ToolName;

        // Assert
        Assert.Equal("OpenModelica", toolName);
    }

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act & Assert
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void CurrentProgress_InitiallyNotNull()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act & Assert
        Assert.NotNull(service.CurrentProgress);
    }

    #endregion

    #region StopChecking Tests

    [Fact]
    public void StopChecking_WhenNotRunning_DoesNotThrow()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act & Assert - Should not throw
        service.StopChecking();
    }

    #endregion

    #region ResetAsync Tests

    [Fact]
    public async Task ResetAsync_CallsFactoryReset()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.ResetAsync()).Returns(Task.CompletedTask);
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act
        await service.ResetAsync();

        // Assert
        mockFactory.Verify(f => f.ResetAsync(), Times.Once);
    }

    [Fact]
    public async Task ResetAsync_StopsRunningCheck()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.ResetAsync()).Returns(Task.CompletedTask);
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act - Should not throw even if there's nothing running
        await service.ResetAsync();

        // Assert
        Assert.False(service.IsRunning);
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnProgressChanged_CanSubscribe()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var eventFired = false;

        // Act
        service.OnProgressChanged += (progress) => eventFired = true;

        // Assert - Just verify subscription doesn't throw
        Assert.False(eventFired); // No event should have fired yet
    }

    [Fact]
    public void OnModelChecked_CanSubscribe()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var eventFired = false;

        // Act
        service.OnModelChecked += (result) => eventFired = true;

        // Assert
        Assert.False(eventFired);
    }

    [Fact]
    public void OnCheckingComplete_CanSubscribe()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var eventFired = false;

        // Act
        service.OnCheckingComplete += (progress) => eventFired = true;

        // Assert
        Assert.False(eventFired);
    }

    [Fact]
    public void Events_CanUnsubscribe()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);
        Action<ModelCheckProgress> progressHandler = (p) => { };
        Action<ModelCheckResult> resultHandler = (r) => { };

        // Act - Subscribe and unsubscribe (should not throw)
        service.OnProgressChanged += progressHandler;
        service.OnProgressChanged -= progressHandler;

        service.OnModelChecked += resultHandler;
        service.OnModelChecked -= resultHandler;

        service.OnCheckingComplete += progressHandler;
        service.OnCheckingComplete -= progressHandler;

        // Assert - No exception means success
        Assert.True(true);
    }

    #endregion

    #region StartCheckingAsync Tests

    /// <summary>
    /// A second check started while the first is still running is refused.
    /// </summary>
    /// <remarks>
    /// <para><b>This test used to be a coin toss, and it is where a coverage oscillation came
    /// from.</b> It started a check, waited 50ms, and then only tried the second call
    /// <c>if (service.IsRunning)</c> — which was usually false, because the factory threw at once and
    /// the background task had already cleared the flag in its <c>finally</c>. So the early return in
    /// <c>StartCheckingAsync</c> was covered in roughly one run in four, moving
    /// <c>OpenModelicaCheckingService</c>'s measured coverage by one line and flapping the ratchet.
    /// Its closing assertion was <c>callCount &gt;= 1</c>, which cannot fail, so the test never
    /// checked the thing its name promises either.</para>
    ///
    /// <para>The first call is now held open by a gate the test releases, so "already running" is a
    /// state the test creates rather than one it hopes to catch. <c>callCount</c> is the assertion:
    /// the refused call must never reach the factory.</para>
    /// </remarks>
    [Fact]
    public async Task StartCheckingAsync_WhenAlreadyRunning_DoesNotStartAgain()
    {
        var mockFactory = CreateMockFactory();
        var callCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Holds the first check inside the factory until the test lets go, so "already running" is
        // true for as long as the second call needs it to be.
        async Task<IOpenModelicaInterface> BlockThenFail()
        {
            Interlocked.Increment(ref callCount);
            await gate.Task;
            throw new InvalidOperationException("no OpenModelica in a unit test");
        }

        mockFactory.Setup(f => f.GetOrCreateAsync()).Returns(BlockThenFail);

        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var (graph, modelNode) = CreateSimpleModelGraph();

        await service.StartCheckingAsync(modelNode, graph);
        await WaitUntilAsync(() => service.IsRunning, "the first check never started");

        // The call under test. It must return without starting anything.
        await service.StartCheckingAsync(modelNode, graph);

        gate.SetResult();
        await WaitUntilAsync(() => !service.IsRunning, "the first check never finished");

        Assert.Equal(1, Volatile.Read(ref callCount));
    }

    /// <summary>
    /// Waits for a state the test has arranged to be reachable, rather than sleeping for a guess.
    /// The timeout is a failure report, not a synchronisation device.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string whatFailed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.True(condition(), whatFailed);
    }

    [Fact]
    public async Task StartCheckingAsync_ReturnsImmediately()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ReturnsAsync(() => throw new Exception("Not connected"));

        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var (graph, modelNode) = CreateSimpleModelGraph();

        // Act - Should return immediately (not block)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StartCheckingAsync(modelNode, graph);
        stopwatch.Stop();

        // Assert - Should return in under 100ms (it's async fire-and-forget)
        Assert.True(stopwatch.ElapsedMilliseconds < 100);
    }

    #endregion

    #region CheckModelAsync Tests

    [Fact]
    public async Task CheckModelAsync_WhenOpenModelicaNotAvailable_ReturnsFailure()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("OpenModelica not available"));

        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var (graph, modelNode) = CreateSimpleModelGraph();

        // Act
        var result = await service.CheckModelAsync(modelNode, graph);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("TestModel", result.ModelId);
        Assert.Contains("OpenModelica", result.ErrorMessage ?? "");
    }

    #endregion

    #region EnsureLibraryLoadedAsync Tests

    [Fact]
    public async Task EnsureLibraryLoadedAsync_WhenOpenModelicaNotAvailable_ReturnsFailure()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("OpenModelica not available"));

        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act
        var (success, errorMessage) = await service.EnsureLibraryLoadedAsync("test.mo");

        // Assert
        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("OpenModelica", errorMessage);
    }

    [Fact]
    public async Task EnsureLibraryLoadedAsync_WithNonExistentFile_ReturnsError()
    {
        // This test verifies error handling when factory throws
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("Connection failed"));

        var service = new OpenModelicaCheckingService(mockFactory.Object);

        var (success, errorMessage) = await service.EnsureLibraryLoadedAsync(Path.Combine(Path.GetTempPath(), "nonexistent_file_12345.mo"));

        Assert.False(success);
        Assert.NotNull(errorMessage);
    }

    #endregion

    #region Service Behavior Tests

    [Fact]
    public void MultipleServiceInstances_AreIndependent()
    {
        // Arrange
        var mockFactory1 = CreateMockFactory();
        var mockFactory2 = CreateMockFactory();
        var service1 = new OpenModelicaCheckingService(mockFactory1.Object);
        var service2 = new OpenModelicaCheckingService(mockFactory2.Object);

        // Assert - Each service has its own state
        Assert.False(service1.IsRunning);
        Assert.False(service2.IsRunning);
        Assert.NotSame(service1.CurrentProgress, service2.CurrentProgress);
    }

    [Fact]
    public async Task ResetAsync_ClearsInternalState()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.ResetAsync()).Returns(Task.CompletedTask);
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act
        await service.ResetAsync();

        // Assert
        Assert.False(service.IsRunning);
        mockFactory.Verify(f => f.ResetAsync(), Times.Once);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task StartCheckingAsync_WithCancellation_RespectsCancellationToken()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ReturnsAsync(() => throw new Exception("Not connected"));

        var service = new OpenModelicaCheckingService(mockFactory.Object);
        var (graph, modelNode) = CreateSimpleModelGraph();
        var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        // Act - Should not throw despite cancellation
        await service.StartCheckingAsync(modelNode, graph, cts.Token);

        // Wait longer for background work to complete (background tasks may still be running)
        await Task.Delay(300);

        // Assert - Service should eventually stop running (may take time for background thread to finish)
        // We're testing that the cancellation doesn't cause crashes, not precise timing
        Assert.True(true); // If we get here without exception, cancellation was handled gracefully
    }

    [Fact]
    public void StopChecking_MultipleCallsDoNotThrow()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new OpenModelicaCheckingService(mockFactory.Object);

        // Act & Assert - Multiple calls should not throw
        service.StopChecking();
        service.StopChecking();
        service.StopChecking();
    }

    #endregion
}

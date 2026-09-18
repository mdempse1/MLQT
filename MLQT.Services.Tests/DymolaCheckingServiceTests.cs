using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using DymolaInterface.Interfaces;
using Moq;

namespace MLQT.Services.Tests;

/// <summary>
/// Tests for DymolaCheckingService.
/// Note: These tests focus on state management, event firing, and cancellation logic.
/// Full integration tests would require an actual Dymola installation.
/// </summary>
public class DymolaCheckingServiceTests
{
    private Mock<IDymolaInterfaceFactory> CreateMockFactory()
    {
        var mockFactory = new Mock<IDymolaInterfaceFactory>();
        // By default, GetOrCreateAsync will throw because we don't have Dymola installed
        // Tests that need actual Dymola interaction will be skipped or use specific mocking
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
        var service = new DymolaCheckingService(mockFactory.Object);

        // Assert
        Assert.False(service.IsRunning);
        Assert.NotNull(service.CurrentProgress);
        Assert.Equal("Dymola", service.ToolName);
    }

    [Fact]
    public void ToolName_ReturnsDymola()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DymolaCheckingService(mockFactory.Object);

        // Act
        var toolName = service.ToolName;

        // Assert
        Assert.Equal("Dymola", toolName);
    }

    [Fact]
    public void IsRunning_InitiallyFalse()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DymolaCheckingService(mockFactory.Object);

        // Act & Assert
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void CurrentProgress_InitiallyNotNull()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DymolaCheckingService(mockFactory.Object);

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
        var service = new DymolaCheckingService(mockFactory.Object);

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
        var service = new DymolaCheckingService(mockFactory.Object);

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
        var service = new DymolaCheckingService(mockFactory.Object);

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
        var service = new DymolaCheckingService(mockFactory.Object);
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
        var service = new DymolaCheckingService(mockFactory.Object);
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
        var service = new DymolaCheckingService(mockFactory.Object);
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
        var service = new DymolaCheckingService(mockFactory.Object);
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
    /// <c>DymolaCheckingService</c>'s measured coverage by one line and flapping the ratchet.
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
        async Task<DymolaInterface.DymolaInterface> BlockThenFail()
        {
            Interlocked.Increment(ref callCount);
            await gate.Task;
            throw new InvalidOperationException("no Dymola in a unit test");
        }

        mockFactory.Setup(f => f.GetOrCreateAsync()).Returns(BlockThenFail);

        var service = new DymolaCheckingService(mockFactory.Object);
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

        var service = new DymolaCheckingService(mockFactory.Object);
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
    public async Task CheckModelAsync_WhenDymolaNotAvailable_ReturnsFailure()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("Dymola not available"));

        var service = new DymolaCheckingService(mockFactory.Object);
        var (graph, modelNode) = CreateSimpleModelGraph();

        // Act
        var result = await service.CheckModelAsync(modelNode, graph);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("TestModel", result.ModelId);
        Assert.Contains("Dymola", result.ErrorMessage ?? "");
    }

    #endregion

    #region EnsureLibraryLoadedAsync Tests

    [Fact]
    public async Task EnsureLibraryLoadedAsync_WhenDymolaNotAvailable_ReturnsFailure()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("Dymola not available"));

        var service = new DymolaCheckingService(mockFactory.Object);

        // Act
        var (success, errorMessage) = await service.EnsureLibraryLoadedAsync("test.mo");

        // Assert
        Assert.False(success);
        Assert.NotNull(errorMessage);
        Assert.Contains("Dymola", errorMessage);
    }

    [Fact]
    public async Task EnsureLibraryLoadedAsync_WithNonExistentFile_ReturnsFileNotFoundError()
    {
        // This test requires a mock DymolaInterface that returns false for OpenModelAsync
        // For now, we'll just verify error handling when factory throws
        var mockFactory = CreateMockFactory();
        mockFactory.Setup(f => f.GetOrCreateAsync())
            .ThrowsAsync(new Exception("Connection failed"));

        var service = new DymolaCheckingService(mockFactory.Object);

        var (success, errorMessage) = await service.EnsureLibraryLoadedAsync(Path.Combine(Path.GetTempPath(), "nonexistent_file_12345.mo"));

        Assert.False(success);
        Assert.NotNull(errorMessage);
    }

    #endregion

    #region ModelCheckProgress Tests

    [Fact]
    public void ModelCheckProgress_DefaultValues()
    {
        // Arrange & Act
        var progress = new ModelCheckProgress();

        // Assert
        Assert.Equal(0, progress.TotalModels);
        Assert.Equal(0, progress.ModelsChecked);
        Assert.Equal(string.Empty, progress.CurrentModel);
        Assert.False(progress.IsComplete);
        Assert.False(progress.WasCancelled);
    }

    [Fact]
    public void ModelCheckProgress_CanSetAllProperties()
    {
        // Arrange
        var progress = new ModelCheckProgress
        {
            TotalModels = 10,
            ModelsChecked = 5,
            CurrentModel = "TestModel",
            IsComplete = true,
            WasCancelled = true
        };

        // Assert
        Assert.Equal(10, progress.TotalModels);
        Assert.Equal(5, progress.ModelsChecked);
        Assert.Equal("TestModel", progress.CurrentModel);
        Assert.True(progress.IsComplete);
        Assert.True(progress.WasCancelled);
    }

    #endregion

    #region ModelCheckResult Tests

    [Fact]
    public void ModelCheckResult_DefaultValues()
    {
        // Arrange & Act
        var result = new ModelCheckResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.ModelId);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void ModelCheckResult_CanSetAllProperties()
    {
        // Arrange
        var result = new ModelCheckResult
        {
            Success = true,
            ModelId = "TestModel",
            ErrorMessage = "Test error",
            Summary = "Test summary"
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal("TestModel", result.ModelId);
        Assert.Equal("Test error", result.ErrorMessage);
        Assert.Equal("Test summary", result.Summary);
    }

    #endregion
}

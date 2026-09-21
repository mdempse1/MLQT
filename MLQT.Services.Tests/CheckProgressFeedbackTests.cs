using DymolaInterface.Interfaces;
using MLQT.Services;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using MLQT.Services.Interfaces;
using ModelicaGraph.DataTypes;
using OpenModelicaInterface.Interfaces;
using Moq;

namespace MLQT.Services.Tests;

/// <summary>
/// B259 — something on screen from the moment the button is pressed.
///
/// <para>A check does not begin with the first class: the tool has to be started and the library
/// opened, which on a large library is most of the wait. Both counts are zero throughout it, so the
/// progress dialog had nothing to show and was not opened for a single class at all — and for
/// OpenModelica, which has no window of its own to appear, the only evidence a check was running
/// was that the button had been pressed.</para>
///
/// <para>What these assert is the <b>order</b>: the first progress event reaches the UI before the
/// slow part, not after it. A test that only looked at the events afterwards would pass against the
/// behaviour that was reported as broken.</para>
/// </summary>
public class CheckProgressFeedbackTests
{
    /// <summary>A class in a file, so there is a library to open.</summary>
    private static (DirectedGraph Graph, ModelNode Model) Library()
    {
        var data = new LibraryDataService();
        data.AddLibraryFromFileAsync(Path.Combine("Lib", "package.mo"),
                "package Lib \"l\"\n  model M \"m\"\n  end M;\nend Lib;")
            .GetAwaiter().GetResult();

        var graph = data.CombinedGraph;
        return (graph, graph.GetNode<ModelNode>("Lib.M")!);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string whatFailed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.True(condition(), whatFailed);
    }

    /// <summary>
    /// The reported scenario: the tool is slow to start, and the user is looking at the screen
    /// while it is. The factory is held open, so "still starting" is a state the test creates
    /// rather than a moment it hopes to catch.
    /// </summary>
    [Fact]
    public async Task OpenModelica_SaysItIsStarting_BeforeTheToolHasStarted()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Mock<IOpenModelicaInterfaceFactory>();
        factory.Setup(f => f.GetOrCreateAsync()).Returns(async () =>
        {
            await gate.Task;
            throw new InvalidOperationException("no OpenModelica in a unit test");
        });

        var service = new OpenModelicaCheckingService(factory.Object);
        var reported = new List<ModelCheckProgress>();
        service.OnProgressChanged += p => { lock (reported) reported.Add(p); };

        var (graph, model) = Library();
        await service.StartCheckingAsync(model, graph);

        // While the tool is still starting - the gate has not been released.
        await WaitUntilAsync(() => { lock (reported) return reported.Count > 0; },
            "nothing was reported while the tool was starting");

        ModelCheckProgress first;
        lock (reported) first = reported[0];

        Assert.Contains("OpenModelica", first.Status ?? "");
        Assert.Equal(0, first.TotalModels);   // there is nothing to count yet, which is the point

        gate.SetResult();
        await WaitUntilAsync(() => !service.IsRunning, "the check never finished");
    }

    [Fact]
    public async Task Dymola_SaysItIsStarting_BeforeTheToolHasStarted()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new Mock<IDymolaInterfaceFactory>();
        factory.Setup(f => f.GetOrCreateAsync()).Returns(async () =>
        {
            await gate.Task;
            throw new InvalidOperationException("no Dymola in a unit test");
        });

        var service = new DymolaCheckingService(factory.Object);
        var reported = new List<ModelCheckProgress>();
        service.OnProgressChanged += p => { lock (reported) reported.Add(p); };

        var (graph, model) = Library();
        await service.StartCheckingAsync(model, graph);

        await WaitUntilAsync(() => { lock (reported) return reported.Count > 0; },
            "nothing was reported while the tool was starting");

        ModelCheckProgress first;
        lock (reported) first = reported[0];

        Assert.Contains("Dymola", first.Status ?? "");
        Assert.Equal(0, first.TotalModels);

        gate.SetResult();
        await WaitUntilAsync(() => !service.IsRunning, "the check never finished");
    }

    /// <summary>
    /// Opening the library is the other half of the wait, and it is the half that takes minutes on a
    /// real library — so it says which file it is opening rather than only that something is
    /// happening.
    /// </summary>
    [Theory]
    [InlineData("OpenModelica")]
    [InlineData("Dymola")]
    public async Task TheLibraryBeingOpenedIsNamed(string tool)
    {
        var reported = new List<ModelCheckProgress>();
        var (graph, model) = Library();

        IModelCheckingService service = tool == "Dymola"
            ? new DymolaCheckingService(new Mock<IDymolaInterfaceFactory>().Object)
            : new OpenModelicaCheckingService(new Mock<IOpenModelicaInterfaceFactory>().Object);

        service.OnProgressChanged += p => { lock (reported) reported.Add(p); };

        await service.StartCheckingAsync(model, graph);
        await WaitUntilAsync(() => !service.IsRunning, "the check never finished");

        List<string> statuses;
        lock (reported)
            statuses = reported.Select(p => p.Status).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;

        var startingAt = statuses.FindIndex(s => s.Contains("Starting"));
        var openingAt = statuses.FindIndex(s => s.Contains("package.mo") && s.Contains(tool));

        Assert.True(openingAt >= 0, $"the library being opened was never named: {string.Join(" / ", statuses)}");

        // And in that order - starting the tool comes before opening the library in it, so a user
        // watching this is told what is happening as each wait begins rather than after it.
        Assert.True(startingAt >= 0 && openingAt > startingAt,
            $"expected the tool to be starting before the library is opened: {string.Join(" / ", statuses)}");
    }
}

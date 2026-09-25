using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// B335 — one external-tool check at a time on the Code Review page, and only its events heard.
/// </summary>
/// <remarks>
/// The page subscribed the same handlers to both tools with one result list and one token between
/// them. Clicking the same tool again while its run was starting reset the results and opened a
/// dialog the service ignored, which the old run then closed; clicking the other tool let the stopped
/// run's completion close the new tool's dialog and dispose its token, leaving that run unstoppable.
/// </remarks>
public class ExternalCheckSessionTests
{
    /// <summary>A tool whose events a test raises by hand, and which records being started and stopped.</summary>
    private sealed class FakeService(string name) : IModelCheckingService
    {
        public bool Running;
        public int Starts;
        public int Stops;
        public CancellationToken LastToken;

        public event Action<ModelCheckProgress>? OnProgressChanged;
        public event Action<ModelCheckResult>? OnModelChecked;
        public event Action<ModelCheckProgress>? OnCheckingComplete;

        public bool IsRunning => Running;
        public ModelCheckProgress CurrentProgress { get; } = new();
        public string ToolName => name;

        public Task StartCheckingAsync(ModelNode modelNode, DirectedGraph graph, CancellationToken cancellationToken = default)
        {
            Starts++;
            Running = true;
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }

        public void StopChecking() => Stops++;

        public void Report(ModelCheckResult result) => OnModelChecked?.Invoke(result);
        public void Progress(int total) => OnProgressChanged?.Invoke(new ModelCheckProgress { TotalModels = total });

        public void Finish(bool cancelled = false)
        {
            Running = false;
            OnCheckingComplete?.Invoke(new ModelCheckProgress { IsComplete = true, WasCancelled = cancelled });
        }

        public int Subscribers =>
            (OnProgressChanged?.GetInvocationList().Length ?? 0)
            + (OnModelChecked?.GetInvocationList().Length ?? 0)
            + (OnCheckingComplete?.GetInvocationList().Length ?? 0);

        public Task<ModelCheckResult> CheckModelAsync(ModelNode modelNode, DirectedGraph graph) =>
            throw new NotSupportedException();
        public Task<(bool Success, string? ErrorMessage)> EnsureLibraryLoadedAsync(string filePath) =>
            throw new NotSupportedException();
        public Task ResetAsync() => Task.CompletedTask;
    }

    private static (DirectedGraph Graph, ModelNode Model) Model()
    {
        var graph = new DirectedGraph();
        var model = new ModelNode("Lib.M", "M", "model M end M;");
        graph.AddNode(model);
        return (graph, model);
    }

    private static ModelCheckResult Result(string id, bool success = true) => new() { ModelId = id, Success = success };

    [Fact]
    public void ACheckIsNotStartedWhileTheOtherToolIsStillRunning()
    {
        var dymola = new FakeService("Dymola");
        var omc = new FakeService("OpenModelica");
        using var session = new ExternalCheckSession(dymola, omc);
        var (graph, model) = Model();

        Assert.True(session.TryStart(dymola, model, graph));
        session.Stop();   // stopped, but still starting the tool: it has not ended yet

        Assert.True(session.IsAnyRunning);
        Assert.False(session.TryStart(omc, model, graph));
        Assert.Equal(0, omc.Starts);
        Assert.Same(dymola, session.Current);
    }

    [Fact]
    public void ACheckIsNotStartedAgainWhileTheSameToolIsStillRunning()
    {
        // The other half of the report: a second click reset the results and opened a dialog for a
        // run the service was going to refuse.
        var dymola = new FakeService("Dymola");
        using var session = new ExternalCheckSession(dymola, new FakeService("OpenModelica"));
        var (graph, model) = Model();
        session.TryStart(dymola, model, graph);
        dymola.Report(Result("Lib.A"));

        Assert.False(session.TryStart(dymola, model, graph));

        Assert.Equal(1, dymola.Starts);
        Assert.Single(session.Results);
    }

    [Fact]
    public void EventsFromAToolThatIsNotCurrentAreNotPassedOn()
    {
        var dymola = new FakeService("Dymola");
        var omc = new FakeService("OpenModelica");
        using var session = new ExternalCheckSession(dymola, omc);
        var (graph, model) = Model();
        session.TryStart(omc, model, graph);

        var progress = 0;
        var checkedCount = 0;
        var completed = 0;
        session.ProgressChanged += _ => progress++;
        session.ModelChecked += _ => checkedCount++;
        session.Completed += _ => completed++;

        // A stale run of the other tool, finishing late.
        dymola.Progress(3);
        dymola.Report(Result("Lib.FromDymola", success: false));
        dymola.Finish(cancelled: true);

        Assert.Equal((0, 0, 0), (progress, checkedCount, completed));
        Assert.Empty(session.Results);
        Assert.False(session.WasCancelled);
    }

    [Fact]
    public void TheCurrentRunsEventsArePassedOnAndItsOutcomeKept()
    {
        var omc = new FakeService("OpenModelica");
        using var session = new ExternalCheckSession(new FakeService("Dymola"), omc);
        var (graph, model) = Model();
        session.TryStart(omc, model, graph);

        ModelCheckProgress? completed = null;
        session.Completed += p => completed = p;

        omc.Report(Result("Lib.A"));
        omc.Report(Result("Lib.B", success: false));
        omc.Finish(cancelled: true);

        Assert.Equal(["Lib.A", "Lib.B"], session.Results.Select(r => r.ModelId));
        Assert.True(session.WasCancelled);
        Assert.NotNull(completed);
        Assert.Equal("OpenModelica", session.ToolName);
        Assert.False(session.IsAnyRunning);
    }

    [Fact]
    public void ANewRunStartsWithNothingFromTheLastOne()
    {
        var dymola = new FakeService("Dymola");
        var omc = new FakeService("OpenModelica");
        using var session = new ExternalCheckSession(dymola, omc);
        var (graph, model) = Model();
        session.TryStart(dymola, model, graph);
        dymola.Report(Result("Lib.A"));
        dymola.Finish(cancelled: true);

        Assert.True(session.TryStart(omc, model, graph));

        Assert.Empty(session.Results);
        Assert.False(session.WasCancelled);
        Assert.Same(omc, session.Current);
    }

    [Fact]
    public void StopReachesTheCurrentRunThroughBothItsTokenAndItsService()
    {
        var dymola = new FakeService("Dymola");
        using var session = new ExternalCheckSession(dymola, new FakeService("OpenModelica"));
        var (graph, model) = Model();
        session.TryStart(dymola, model, graph);

        session.Stop();

        Assert.True(dymola.LastToken.IsCancellationRequested);
        Assert.Equal(1, dymola.Stops);
    }

    [Fact]
    public void DisposingUnsubscribesFromBothTools()
    {
        var dymola = new FakeService("Dymola");
        var omc = new FakeService("OpenModelica");
        var session = new ExternalCheckSession(dymola, omc);
        Assert.Equal(3, dymola.Subscribers);

        session.Dispose();

        Assert.Equal(0, dymola.Subscribers);
        Assert.Equal(0, omc.Subscribers);
    }
}

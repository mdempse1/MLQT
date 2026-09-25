using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B299 — the Metrics tab does its work on its own dispatcher, not on whichever thread announced a
/// change.
/// </summary>
/// <remarks>
/// <para>Three of its handlers did their work where they were raised: <c>OnLibrariesChanged</c>
/// cleared the figures and read every history file there, and the two analysis-progress handlers
/// recounted findings there. A library load raises the first on a pool thread, a checker the other
/// two on its worker: the fields they changed were the ones a render reads, and the work was charged
/// to a thread that had something else to do - on the desktop host possibly the window's.</para>
///
/// <para><b>Two different fixes, so two different tests.</b> The history read is file I/O, so it
/// moved to the pool: raising the event no longer waits for it, which is what the first test times.
/// The recount takes milliseconds, and what was wrong with it was the thread, not the cost: it
/// changed fields a render reads. It now runs through <c>InvokeAsync</c>, and the other two tests
/// check it happens on the dispatcher. Timing those would prove nothing: <c>InvokeAsync</c> runs
/// the work inline for a caller when the dispatcher is idle, and on the desktop host it is a
/// synchronous <c>SendMessage</c> - either way whoever raised the event still waits for the
/// work, which is only acceptable because the work is small.</para>
/// </remarks>
public class MetricsDashboardEventThreadTests : MlqtComponentTestBase
{
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(2);

    private readonly Mock<ILibraryDataService> _libraries = new();
    private readonly Mock<IRepositoryService> _repositories = new();
    private readonly Mock<ICodeReviewService> _findings = new();
    private readonly Mock<IStyleCheckingService> _checker = new();

    // Set once the tab has rendered, so only what an event triggers is measured.
    private volatile bool _watching;
    private int _historyReads;
    private int _recountsOnTheDispatcher;
    private int _recountsElsewhere;

    public MetricsDashboardEventThreadTests()
    {
        _libraries.Setup(l => l.GetAllModels()).Returns([]);
        _libraries.SetupGet(l => l.Libraries).Returns([]);
        _libraries.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());

        // Read by the history load, and nowhere else an event reaches.
        _repositories.SetupGet(r => r.Repositories).Returns(() =>
        {
            if (_watching)
            {
                Interlocked.Increment(ref _historyReads);
                Thread.Sleep(Slow);
            }
            return [];
        });

        // Read by the findings recount, and asked which thread it is on.
        _findings.SetupGet(f => f.LogMessages).Returns(() =>
        {
            if (_watching)
            {
                if (Renderer.Dispatcher.CheckAccess())
                    Interlocked.Increment(ref _recountsOnTheDispatcher);
                else
                    Interlocked.Increment(ref _recountsElsewhere);
            }
            return new List<LogMessage>();
        });

        Services.AddSingleton(_libraries.Object);
        Services.AddSingleton(_repositories.Object);
        Services.AddSingleton(_findings.Object);
        Services.AddSingleton(_checker.Object);
    }

    private IRenderedComponent<MetricsDashboard> RenderTab()
    {
        // Findings are only counted once the analysis has run; before that the count is unknown.
        NavState.DependencyAnalysisCompleted();
        NavState.StyleCheckingCompleted();

        RenderProviders();
        var tab = Render<MetricsDashboard>();
        _watching = true;
        return tab;
    }

    /// <summary>Raises an event from a thread of its own, as a load or a checker does, and times it.</summary>
    private static TimeSpan RaiseFromAnotherThread(Action raise)
    {
        var elapsed = TimeSpan.Zero;
        var thread = new Thread(() =>
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            raise();
            elapsed = timer.Elapsed;
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "raising the event never returned");
        return elapsed;
    }

    [Fact]
    public void ALibraryChange_ReadsTheHistoryWithoutHoldingWhoeverAnnouncedIt()
    {
        var tab = RenderTab();

        var elapsed = RaiseFromAnotherThread(() => _libraries.Raise(l => l.OnLibrariesChanged += null));

        tab.WaitForAssertion(() => Assert.True(Volatile.Read(ref _historyReads) > 0, "the history was never reloaded"),
            TimeSpan.FromSeconds(10));
        Assert.True(elapsed < Slow,
            $"announcing a library change took {elapsed.TotalMilliseconds:0}ms: the Metrics tab read its history "
            + "on the thread that loaded the library");
    }

    [Fact]
    public void AnalysisProgress_IsRecountedOnTheDispatcher()
    {
        var tab = RenderTab();

        RaiseFromAnotherThread(() => _checker.Raise(c => c.OnProgressChanged += null, true));

        AssertRecountedOnTheDispatcher(tab, "a checker's progress");
    }

    [Fact]
    public void ADeferredStepCompleting_IsRecountedOnTheDispatcher()
    {
        var tab = RenderTab();

        RaiseFromAnotherThread(() => NavState.StyleCheckingCompleted());

        AssertRecountedOnTheDispatcher(tab, "a completed analysis step");
    }

    private void AssertRecountedOnTheDispatcher(IRenderedComponent<MetricsDashboard> tab, string what)
    {
        tab.WaitForAssertion(
            () => Assert.True(Volatile.Read(ref _recountsOnTheDispatcher) + Volatile.Read(ref _recountsElsewhere) > 0,
                $"{what} never recounted the findings"),
            TimeSpan.FromSeconds(10));
        Assert.True(Volatile.Read(ref _recountsElsewhere) == 0,
            $"{what} recounted the findings on the thread that announced it, changing fields a render reads");
    }
}

using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// What <see cref="DymolaCheckingService"/> and <see cref="OpenModelicaCheckingService"/> both
/// promise, asserted once and run against each of them.
/// </summary>
/// <remarks>
/// <para><b>Why a contract rather than two test classes (B229).</b> The two services are the same
/// 420 lines twice over: the same load-then-drain-then-check sequence, the same four result shapes,
/// the same package fan-out, the same two nested catches. This repository's recorded failure mode
/// for exactly that arrangement is a fix applied to one tool and not to its twin — B165/B166 were
/// that, and B170 was a load step that was right for Dymola and wrong for OpenModelica for a whole
/// release. A behaviour written here cannot be true of one tool and untested on the other.</para>
///
/// <para><b>Why a fake session rather than a mock.</b> Most of these promises are about a
/// <i>buffer</i> — Dymola's log accumulates until it is cleared, omc's error string empties itself
/// when it is read — and the interesting failures are about which check's output a result ends up
/// carrying. A mock returning a fixed string cannot tell a drained buffer from an undrained one, so
/// <see cref="FakeTool"/> models the buffer and the tests seed it with what a previous command left
/// behind.</para>
///
/// <para><b>Why this is possible at all.</b> Both factories used to hand back the concrete session
/// class, every method of which ends in a round trip to a running tool, so nothing past the first
/// call could be reached without the tool installed — and no automated run has one. Extracting
/// <c>IDymolaInterface</c> and <c>IOpenModelicaInterface</c> is what these tests are built on.</para>
///
/// <para><b>What still survives mutation here, and why it was left.</b> Measured across both
/// services: 11% of covered mutants killed became 73.6% and 73.5%, and 176 mutants covered by no
/// test at all became 13. Of the 52 left, 36 are the text and the presence of a log call — twelve
/// <c>Error</c>/<c>Warn</c>/<c>Debug</c> sites × three mutants each — which nothing asserts and
/// nothing should. The other sixteen are read and recorded rather than scored:</para>
/// <list type="bullet">
/// <item>the two <c>FireThrottledProgressUpdate()</c> calls per loop, one per service. Removing
/// <i>either</i> leaves the other firing, and the count that would separate them depends on how
/// long a class takes to check. <see cref="ARunLongerThanTheThrottleReportsProgressWhileItIsGoing"/>
/// holds the property that matters — that a long run reports while it runs — without asserting
/// a number that a loaded machine could change;</item>
/// <item>the throttle comparison itself (<c>&gt;=</c> to <c>&gt;</c>, and negated). Separating
/// these needs sub-500ms timing precision from a test, which is how flaky tests are written;</item>
/// <item><c>_cancellationTokenSource?.Dispose()</c>, which has no effect a caller can observe;</item>
/// <item>the two null checks on the session. <c>_dymola is null ? null : …</c> is only reached
/// where the session was just dereferenced, and <c>_dymola != null ? … : ex.Message</c> forced
/// true dereferences null, throws, and is caught by the inner <c>catch</c> that produces the same
/// message. Both are equivalent mutants.</item>
/// </list>
/// </remarks>
public abstract class ModelCheckingServiceContract
{
    protected abstract ToolHarness NewHarness();

    // ── the single-model path: CheckModelAsync ───────────────────────────────────

    [Fact]
    public async Task AModelThatChecksCleanlyIsASuccess()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => true;

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.True(result.Success);
        Assert.Equal(model.Id, result.ModelId);
        Assert.Null(result.ErrorMessage);
    }

    /// <summary>
    /// B170: <c>checkModel</c> returning true means it checked, not that it was silent. A model
    /// that passes with six warnings returns true, and the warnings are the whole reason the
    /// dialog is worth opening.
    /// </summary>
    [Fact]
    public async Task AModelThatChecksWithWarningsKeepsThemOnTheResult()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => true;
        harness.Tool.Says = _ => "Warning: 6 warnings were issued";

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.True(result.Success);
        Assert.Contains("6 warnings", result.Log);

        // ...and the warnings are not dressed up as a failure, which is why Log exists.
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task AModelThatChecksSilentlyCarriesNoLogRatherThanAnEmptyOne()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => true;
        harness.Tool.Says = _ => "   \n  ";

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.True(result.Success);
        Assert.Null(result.Log);
    }

    /// <summary>
    /// B170 again, from the other side: the log is drained before the check runs, so what comes
    /// back belongs to <i>this</i> model and not to whatever was checked before it.
    /// </summary>
    [Fact]
    public async Task AResultNeverCarriesWhatAnEarlierCheckSaid()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Buffer = "Error: something the previous model did";
        harness.Tool.Checks = _ => true;
        harness.Tool.Says = _ => "Warning: what this model did";

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.Contains("what this model did", result.Log);
        Assert.DoesNotContain("the previous model", result.Log ?? "");
    }

    [Fact]
    public async Task AModelThatFailsCarriesTheToolsOwnWords()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => false;
        harness.Tool.Says = _ => "Error: Failed to expand the model";

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.False(result.Success);
        Assert.Equal($"{harness.ToolName} Check Failed", result.Summary);
        Assert.Contains("Failed to expand", result.ErrorMessage);
        Assert.Contains("Failed to expand", result.Log);
    }

    /// <summary>
    /// The one failure that is not the user's model. Both services single it out by its text, so
    /// the dialog can say "buy a licence" rather than "your model is broken".
    /// </summary>
    [Fact]
    public async Task ADemoLicenceLimitIsNotReportedAsAFailedModel()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => false;
        harness.Tool.Says = _ => "Error: the model is too complex for the current license";

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.False(result.Success);
        Assert.Equal("Model too complex for demo license", result.Summary);
    }

    [Fact]
    public async Task AToolThatThrowsMidCheckStillProducesAResult()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Says = _ => "Error: what the tool had already said";
        harness.Tool.ThrowOnCheck = new InvalidOperationException("the socket went away");

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.False(result.Success);
        Assert.Equal($"{harness.ToolName} Check Failed", result.Summary);

        // The tool's own words, in preference to the exception's - the tool knows more about why.
        Assert.Contains("what the tool had already said", result.ErrorMessage);
    }

    /// <summary>
    /// The inner catch. When the tool has gone the second question fails too, and the exception
    /// message is all that is left.
    /// </summary>
    [Fact]
    public async Task WhenTheToolCannotEvenBeAskedWhyTheExceptionIsTheAnswer()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.ThrowOnCheck = new InvalidOperationException("the socket went away");
        harness.Tool.ThrowOnRead = new InvalidOperationException("and it is still gone");

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.False(result.Success);
        Assert.Equal("the socket went away", result.ErrorMessage);
    }

    /// <summary>
    /// Reading the log is documented as never throwing, because losing a clean result over a log
    /// that could not be fetched is a worse answer than a result with no log on it.
    /// </summary>
    [Fact]
    public async Task ALogThatCannotBeReadDoesNotLoseACleanResult()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        harness.Tool.Checks = _ => true;
        harness.Tool.ThrowOnRead = new InvalidOperationException("no log today");

        var result = await harness.Service.CheckModelAsync(model, graph);

        Assert.True(result.Success);
        Assert.Null(result.Log);
    }

    [Fact]
    public async Task TheSessionIsReusedRatherThanReconnectedPerModel()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();

        await harness.Service.CheckModelAsync(model, graph);
        await harness.Service.CheckModelAsync(model, graph);

        Assert.Equal(1, harness.Connections);
    }

    [Fact]
    public async Task ResettingDropsTheSessionAsWellAsTheFactorys()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        await harness.Service.CheckModelAsync(model, graph);

        await harness.Service.ResetAsync();
        await harness.Service.CheckModelAsync(model, graph);

        Assert.Equal(1, harness.Resets);
        Assert.Equal(2, harness.Connections);   // ...or the service kept a session the factory disowned
    }

    /// <summary>
    /// Resetting is what the settings dialog does when the tool's configuration changes, and a
    /// check still running against the old session has to stop rather than finish against it.
    /// </summary>
    [Fact]
    public async Task ResettingStopsARunThatIsStillGoing()
    {
        var harness = NewHarness();
        var (graph, package) = Package(50);

        // The first class is held inside the tool until the reset has happened, so "still going"
        // is a state the test creates rather than a moment it hopes to catch.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Tool.Checks = _ =>
        {
            started.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return true;
        };

        ModelCheckProgress? completed = null;
        harness.Service.OnCheckingComplete += p => completed = p;

        await harness.Service.StartCheckingAsync(package, graph);
        await started.Task;

        await harness.Service.ResetAsync();
        release.SetResult();
        await WaitUntilAsync(() => !harness.Service.IsRunning, "the run never stopped");

        // Cancelled, not merely finished. Resetting also drops the session, so a run that carried
        // on would fail every remaining class against a null one and report itself complete - which
        // stops at the same moment and means something entirely different to the user.
        Assert.NotNull(completed);
        Assert.True(completed.WasCancelled, "the run was not reported as cancelled");
        Assert.True(harness.Tool.ChecksRun < 50, "the run carried on after the service was reset");
    }

    // ── opening the library: EnsureLibraryLoadedAsync ────────────────────────────

    [Fact]
    public async Task ALibraryThatOpensNeedsNoSecondAttempt()
    {
        var harness = NewHarness();
        harness.Tool.Opens = _ => true;

        var (success, error) = await harness.Service.EnsureLibraryLoadedAsync(ThisFile());

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(1, harness.Tool.OpenAttempts);
        Assert.Equal(0, harness.Tool.Clears);
    }

    /// <summary>
    /// A refused open usually means the tool already has that library from an earlier session, so
    /// it is cleared and asked once more before the attempt is given up on.
    /// </summary>
    [Fact]
    public async Task ALibraryTheToolAlreadyHasIsClearedAndOpenedAgain()
    {
        var harness = NewHarness();
        var attempts = 0;
        harness.Tool.Opens = _ => ++attempts > 1;

        var (success, error) = await harness.Service.EnsureLibraryLoadedAsync(ThisFile());

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(1, harness.Tool.Clears);
        Assert.Equal(2, harness.Tool.OpenAttempts);
    }

    [Fact]
    public async Task ALibraryThatWillNotOpenTwiceIsReportedAsSuch()
    {
        var harness = NewHarness();
        harness.Tool.Opens = _ => false;

        var (success, error) = await harness.Service.EnsureLibraryLoadedAsync(ThisFile());

        Assert.False(success);
        Assert.Contains(harness.ToolName, error);
        Assert.Contains("open the file", error);
        Assert.Equal(2, harness.Tool.OpenAttempts);
    }

    /// <summary>
    /// A file that is not there is a different answer from a file the tool refused, and worth
    /// separating: one is the user's path, the other is the tool's state.
    /// </summary>
    [Fact]
    public async Task AMissingFileIsNotBlamedOnTheTool()
    {
        var harness = NewHarness();
        harness.Tool.Opens = _ => false;
        var missing = Path.Combine(Path.GetTempPath(), $"mlqt-not-here-{Guid.NewGuid():N}.mo");

        var (success, error) = await harness.Service.EnsureLibraryLoadedAsync(missing);

        Assert.False(success);
        Assert.Equal($"File not found: {missing}", error);
        Assert.Equal(1, harness.Tool.OpenAttempts);   // no point retrying a file that is not there
    }

    [Fact]
    public async Task AToolThatWillNotStartIsReportedRatherThanThrown()
    {
        var harness = NewHarness();
        harness.FailToConnect(new InvalidOperationException("not installed"));

        var (success, error) = await harness.Service.EnsureLibraryLoadedAsync(ThisFile());

        Assert.False(success);
        Assert.Contains(harness.ToolName, error);
        Assert.Contains("not installed", error);
    }

    // ── the package path: StartCheckingAsync ─────────────────────────────────────

    [Fact]
    public async Task CheckingAPackageChecksEveryClassInIt()
    {
        var harness = NewHarness();
        var (graph, package) = Package(3);
        var checkedModels = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (checkedModels) checkedModels.Add(r); };

        await RunToCompletion(harness, package, graph);

        Assert.Equal(3, checkedModels.Count);
        Assert.Equal(
            new[] { "TestPackage.Model1", "TestPackage.Model2", "TestPackage.Model3" },
            checkedModels.Select(r => r.ModelId).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// The package itself is not a class the tool can check, and a sibling package whose name
    /// merely starts with the same letters is not inside it.
    /// </summary>
    [Fact]
    public async Task CheckingAPackageChecksNeitherItselfNorItsNamesakes()
    {
        var harness = NewHarness();
        var (graph, package) = Package(1);
        AddModel(graph, "TestPackageOther.Model1");   // a namesake, not a child

        var checkedModels = new List<string>();
        harness.Service.OnModelChecked += r => { lock (checkedModels) checkedModels.Add(r.ModelId); };

        await RunToCompletion(harness, package, graph);

        Assert.Equal(new[] { "TestPackage.Model1" }, checkedModels);
    }

    /// <summary>
    /// A sub-package is not something a tool can be asked to check — only the classes in it are.
    /// A library of any size is mostly packages, so checking them too would be a long wait and a
    /// list of failures that say nothing about the user's models.
    /// </summary>
    [Fact]
    public async Task CheckingAPackageDoesNotAskTheToolAboutTheSubPackages()
    {
        var harness = NewHarness();
        var (graph, package) = Package(1);
        AddModel(graph, "TestPackage.Inner", "package");
        AddModel(graph, "TestPackage.Inner.Model1");

        var checkedModels = new List<string>();
        harness.Service.OnModelChecked += r => { lock (checkedModels) checkedModels.Add(r.ModelId); };

        await RunToCompletion(harness, package, graph);

        Assert.DoesNotContain("TestPackage.Inner", checkedModels);
        Assert.Contains("TestPackage.Inner.Model1", checkedModels);
        Assert.Equal(2, checkedModels.Count);
    }

    [Fact]
    public async Task ARunOverAPackageReportsHowManyClassesItCheckedAndThatItFinished()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);
        ModelCheckProgress? completed = null;
        harness.Service.OnCheckingComplete += p => completed = p;

        await RunToCompletion(harness, package, graph);

        Assert.NotNull(completed);
        Assert.Equal(2, completed.TotalModels);
        Assert.Equal(2, completed.ModelsChecked);
        Assert.True(completed.IsComplete);
        Assert.False(completed.WasCancelled);
    }

    /// <summary>
    /// Every result, not only the failures. A clean run that reports nothing leaves the dialog
    /// correctly concluding that nothing was checked (B170).
    /// </summary>
    [Fact]
    public async Task ACleanRunStillReportsEveryClassItChecked()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);
        harness.Tool.Checks = _ => true;
        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        Assert.Equal(2, reported.Count);
        Assert.All(reported, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task AFailureInTheMiddleOfAPackageDoesNotStopTheRest()
    {
        var harness = NewHarness();
        var (graph, package) = Package(3);
        harness.Tool.Checks = id => id != "TestPackage.Model2";
        harness.Tool.Says = id => id == "TestPackage.Model2" ? "Error: this one is broken" : "";

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        Assert.Equal(3, reported.Count);
        var failed = Assert.Single(reported, r => !r.Success);
        Assert.Equal("TestPackage.Model2", failed.ModelId);
        Assert.Contains("this one is broken", failed.ErrorMessage);
    }

    [Fact]
    public async Task ADemoLicenceLimitIsCalledOutInThePackagePathToo()
    {
        var harness = NewHarness();
        var (graph, package) = Package(1);
        harness.Tool.Checks = _ => false;
        harness.Tool.Says = _ => "Error: the model is too complex for the current license";

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        Assert.Equal("Model too complex for demo license", Assert.Single(reported).Summary);
    }

    [Fact]
    public async Task AToolThatThrowsMidPackageStillReportsThatClass()
    {
        var harness = NewHarness();
        var (graph, package) = Package(1);
        harness.Tool.ThrowOnCheck = new InvalidOperationException("the socket went away");

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        var result = Assert.Single(reported);
        Assert.False(result.Success);
        Assert.Equal($"{harness.ToolName} Check Failed", result.Summary);
    }

    /// <summary>
    /// The same promise as <see cref="AModelThatChecksWithWarningsKeepsThemOnTheResult"/>, asked of
    /// the path that runs when the user checks a package rather than one class.
    /// </summary>
    [Fact]
    public async Task AClassInAPackageKeepsItsWarningsToo()
    {
        var harness = NewHarness();
        var (graph, package) = Package(1);
        harness.Tool.Checks = _ => true;
        harness.Tool.Says = _ => "Warning: 6 warnings were issued";

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        Assert.Contains("6 warnings", Assert.Single(reported).Log);
    }

    /// <summary>
    /// The same promise as <see cref="AResultNeverCarriesWhatAnEarlierCheckSaid"/>, asked of the
    /// package path — where the classes checked before this one are the ones whose output could
    /// leak into it.
    /// </summary>
    /// <remarks>
    /// The first class checked is the one that passes, whichever the graph hands over first, so
    /// this does not depend on the order the package is walked in. It has to be a pass followed by
    /// a failure to catch both tools: Dymola's log accumulates until it is cleared, and omc's
    /// empties itself when it is read — so a failure after a failure leaks only in Dymola, while a
    /// failure after a silent pass leaks in both.
    /// </remarks>
    [Fact]
    public async Task AClassInAPackageIsNotBlamedForWhatTheOneBeforeItSaid()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);

        string? passed = null;
        harness.Tool.Checks = id => (passed ??= id) == id;
        harness.Tool.Says = id => $"[said while checking {id}]";

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };

        await RunToCompletion(harness, package, graph);

        var failed = Assert.Single(reported, r => !r.Success);
        Assert.Contains($"[said while checking {failed.ModelId}]", failed.ErrorMessage);
        Assert.DoesNotContain($"[said while checking {passed}]", failed.ErrorMessage ?? "");
    }

    [Fact]
    public async Task CheckingOneModelReportsOneModel()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        ModelCheckProgress? completed = null;
        harness.Service.OnCheckingComplete += p => completed = p;

        await RunToCompletion(harness, model, graph);

        Assert.NotNull(completed);
        Assert.Equal(1, completed.TotalModels);
    }

    /// <summary>
    /// The progress dialog is driven entirely by these, and the ones in the middle of a run are
    /// throttled to 500ms — so the two that are always sent are the only ones a short run has: the
    /// count before the first class is checked, and the finished one after the last.
    /// </summary>
    [Fact]
    public async Task ARunReportsItsCountBeforeItStartsAndAgainWhenItHasFinished()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);

        // Snapshotted, not kept: the run mutates one ModelCheckProgress in place and raises the
        // same instance each time, so holding the references would give three views of the last
        // state. The UI reads it inside the handler, which is why that is not a defect.
        var reported = new List<(int TotalModels, bool IsComplete)>();
        harness.Service.OnProgressChanged += p =>
        {
            lock (reported) reported.Add((p.TotalModels, p.IsComplete));
        };

        await RunToCompletion(harness, package, graph);

        List<(int TotalModels, bool IsComplete)> seen;
        lock (reported) seen = [.. reported];

        // Before: there is a total to show, and the dialog must not read as already finished.
        var opening = seen.First(p => p.TotalModels > 0);
        Assert.Equal(2, opening.TotalModels);
        Assert.False(opening.IsComplete);

        Assert.True(seen[^1].IsComplete, "the run never reported that it had finished");
    }

    /// <summary>
    /// The per-class updates are throttled to 500ms, because a package of thousands would otherwise
    /// re-render the dialog thousands of times. A run longer than that interval must still report
    /// as it goes — a progress bar that never moves and a frozen application look the same.
    /// </summary>
    [Fact]
    public async Task ARunLongerThanTheThrottleReportsProgressWhileItIsGoing()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);

        // Longer than the 500ms interval, so the update after the first class is due. Measured by
        // elapsed time rather than by scheduling, so a loaded machine makes it later, never sooner.
        harness.Tool.Checks = _ => { Thread.Sleep(700); return true; };

        var duringTheRun = 0;
        harness.Service.OnProgressChanged += p =>
        {
            if (p is { TotalModels: > 0, IsComplete: false })
                Interlocked.Increment(ref duringTheRun);
        };

        await RunToCompletion(harness, package, graph);

        // The opening one, plus at least one sent while the classes were being checked.
        Assert.True(Volatile.Read(ref duringTheRun) > 1,
            "nothing was reported between the run starting and it finishing");
    }

    /// <summary>
    /// B259: starting the tool and opening the library is most of the wait, and both counts are
    /// zero throughout it — so the first thing the progress dialog is given has to be words.
    /// </summary>
    [Fact]
    public async Task TheFirstThingReportedNamesTheToolBeforeThereIsAnythingToCount()
    {
        var harness = NewHarness();
        var (graph, model) = SingleModel();
        var reported = new List<ModelCheckProgress>();
        harness.Service.OnProgressChanged += p => { lock (reported) reported.Add(p); };

        await RunToCompletion(harness, model, graph);

        string first;
        lock (reported) first = reported[0].Status ?? "";
        Assert.Contains(harness.ToolName, first);
    }

    [Fact]
    public async Task ALibraryThatWillNotOpenEndsTheRunRatherThanCheckingNothingQuietly()
    {
        var harness = NewHarness();
        var (graph, package) = PackageInAFile(2);
        harness.Tool.Opens = _ => false;

        var reported = new List<ModelCheckResult>();
        harness.Service.OnModelChecked += r => { lock (reported) reported.Add(r); };
        ModelCheckProgress? completed = null;
        harness.Service.OnCheckingComplete += p => completed = p;

        await RunToCompletion(harness, package, graph);

        var result = Assert.Single(reported);
        Assert.Equal("Failed to load library", result.Summary);
        Assert.False(result.Success);
        Assert.NotNull(completed);
        Assert.True(completed.IsComplete);
        Assert.False(completed.WasCancelled);
        Assert.Equal(0, harness.Tool.ChecksRun);
    }

    [Fact]
    public async Task APackageInAFileIsOpenedBeforeAnythingInItIsChecked()
    {
        var harness = NewHarness();
        var (graph, package) = PackageInAFile(1);

        await RunToCompletion(harness, package, graph);

        Assert.Equal(1, harness.Tool.OpenAttempts);
        Assert.Equal("open", harness.Tool.Calls.First(c => c is "open" or "check"));
    }

    [Fact]
    public async Task ACancelledRunSaysSoRatherThanReportingCompletion()
    {
        var harness = NewHarness();
        var (graph, package) = Package(50);

        // Cancels as soon as the tool is asked about anything, so the run is stopped part-way
        // rather than at a moment the test hopes to catch.
        harness.Tool.Checks = _ => { harness.Service.StopChecking(); return true; };

        ModelCheckProgress? completed = null;
        harness.Service.OnCheckingComplete += p => completed = p;

        await RunToCompletion(harness, package, graph);

        Assert.NotNull(completed);
        Assert.True(completed.WasCancelled);
        Assert.True(completed.IsComplete);
        Assert.True(harness.Tool.ChecksRun < 50, "the run carried on after it was stopped");
    }

    [Fact]
    public async Task ASecondRunIsRefusedWhileTheFirstIsStillGoing()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Tool.Checks = _ => { gate.Task.GetAwaiter().GetResult(); return true; };

        await harness.Service.StartCheckingAsync(package, graph);
        await WaitUntilAsync(() => harness.Service.IsRunning, "the first run never started");

        await harness.Service.StartCheckingAsync(package, graph);   // the call under test

        gate.SetResult();
        await WaitUntilAsync(() => !harness.Service.IsRunning, "the first run never finished");

        Assert.Equal(2, harness.Tool.ChecksRun);   // 4 would mean the second run also happened
    }

    /// <summary>
    /// The events are raised on the background thread that does the checking, so a subscriber that
    /// throws throws <i>there</i>. The run has to end anyway: <see cref="IModelCheckingService.IsRunning"/>
    /// left true is a service that refuses every later check, with nothing on screen to say why.
    /// </summary>
    [Fact]
    public async Task ASubscriberThatThrowsDoesNotLeaveTheServiceStuck()
    {
        var harness = NewHarness();
        var (graph, package) = Package(2);
        harness.Service.OnModelChecked += _ => throw new InvalidOperationException("a bad subscriber");

        var completed = new List<ModelCheckProgress>();
        harness.Service.OnCheckingComplete += p => { lock (completed) completed.Add(p); };

        await RunToCompletion(harness, package, graph);

        Assert.False(harness.Service.IsRunning);

        var finished = Assert.Single(completed);
        Assert.True(finished.IsComplete);

        // ...and it is reported as finished, not as cancelled: nobody asked for it to stop.
        Assert.False(finished.WasCancelled);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────

    /// <summary>A file that exists, for the paths that ask.</summary>
    private static string ThisFile() => typeof(ToolHarness).Assembly.Location;

    private protected static (DirectedGraph graph, ModelNode model) SingleModel()
    {
        var graph = new DirectedGraph();
        var model = AddModel(graph, "TestModel");
        return (graph, model);
    }

    private protected static (DirectedGraph graph, ModelNode package) Package(int children)
    {
        var graph = new DirectedGraph();
        var package = AddModel(graph, "TestPackage", "package");
        for (var i = 1; i <= children; i++)
            AddModel(graph, $"TestPackage.Model{i}");
        return (graph, package);
    }

    /// <summary>
    /// The same, with the package's own file on the graph — which is what makes the service open
    /// something before it checks anything.
    /// </summary>
    private static (DirectedGraph graph, ModelNode package) PackageInAFile(int children)
    {
        var (graph, package) = Package(children);
        var file = new FileNode("file1", ThisFile());
        graph.AddNode(file);
        graph.AddFileContainsModel("file1", package.Id);
        return (graph, package);
    }

    private protected static ModelNode AddModel(DirectedGraph graph, string id, string classType = "model")
    {
        var name = id[(id.LastIndexOf('.') + 1)..];
        var code = $"{classType} {name}\nend {name};";
        var node = new ModelNode(id, new ModelDefinition(name, code) { ParsedCode = ModelicaParserHelper.Parse(code) })
        {
            ClassType = classType
        };
        graph.AddNode(node);
        return node;
    }

    private static async Task RunToCompletion(ToolHarness harness, ModelNode model, DirectedGraph graph)
    {
        await harness.Service.StartCheckingAsync(model, graph);
        await WaitUntilAsync(() => !harness.Service.IsRunning, "the run never finished");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string whatFailed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.True(condition(), whatFailed);
    }
}

public class DymolaCheckingServiceContractTests : ModelCheckingServiceContract
{
    protected override ToolHarness NewHarness() => new DymolaHarness();

    /// <summary>
    /// <c>checkModel</c> takes flags that make it do rather more than check, and MLQT passes false
    /// for both. Simulating every class in a library while the user waits for a style check is not
    /// a slower version of the right answer.
    /// </summary>
    [Fact]
    public async Task DymolaIsAskedToCheckAndNotToSimulate()
    {
        var harness = new DymolaHarness();
        var tool = (FakeDymola)harness.Tool;
        var (graph, model) = SingleModel();

        await harness.Service.CheckModelAsync(model, graph);

        Assert.Equal((Simulate: false, Constraint: false), tool.LastCheckFlags);
    }

    /// <summary>
    /// <c>openModel</c> likewise. <c>changeDirectory</c> moves Dymola's working directory to the
    /// library being opened, which would silently relocate whatever the user does in that session
    /// afterwards; <c>mustRead</c> turns a library Dymola already has into an error rather than
    /// letting the retry below handle it.
    /// </summary>
    [Fact]
    public async Task DymolaIsAskedToOpenTheLibraryWithoutMovingItsWorkingDirectory()
    {
        var harness = new DymolaHarness();
        var tool = (FakeDymola)harness.Tool;

        // Refused once, so the retry is asked the same way as the first attempt.
        var attempts = 0;
        tool.Opens = _ => ++attempts > 1;

        await harness.Service.EnsureLibraryLoadedAsync(typeof(ToolHarness).Assembly.Location);

        Assert.Equal(2, tool.OpenFlags.Count);
        Assert.All(tool.OpenFlags, flags => Assert.Equal((MustRead: false, ChangeDirectory: false), flags));
    }
}

public class OpenModelicaCheckingServiceContractTests : ModelCheckingServiceContract
{
    protected override ToolHarness NewHarness() => new OpenModelicaHarness();
}

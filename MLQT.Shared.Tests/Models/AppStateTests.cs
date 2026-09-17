using MudBlazor;
using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Shared.Tests.Models;

/// <summary>
/// The cross-component event bus.
/// </summary>
/// <remarks>
/// <para>Every component in the application subscribes to something here, and the contract is always
/// the same shape: <b>the method is what raises the event</b>. Setting a property directly leaves
/// every subscriber unaware, which is why the properties have private setters and why CLAUDE.md
/// tells callers to use the methods. These tests hold that contract.</para>
///
/// <para>It reached phase 7b at 4% covered with a coverage-ledger entry reading "Nobody has written
/// the tests" — the one entry in that file whose reason was an admission rather than a constraint.
/// It has no dependencies and needs no renderer, so there was never anything stopping it.</para>
///
/// <para>The tests are written from the subscriber's side: what a component would see, not what the
/// field holds. A raise that does not happen is invisible to a unit test asserting on state alone,
/// and that is the failure mode this class can actually have — the events were removed from
/// <c>AppState</c> once already (commit <c>d3bbed7</c>) after nine months of never being raised.</para>
/// </remarks>
public class AppStateTests
{
    /// <summary>Counts raises, so a test can say "once" rather than "at least once".</summary>
    private sealed class Recorder
    {
        public int Count { get; private set; }
        public void Record() => Count++;
    }

    // ---- model selection -------------------------------------------------------------------

    [Fact]
    public void ChangingTheModel_SetsItAndTellsSubscribers()
    {
        var state = new AppState();
        var raised = new Recorder();
        state.OnChangeModel += raised.Record;

        state.ChangeModelID("Modelica.Blocks.Add");

        Assert.Equal("Modelica.Blocks.Add", state.ModelID);
        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public void ChangingTheModelToTheSameValue_StillNotifies()
    {
        // Pinned deliberately. Components re-read more than the model id when this fires - the code
        // viewer reloads, the tree re-highlights - so suppressing a repeat would be a behaviour
        // change, not an optimisation. Anyone adding a guard should have to change this test.
        var state = new AppState();
        var raised = new Recorder();
        state.ChangeModelID("A");
        state.OnChangeModel += raised.Record;

        state.ChangeModelID("A");

        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public void TheModelStartsEmptyRatherThanNull()
    {
        // Every consumer treats "no selection" as the empty string; a null here would be a
        // NullReferenceException in whichever component read it first.
        Assert.Equal(string.Empty, new AppState().ModelID);
    }

    [Fact]
    public void SettingTheSelection_ReplacesItAndNotifiesOnce()
    {
        var state = new AppState();
        var raised = new Recorder();
        state.OnSelectedModelsChanged += raised.Record;

        state.SetSelectedModels(["a", "b"]);
        state.SetSelectedModels(["c"]);

        Assert.Equal(["c"], state.SelectedModelIDs);
        Assert.Equal(2, raised.Count);
    }

    [Fact]
    public void SettingTheSelection_TakesACopy()
    {
        // The caller's collection must not stay live: LibraryBrowser passes the list it is about to
        // mutate as the user carries on clicking, and a shared reference would change the selection
        // under everyone without an event.
        // A HashSet, deliberately: the property is a HashSet, so passing a List forces a copy
        // whatever the implementation does and the test could never fail. Only a same-typed source
        // can distinguish "copied" from "kept the caller's reference".
        var state = new AppState();
        var source = new HashSet<string> { "a" };

        state.SetSelectedModels(source);
        source.Add("b");

        Assert.Equal(["a"], state.SelectedModelIDs);
    }

    [Fact]
    public void SettingTheSelection_DeduplicatesIt()
    {
        var state = new AppState();

        state.SetSelectedModels(["a", "a", "b"]);

        Assert.Equal(2, state.SelectedModelIDs.Count);
    }

    [Fact]
    public void ClearingTheSelection_EmptiesItAndNotifies()
    {
        var state = new AppState();
        state.SetSelectedModels(["a", "b"]);
        var raised = new Recorder();
        state.OnSelectedModelsChanged += raised.Record;

        state.ClearSelectedModels();

        Assert.Empty(state.SelectedModelIDs);
        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public void ChangingTheSelectionMode_SetsItAndNotifies()
    {
        var state = new AppState();
        var raised = new Recorder();
        state.OnEnableMultiSelect += raised.Record;

        state.ChangeSelectionMode(SelectionMode.MultiSelection);

        Assert.Equal(SelectionMode.MultiSelection, state.SelectionMode);
        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public void TheDefaultSelectionMode_IsSingle()
    {
        Assert.Equal(SelectionMode.SingleSelection, new AppState().SelectionMode);
    }

    // ---- the events that carry a payload ---------------------------------------------------

    [Fact]
    public void RepositorySettingsApplied_CarriesBothFlagsUnchanged()
    {
        // The two booleans decide whether a full reformat and a re-check run. Swapping them is
        // invisible at the call site and expensive at the other end, so the order is asserted rather
        // than assumed.
        var state = new AppState();
        (string Id, bool Formatting, bool Style)? seen = null;
        state.OnRepositorySettingsApplied += (id, formatting, style) => seen = (id, formatting, style);

        state.RepositorySettingsApplied("repo-1", formattingChanged: true, styleSettingsChanged: false);

        Assert.Equal(("repo-1", true, false), seen);
    }

    [Fact]
    public void VcsModelsChanged_CarriesTheRepositoryAndTheModels()
    {
        var state = new AppState();
        (string Id, IReadOnlyList<string> Models)? seen = null;
        state.OnVcsModelsChanged += (id, models) => seen = (id, models);

        state.VcsModelsChanged("repo-1", ["m1", "m2"]);

        Assert.Equal("repo-1", seen!.Value.Id);
        Assert.Equal(["m1", "m2"], seen.Value.Models);
    }

    [Fact]
    public void VcsFilesChanged_CarriesTheRepository()
    {
        var state = new AppState();
        string? seen = null;
        state.OnVcsFilesChanged += id => seen = id;

        state.VcsFilesChanged("repo-1");

        Assert.Equal("repo-1", seen);
    }

    [Fact]
    public void ModelContentChanged_CarriesTheModels()
    {
        var state = new AppState();
        IReadOnlyCollection<string>? seen = null;
        state.OnModelContentChanged += models => seen = models;

        state.ModelContentChanged(["m1"]);

        Assert.Equal(["m1"], seen);
    }

    [Fact]
    public void ThemeChanged_CarriesTheSettings()
    {
        var state = new AppState();
        UISettings? seen = null;
        state.OnThemeChanged += ui => seen = ui;
        var settings = new UISettings { Theme = Theme.Dark };

        state.ThemeChanged(settings);

        Assert.Same(settings, seen);
    }

    [Fact]
    public void ProjectChanged_CarriesTheProject()
    {
        var state = new AppState();
        string? seen = null;
        state.OnProjectChanged += id => seen = id;

        state.ProjectChanged("project-1");

        Assert.Equal("project-1", seen);
    }

    // ---- raising with nothing attached ------------------------------------------------------

    [Fact]
    public void EveryNotifierIsSafeWithNoSubscribers()
    {
        // Every one of these runs before the UI has finished wiring up, and at shutdown after
        // components have unsubscribed. A missing null-conditional would be a crash on a path nobody
        // exercises deliberately.
        var state = new AppState();

        state.ChangeModelID("a");
        state.SetSelectedModels(["a"]);
        state.ClearSelectedModels();
        state.ChangeSelectionMode(SelectionMode.MultiSelection);
        state.ModelContentChanged(["a"]);
        state.SaveSettings();
        state.ThemeChanged(new UISettings());
        state.RepositorySettingsApplied("r", false, false);
        state.VcsFilesChanged("r");
        state.VcsModelsChanged("r", ["m"]);
        state.ProjectSwitchStarting();
        state.ProjectChanged("p");
        state.ClearLogMessages();
    }

    [Fact]
    public async Task EveryAsyncTriggerIsSafeWithNoSubscribers()
    {
        // These are Func<Task> events, so "no subscribers" means there is no task to await - and the
        // guard is a null check rather than a null-conditional. Getting it wrong throws rather than
        // no-ops.
        var state = new AppState();

        await state.RunDeferredDependenciesAsync();
        await state.RunDeferredStyleCheckingAsync();
        await state.RunDeferredExternalResourcesAsync();
        await state.RunAllDeferredAnalysisAsync();
        await state.RerunStyleCheckingAsync();
        await state.FormatChangedFilesForCommitAsync("repo-1");
    }

    [Fact]
    public async Task TheAsyncTriggersAwaitTheirSubscriber()
    {
        // The point of Func<Task> rather than Action: the caller shows a progress dialog and needs
        // the work to be finished when the await returns. An Action would complete immediately and
        // the dialog would close over a run still in flight.
        var state = new AppState();
        var finished = false;
        state.OnRunAllDeferredAnalysis += async () =>
        {
            await Task.Delay(20);
            finished = true;
        };

        await state.RunAllDeferredAnalysisAsync();

        Assert.True(finished);
    }

    // ---- deferred analysis ------------------------------------------------------------------

    [Fact]
    public void EnablingDeferredMode_ResetsTheThreeCompletionFlags()
    {
        var state = new AppState();
        state.DependencyAnalysisCompleted();
        state.StyleCheckingCompleted();
        state.ExternalResourcesAnalysisCompleted();

        state.EnableDeferredMode();

        Assert.True(state.IsDeferredMode);
        Assert.False(state.HasDependencyAnalysisRun);
        Assert.False(state.HasStyleCheckingRun);
        Assert.False(state.HasExternalResourcesAnalyzed);
    }

    [Theory]
    [InlineData("dependencies")]
    [InlineData("style")]
    [InlineData("resources")]
    public void EachCompletion_SetsOnlyItsOwnFlagAndNotifies(string step)
    {
        // Three near-identical methods next to each other is exactly where a copy-paste sets the
        // wrong flag, and the symptom would be a deferred step that silently never runs.
        var state = new AppState();
        var raised = new Recorder();
        state.OnDeferredAnalysisCompleted += raised.Record;

        switch (step)
        {
            case "dependencies": state.DependencyAnalysisCompleted(); break;
            case "style": state.StyleCheckingCompleted(); break;
            default: state.ExternalResourcesAnalysisCompleted(); break;
        }

        Assert.Equal(step == "dependencies", state.HasDependencyAnalysisRun);
        Assert.Equal(step == "style", state.HasStyleCheckingRun);
        Assert.Equal(step == "resources", state.HasExternalResourcesAnalyzed);
        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public async Task RerunningStyleChecking_ClearsTheGuardFirst()
    {
        // The whole reason the method exists: the caller has already run style checking once, and
        // without clearing the flag the subscriber's own guard would skip the second run. The flag
        // is read *inside* the handler, so it has to be false by then rather than afterwards.
        var state = new AppState();
        state.StyleCheckingCompleted();
        bool? flagSeenByHandler = null;
        state.OnRunDeferredStyleChecking += () =>
        {
            flagSeenByHandler = state.HasStyleCheckingRun;
            return Task.CompletedTask;
        };

        await state.RerunStyleCheckingAsync();

        Assert.False(flagSeenByHandler);
    }

    [Fact]
    public void ResettingDeferredState_ClearsEverything()
    {
        // Called on a project switch. A flag left set means the new project skips an analysis the
        // old one had already done.
        var state = new AppState();
        state.EnableDeferredMode();
        state.DependencyAnalysisCompleted();
        state.StyleCheckingCompleted();
        state.ExternalResourcesAnalysisCompleted();

        state.ResetDeferredState();

        Assert.False(state.IsDeferredMode);
        Assert.False(state.HasDependencyAnalysisRun);
        Assert.False(state.HasStyleCheckingRun);
        Assert.False(state.HasExternalResourcesAnalyzed);
    }

    [Fact]
    public void DisablingDeferredMode_LeavesTheCompletionFlagsAlone()
    {
        // It means "everything has already run", not "start again" - which is the difference between
        // it and ResetDeferredState, and the two are one line apart.
        var state = new AppState();
        state.EnableDeferredMode();
        state.DependencyAnalysisCompleted();

        state.DisableDeferredMode();

        Assert.False(state.IsDeferredMode);
        Assert.True(state.HasDependencyAnalysisRun);
    }

    // ---- session memory ---------------------------------------------------------------------

    [Fact]
    public void MetricsScope_IsPlainSessionMemoryWithNoEvent()
    {
        // Deliberately not a notifier: the Metrics tab writes it so its scope survives the panel
        // being recreated, and nothing else reads it. Asserted so that adding an event here is a
        // decision rather than an assumption about consistency with its neighbours.
        var state = new AppState();

        state.MetricsScope = "Modelica.Blocks";

        Assert.Equal("Modelica.Blocks", state.MetricsScope);
        Assert.Equal(string.Empty, new AppState().MetricsScope);
    }
}

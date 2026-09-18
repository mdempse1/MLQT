using System.Text.RegularExpressions;
using MLQT.Shared.Layout;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The startup progress dialog's deferred steps — the rows offering to run dependency analysis,
/// style checking and external-resource analysis once loading has finished (B194).
///
/// <para><b>What was wrong.</b> Each step could only be started from the small play button in its
/// avatar slot. The row itself — the words "Style checking", and all the space around them — did
/// nothing, which on a list of actions is the opposite of what it looks like.</para>
///
/// <para><b>What the change brings with it.</b> A click on the play button bubbles to the row, so one
/// press now reaches two handlers. <see cref="MainLayout.ShouldStartDeferredStep"/> is what stops the
/// second from starting the work again, and it is the only part of this with logic in it, so it is
/// static and tested directly. The wiring itself is checked by reading the markup, the way this
/// repository already checks the installer scripts: it is a decision that compiles perfectly whether
/// or not it was made.</para>
/// </summary>
public class StartupDialogDeferredStepTests
{
    [Fact]
    public void ADeferredStepWithNothingElseRunning_Starts()
    {
        Assert.True(MainLayout.ShouldStartDeferredStep(stepIsDeferred: true, anyStepRunning: false));
    }

    [Fact]
    public void TheSecondArrivalOfOneClick_DoesNotStartTheWorkAgain()
    {
        // The re-entrancy the row introduces. The first handler sets the running flag before its
        // first await, so the one that follows it sees this state.
        Assert.False(MainLayout.ShouldStartDeferredStep(stepIsDeferred: true, anyStepRunning: true));
    }

    [Fact]
    public void ARowForAStepThatHasAlreadyRun_DoesNothing()
    {
        // A finished step keeps its row; clicking it must not re-run the analysis.
        Assert.False(MainLayout.ShouldStartDeferredStep(stepIsDeferred: false, anyStepRunning: false));
    }

    [Fact]
    public void ARowClickedWhileAnotherStepRuns_DoesNothing()
    {
        // The state the play buttons express by being disabled. The row has no disabled attribute to
        // rely on, so the guard is what makes it behave the same way.
        Assert.False(MainLayout.ShouldStartDeferredStep(stepIsDeferred: false, anyStepRunning: true));
    }

    /// <summary>
    /// The three deferred rows of the startup dialog, by the handler each one runs.
    /// </summary>
    public static TheoryData<string> DeferredStepHandlers() =>
    [
        "RunDeferredDependenciesFromDialogAsync",
        "RunDeferredStyleCheckingFromDialogAsync",
        "RunDeferredExternalResourcesFromDialogAsync"
    ];

    [Theory]
    [MemberData(nameof(DeferredStepHandlers))]
    public void EachDeferredStepRowRunsItsStep(string handler)
    {
        // Read from the markup because there is nothing else to read it from: MainLayout cannot be
        // rendered in this suite, and a MudListItem with no OnClick compiles and looks right.
        var markup = MainLayoutMarkup();
        if (markup is null)
            return;     // sources not beside the test binary; nothing to check

        // The row: a MudListItem carrying this handler on OnClick. The play button carries it too,
        // and is matched by the separate assertion below, so this looks for the list item
        // specifically.
        var onListItem = Regex.IsMatch(
            markup, @"<MudListItem\b[^>]*OnClick=""" + Regex.Escape(handler) + @"""");

        Assert.True(onListItem,
            $"the startup dialog's row for {handler} does not run it — a MudListItem with OnClick=\"{handler}\" " +
            "is what makes the whole row clickable rather than only its play button (B194)");
    }

    [Theory]
    [MemberData(nameof(DeferredStepHandlers))]
    public void EachDeferredStepKeepsItsPlayButton(string handler)
    {
        // The button is not replaced by the row. It is what shows at a glance that the row is
        // actionable, and it is the keyboard target.
        var markup = MainLayoutMarkup();
        if (markup is null)
            return;

        var onButton = Regex.IsMatch(
            markup, @"<MudIconButton\b[^>]*OnClick=""" + Regex.Escape(handler) + @"""", RegexOptions.Singleline);

        Assert.True(onButton, $"the play button for {handler} is gone; the row was meant to be added alongside it");
    }

    private static string? MainLayoutMarkup()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared", "Layout", "MainLayout.razor");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        return null;
    }
}

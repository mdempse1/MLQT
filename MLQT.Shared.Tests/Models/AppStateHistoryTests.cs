using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Shared.Tests.Models;

/// <summary>
/// The way back through the classes visited (B197) — "ideally back through the last few classes
/// visited, like an editor's navigation stack".
///
/// <para>On <see cref="AppState"/> rather than on a page because every surface moves the selection:
/// the tree, a finding, the dependency graph. A history kept by one of them would only know about
/// its own moves, which is the half that is not worth having.</para>
/// </summary>
public class AppStateHistoryTests
{
    private static AppState Visited(params string[] classes)
    {
        var state = new AppState();
        foreach (var name in classes)
            state.ChangeModelID(name);
        return state;
    }

    [Fact]
    public void WithNowhereToGoBothWaysAreClosed()
    {
        var state = new AppState();

        Assert.False(state.CanGoBack);
        Assert.False(state.CanGoForward);
    }

    [Fact]
    public void OneClassIsNotSomethingToGoBackFrom() =>
        Assert.False(Visited("Lib.A").CanGoBack);

    [Fact]
    public void GoingBackReturnsToTheClassBefore()
    {
        var state = Visited("Lib.A", "Lib.B");

        state.GoBack();

        Assert.Equal("Lib.A", state.ModelID);
        Assert.False(state.CanGoBack);
        Assert.True(state.CanGoForward);
    }

    [Fact]
    public void GoingForwardReturnsToWhereTheUserWas()
    {
        var state = Visited("Lib.A", "Lib.B", "Lib.C");

        state.GoBack();
        state.GoBack();
        Assert.Equal("Lib.A", state.ModelID);

        state.GoForward();
        state.GoForward();

        Assert.Equal("Lib.C", state.ModelID);
        Assert.False(state.CanGoForward);
    }

    [Fact]
    public void GoingBackDoesNotCountAsAVisit()
    {
        // Otherwise back would append A and then go back to B, and the two buttons would walk the
        // user around in a circle instead of anywhere.
        var state = Visited("Lib.A", "Lib.B");

        state.GoBack();
        state.GoBack();

        Assert.Equal("Lib.A", state.ModelID);
    }

    [Fact]
    public void GoingSomewhereNewAfterGoingBackDiscardsWhatWasAhead()
    {
        // What every editor and browser does: keeping it would offer a forward to a class the user
        // has just chosen not to look at.
        var state = Visited("Lib.A", "Lib.B");
        state.GoBack();

        state.ChangeModelID("Lib.C");

        Assert.False(state.CanGoForward);
        Assert.True(state.CanGoBack);

        state.GoBack();
        Assert.Equal("Lib.A", state.ModelID);
    }

    [Fact]
    public void ReSelectingTheSameClassIsNotAVisit()
    {
        // It happens on its own — LibraryBrowser re-raises the current selection after a VCS
        // operation to refresh the page — and counting it would fill the history with one class.
        var state = Visited("Lib.A", "Lib.B", "Lib.B", "Lib.B");

        state.GoBack();

        Assert.Equal("Lib.A", state.ModelID);
        Assert.False(state.CanGoBack);
    }

    [Fact]
    public void TheClassesBehindAreListedNearestFirst()
    {
        var state = Visited("Lib.A", "Lib.B", "Lib.C");

        Assert.Equal(["Lib.B", "Lib.A"], state.Back);
    }

    [Fact]
    public void ThereIsNothingBehindTheFirstClass() =>
        Assert.Empty(Visited("Lib.A").Back);

    [Fact]
    public void SelectingNothingIsNotAVisit()
    {
        // Clearing the selection happens when a project is switched, and it is not a place to
        // return to.
        var state = Visited("Lib.A", "Lib.B");

        state.ChangeModelID("");

        Assert.Equal(["Lib.A"], state.Back);
    }

    [Fact]
    public void TheHistoryDoesNotGrowForever()
    {
        // Long enough to get back, not so long that a session's browsing is held forever.
        var state = new AppState();
        for (var i = 0; i < 120; i++)
            state.ChangeModelID($"Lib.C{i}");

        Assert.True(state.Back.Count <= 50, $"the history kept {state.Back.Count} classes");

        // And the oldest went, not the newest: the class just left is still the one behind.
        Assert.Equal("Lib.C118", state.Back[0]);
    }

    [Fact]
    public void GoingBackRaisesTheSameEventAnOrdinarySelectionDoes()
    {
        // Everything that shows a class listens to this one event, so a move that did not raise it
        // would change the selection and leave every view showing the old class.
        var state = Visited("Lib.A", "Lib.B");
        var raised = 0;
        state.OnChangeModel += () => raised++;

        state.GoBack();
        state.GoForward();

        Assert.Equal(2, raised);
    }
}

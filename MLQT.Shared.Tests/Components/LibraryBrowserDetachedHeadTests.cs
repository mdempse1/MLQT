using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B193 — what the browser says when there is no branch to name.
/// </summary>
/// <remarks>
/// <para>Checking out a tag leaves Git with a detached HEAD, so <c>CurrentBranch</c> is correctly
/// null and the browser showed the words "Detached HEAD" and nothing else. That is the state a user
/// reaches by doing the thing this item added — switching to a released version — so leaving them to
/// work out <i>which</i> version they are looking at would make the feature worse than not having
/// it.</para>
/// </remarks>
public class LibraryBrowserDetachedHeadTests : MlqtComponentTestBase
{
    private static Repository Repo(string? branch, string? detachedLabel) =>
        new()
        {
            Id = "repo-1",
            Name = "ExternData",
            LocalPath = Path.Combine(Path.GetTempPath(), "mlqt-tests", "ExternData"),
            VcsType = RepositoryVcsType.Git,
            CurrentRevision = "a1b2c3d4e5f6",
            CurrentBranch = branch,
            DetachedHeadLabel = detachedLabel,
        };

    private IRenderedComponent<MudBlazor.MudPopoverProvider>? _popovers;

    private IRenderedComponent<LibraryBrowser> RenderBrowser(Repository repository, bool withChanges = false)
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>()))
               .ReturnsAsync(new List<ModelNode>());

        var repositories = new Mock<IRepositoryService>();
        if (withChanges)
        {
            repositories.Setup(r => r.GetWorkingCopyChanges(repository.Id))
                        .Returns([new VcsWorkingCopyFile { Path = "notes.txt", Status = VcsFileStatus.Modified }]);
        }

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        // The browser asks this what kind of change each model carries (B191). No model here has a
        // change, so the answer is always empty - it just has to be an answer.
        var classifier = new Mock<IModelChangeClassifier>();
        classifier.Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
                  .Returns(new Dictionary<string, ClassChangeKind>());
        Services.AddSingleton(classifier.Object);

        // Rendered here rather than by RenderProviders, to keep hold of the popover provider: the
        // Git "More actions" menu renders into it, not into the browser.
        _popovers = Render<MudBlazor.MudPopoverProvider>();
        Render<MudBlazor.MudDialogProvider>();
        Render<MudBlazor.MudSnackbarProvider>();

        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));
    }

    [Fact]
    public void ATagIsNamedWhereTheBranchNameWouldBe()
    {
        var browser = RenderBrowser(Repo(branch: null, detachedLabel: "v2.0.0"));

        Assert.Contains("Detached HEAD at", browser.Markup);
        Assert.Contains("v2.0.0", browser.Markup);
    }

    [Fact]
    public void WithNothingToNameItStillSaysDetachedHead()
    {
        // A revision checked out directly, or a repository MLQT could not ask. The words on their
        // own are still better than a blank, which is what this replaced.
        var browser = RenderBrowser(Repo(branch: null, detachedLabel: null));

        Assert.Contains("Detached HEAD", browser.Markup);
        Assert.DoesNotContain("Detached HEAD at", browser.Markup);
    }

    // ---- What is offered on a detached HEAD (B327) --------------------------------------------
    //
    // Commit, Merge, Rebase and Push all act on a branch, and were all offered on a detached HEAD
    // gated only on uncommitted changes - a commit there belongs to nothing and the next switch
    // strands it. The way out is offered where the state is named.

    private static readonly string[] BranchActions =
        ["Rebase current branch", "Merge branch into working copy", "Push changes to remote", "Create pull request"];

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<LibraryBrowser> browser, string label) =>
        browser.Find($"button[aria-label='{label}']");

    [Fact]
    public void OnADetachedHead_CommitIsOff_EvenWithChanges_AndCreatingABranchIsHighlighted()
    {
        var browser = RenderBrowser(Repo(branch: null, detachedLabel: "v2.0.0"), withChanges: true);

        // Revert is gated on the changes alone, so its enabling says the changes have been read.
        browser.WaitForAssertion(() => Assert.False(Button(browser, "Revert changes").HasAttribute("disabled")));
        Assert.True(Button(browser, "Commit changes").HasAttribute("disabled"));
        Assert.Contains("mud-button-filled", Button(browser, "Create new branch").ClassName);
    }

    [Fact]
    public void OnABranch_CommitIsOn_AndCreatingABranchIsNotHighlighted()
    {
        // The control: the same changes on a branch leave Commit enabled.
        var browser = RenderBrowser(Repo(branch: "main", detachedLabel: null), withChanges: true);

        browser.WaitForAssertion(() => Assert.False(Button(browser, "Commit changes").HasAttribute("disabled")));
        Assert.DoesNotContain("mud-button-filled", Button(browser, "Create new branch").ClassName);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("main", false)]
    public void TheBranchActions_AreOffOnlyOnADetachedHead(string? branch, bool disabled)
    {
        var browser = RenderBrowser(Repo(branch, detachedLabel: branch is null ? "v2.0.0" : null));

        Button(browser, "More actions").Click();

        foreach (var label in BranchActions)
        {
            _popovers!.WaitForAssertion(() =>
                Assert.Equal(disabled, _popovers.Find($"button[aria-label='{label}']").HasAttribute("disabled")));
        }
    }

    [Fact]
    public void OnABranchTheBranchIsNamedAndNothingIsDetached()
    {
        var browser = RenderBrowser(Repo(branch: "main", detachedLabel: null));

        Assert.Contains("Current branch", browser.Markup);
        Assert.Contains("main", browser.Markup);
        Assert.DoesNotContain("Detached HEAD", browser.Markup);
    }
}

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// B193 — tags are offered, and labelled as tags.
/// </summary>
/// <remarks>
/// <para>Switching to a tagged version was not offered for Git at all. The VCS layer now lists tags
/// beside branches; this is the other half, and the half that would otherwise be assumed: a tag
/// reaching the list is no use if the list renders it as a branch, because what happens when you
/// pick one is different — Git leaves HEAD detached, on no branch.</para>
///
/// <para>SVN needs nothing here. Its tags are directories and have always arrived as ordinary
/// <c>tags/*</c> entries, which the tree already grouped; the test for that is the last one, and it
/// exists so the new grouping cannot quietly take the old path over.</para>
/// </remarks>
public class BranchSelectorTagTests : MlqtComponentTestBase
{
    private static VcsBranchInfo Branch(string name, bool current = false) =>
        new() { Name = name, IsCurrent = current };

    private static VcsBranchInfo Tag(string name, bool current = false) =>
        new() { Name = name, IsTag = true, IsCurrent = current };

    private Mock<IRepositoryService> _repositories = new();

    private IRenderedComponent<BranchSelector> RenderSelector(params VcsBranchInfo[] refs)
    {
        _repositories = new Mock<IRepositoryService>();
        _repositories.Setup(r => r.GetBranches("repo-1", It.IsAny<bool>())).Returns(refs.ToList());
        _repositories.Setup(r => r.GetRepository("repo-1"))
                     .Returns(new Repository { Id = "repo-1", Name = "ExternData", CurrentBranch = "main" });

        Services.AddSingleton(_repositories.Object);
        RenderProviders();

        return Render<BranchSelector>(p => p.Add(c => c.RepositoryId, "repo-1"));
    }

    [Fact]
    public void TheBranchListIsReadOnceWhenTheSelectorOpens()
    {
        // It was read twice. OnInitializedAsync loads, and the parameter set that follows it saw
        // _lastRepositoryId still null and loaded again - two calls into git or svn per open, and a
        // second pass through the loading state, which blanks the tree that had just been drawn and
        // puts it back.
        //
        // The flicker is too quick to see and was not too quick for a test: SwitchBranchDialogWidthTests
        // found a row in the first tree and clicked it while the second load had the tree off screen.
        // It passed on every machine it was run on and failed once on a loaded CI runner, which is
        // the only way a race of that width ever tells you.
        var selector = RenderSelector(Branch("main", current: true), Tag("v2.0.0"));
        selector.WaitForAssertion(() => Assert.Contains("v2.0.0", selector.Markup));

        _repositories.Verify(r => r.GetBranches("repo-1", It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void ChangingTheRepositoryStillReloads()
    {
        // The guard is there to catch a real change, and it still does.
        var selector = RenderSelector(Branch("main", current: true), Tag("v2.0.0"));
        selector.WaitForAssertion(() => Assert.Contains("v2.0.0", selector.Markup));

        _repositories.Setup(r => r.GetBranches("repo-2", It.IsAny<bool>()))
                     .Returns(new List<VcsBranchInfo> { Branch("other", current: true) });
        _repositories.Setup(r => r.GetRepository("repo-2"))
                     .Returns(new Repository { Id = "repo-2", Name = "Other", CurrentBranch = "other" });

        selector.Render(p => p.Add(c => c.RepositoryId, "repo-2"));

        selector.WaitForAssertion(() => Assert.Contains("other", selector.Markup));
        _repositories.Verify(r => r.GetBranches("repo-2", It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void ATagIsOffered()
    {
        // The reported case: a tagged version of ExternData that TortoiseGit could reach and MLQT
        // could not, because the list was branches only.
        var selector = RenderSelector(Branch("main", current: true), Tag("v2.0.0"));

        selector.WaitForAssertion(() => Assert.Contains("v2.0.0", selector.Markup));
    }

    [Fact]
    public void ATagSaysItIsATag()
    {
        // Picking one does something different from picking a branch, so the list has to distinguish
        // them before the choice is made rather than afterwards.
        var selector = RenderSelector(Branch("main", current: true), Tag("v2.0.0"));

        selector.WaitForAssertion(() => Assert.Contains(">Tag<", selector.Markup));
    }

    [Fact]
    public void ABranchIsNotLabelledAsATag()
    {
        var selector = RenderSelector(Branch("main", current: true), Branch("feature/x"));

        // "feature/x" is split into a folder and a child, so the whole path is never one string in
        // the markup - the folder is what says the load finished.
        selector.WaitForAssertion(() => Assert.Contains("feature", selector.Markup));
        Assert.DoesNotContain(">Tag<", selector.Markup);
    }

    [Fact]
    public void TagsAreGroupedUnderOneFolder()
    {
        // Rather than one per release scattered through the branch list, which is what a Git
        // repository with forty tags would otherwise look like. The folder is display only - the
        // value passed on is still the tag's own name.
        var selector = RenderSelector(
            Branch("main", current: true), Tag("v1.0.0"), Tag("v2.0.0"), Tag("v2.1.0"));

        selector.WaitForAssertion(() => Assert.Contains("v2.1.0", selector.Markup));
        Assert.Contains("tags", selector.Markup);
    }

    [Fact]
    public void AnSvnTagPathStillWorksTheOldWay()
    {
        // SVN tags arrive as tags/v1.0 with IsTag unset, and were already grouped by the path
        // splitting. Nothing about them changed, and this says so.
        var selector = RenderSelector(Branch("trunk", current: true), Branch("tags/v1.0"));

        selector.WaitForAssertion(() => Assert.Contains("v1.0", selector.Markup));
        Assert.Contains("tags", selector.Markup);
        Assert.DoesNotContain(">Tag<", selector.Markup);
    }
}

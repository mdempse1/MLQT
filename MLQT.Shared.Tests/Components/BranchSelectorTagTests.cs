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

    private IRenderedComponent<BranchSelector> RenderSelector(params VcsBranchInfo[] refs)
    {
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetBranches("repo-1", It.IsAny<bool>())).Returns(refs.ToList());
        repositories.Setup(r => r.GetRepository("repo-1"))
                    .Returns(new Repository { Id = "repo-1", Name = "ExternData", CurrentBranch = "main" });

        Services.AddSingleton(repositories.Object);
        RenderProviders();

        return Render<BranchSelector>(p => p.Add(c => c.RepositoryId, "repo-1"));
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

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MudBlazor;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// That a reference-only repository is labelled as one, whatever kind of repository it is.
///
/// <para><b>What broke (B200).</b> The "Reference only" chip was nested inside the row that shows
/// the current branch, which renders only for a repository that is not
/// <see cref="RepositoryVcsType.Local"/> <i>and</i> has a <c>CurrentRevision</c>. Every other
/// reference repository in a project therefore appeared unlabelled although the flag was set — the
/// reported symptom was that exactly one of them carried the chip. The chip now sits beside the
/// repository name, which is what it labels.</para>
///
/// <para><b>Verified by mutation.</b> <see cref="ALocalReferenceRepositoryIsLabelled"/> and
/// <see cref="AReferenceRepositoryWithNoRevisionIsLabelled"/> both fail against the previous markup;
/// <see cref="AGitReferenceRepositoryWithARevisionIsStillLabelled"/> passed before and has to keep
/// passing, since that was the one case that worked. The last test covers the restructure rather
/// than the defect: hoisting the chip out turned an <c>if/else</c> into a plain negated
/// <c>if</c>, and the branch controls must still be withheld from a repository nobody is meant to
/// commit to.</para>
/// </summary>
public class LibraryBrowserReferenceOnlyTests : MlqtComponentTestBase
{
    private static Repository Repo(
        RepositoryVcsType vcsType, bool isReferenceOnly, string? revision, string? branch = null) =>
        new()
        {
            Id = "repo-1",
            Name = "ExternData",
            LocalPath = Path.Combine(Path.GetTempPath(), "mlqt-tests", "ExternData"),
            VcsType = vcsType,
            IsReferenceOnly = isReferenceOnly,
            CurrentRevision = revision,
            CurrentBranch = branch
        };

    private IRenderedComponent<LibraryBrowser> RenderBrowser(Repository repository)
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>()))
               .ReturnsAsync(new List<ModelNode>());

        Services.AddSingleton(library.Object);
        Services.AddSingleton(new Mock<IRepositoryService>().Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        RenderProviders();

        // Repository mode, which is the mode the defect was reported in.
        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, repository));
    }

    [Fact]
    public void ALocalReferenceRepositoryIsLabelled()
    {
        // A plain folder added as a reference library: no VCS, so the old markup never reached the
        // chip at all.
        var browser = RenderBrowser(Repo(RepositoryVcsType.Local, isReferenceOnly: true, revision: null));

        Assert.Contains("Reference only", browser.Markup);
    }

    [Fact]
    public void AReferenceRepositoryWithNoRevisionIsLabelled()
    {
        // The second half of the same condition: a Git repository whose revision has not been read
        // yet was also unlabelled.
        var browser = RenderBrowser(Repo(RepositoryVcsType.Git, isReferenceOnly: true, revision: null));

        Assert.Contains("Reference only", browser.Markup);
    }

    [Fact]
    public void AGitReferenceRepositoryWithARevisionIsStillLabelled()
    {
        // The one case that already worked. Kept so the fix is not a swap of which repositories are
        // labelled.
        var browser = RenderBrowser(
            Repo(RepositoryVcsType.Git, isReferenceOnly: true, revision: "abc1234", branch: "main"));

        Assert.Contains("Reference only", browser.Markup);
    }

    [Theory]
    [InlineData(RepositoryVcsType.Local, null)]
    [InlineData(RepositoryVcsType.Git, "abc1234")]
    public void ARepositoryThatIsNotReferenceOnlyIsNotLabelled(RepositoryVcsType vcsType, string? revision)
    {
        var browser = RenderBrowser(Repo(vcsType, isReferenceOnly: false, revision));

        Assert.DoesNotContain("Reference only", browser.Markup);
    }

    // The buttons are identified by their icons, not by their tooltip text: MudTooltip renders its
    // text into a popover that a headless render never opens, so asserting on "Switch branch" passes
    // whether the button is there or not. The icon path is inline in the button's own markup.
    private const string SwitchBranchIcon = Icons.Material.Outlined.CallSplit;
    private const string CreateBranchIcon = Icons.Material.Outlined.ForkRight;

    [Fact]
    public void AReferenceRepositoryIsNotOfferedTheBranchControls()
    {
        // Not the reported defect — the restructure's risk. The chip and the branch buttons used to
        // be the two arms of one if/else, so moving the chip out had to leave the buttons behind a
        // negated condition rather than behind nothing.
        var reference = RenderBrowser(
            Repo(RepositoryVcsType.Git, isReferenceOnly: true, revision: "abc1234", branch: "main"));

        Assert.DoesNotContain(SwitchBranchIcon, reference.Markup);
        Assert.DoesNotContain(CreateBranchIcon, reference.Markup);
    }

    [Fact]
    public void AWorkingRepositoryIsStillOfferedTheBranchControls()
    {
        // The positive control, and it earned its place: the first version of the test above asserted
        // on tooltip text and passed against a build with the buttons rendered.
        var working = RenderBrowser(
            Repo(RepositoryVcsType.Git, isReferenceOnly: false, revision: "abc1234", branch: "main"));

        Assert.Contains(SwitchBranchIcon, working.Markup);
        Assert.Contains(CreateBranchIcon, working.Markup);
    }
}

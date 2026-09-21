using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Comparison;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// That the library browser draws the kind of change a class carries, and can be narrowed to one
/// kind of change (B191).
/// </summary>
/// <remarks>
/// <para>Rendered, because what B191 changed is the markup: the marker used to be written out twice
/// — once per tree — and the point of the change is that both now ask one question.</para>
///
/// <para>Asserted on the chip letters rather than on the tooltips. <c>MudTooltip</c> renders its
/// text into a popover a headless render never opens, so a test that looked for the words would
/// pass whether the marker was there or not.</para>
/// </remarks>
public class LibraryBrowserChangeMarkerTests : MlqtComponentTestBase
{
    /// <summary>
    /// The repository root, and the changed file inside it.
    /// </summary>
    /// <remarks>
    /// <para>Built with <see cref="Path.Combine"/> from the temp directory rather than written out
    /// as <c>C:\repo\MyLib\Components.mo</c>. On Linux a backslash is an ordinary character, so the
    /// hard-coded path made <c>GraphBuilder.GenerateFileId</c> and the browser's own
    /// <c>Path.Combine(VcsRootPath, change.Path)</c> disagree, no models were found in the changed
    /// file, and every assertion here failed — on the runner only (B255's shape).</para>
    ///
    /// <para><c>ChangePath</c> keeps its forward slashes, because that is what Git reports on both
    /// platforms and what the browser is written to normalise.</para>
    /// </remarks>
    private static readonly string RepositoryRoot =
        Path.Combine(Path.GetTempPath(), "mlqt-b191-marker-tests");

    private static readonly string FilePath = Path.Combine(RepositoryRoot, "MyLib", "Components.mo");

    private const string ChangePath = "MyLib/Components.mo";

    private readonly Repository _repository = new()
    {
        Id = "repo-1",
        Name = "MyLib",
        LocalPath = RepositoryRoot,
        VcsRootPath = RepositoryRoot,
        VcsType = RepositoryVcsType.Git,
        CurrentRevision = "abc1234",
        CurrentBranch = "main",
        LibraryIds = { "lib-1" },
    };

    /// <summary>
    /// The working copy's changed files. Mutable, so a test can take them away the way a commit
    /// does and then have the browser re-read them.
    /// </summary>
    private readonly List<VcsWorkingCopyFile> _changes = [];

    private static ModelNode Model(string id) =>
        new(id, id[(id.LastIndexOf('.') + 1)..], "model X end X;") { LibraryId = "lib-1" };

    /// <summary>
    /// A repository whose one file holds the three named classes, all reported modified, with the
    /// given classification.
    /// </summary>
    private IRenderedComponent<LibraryBrowser> RenderBrowser(
        IReadOnlyDictionary<string, ClassChangeKind> kinds, params string[] modelIds)
        => RenderBrowserWithMocks(kinds, modelIds).Browser;

    /// <summary>
    /// The same arrangement, handing back the two stand-ins as well — for the tests that assert on
    /// what was and was not asked of them rather than on what came out.
    /// </summary>
    private (IRenderedComponent<LibraryBrowser> Browser,
             Mock<IRepositoryService> Repositories,
             Mock<IModelChangeClassifier> Classifier) RenderBrowserWithMocks(
        IReadOnlyDictionary<string, ClassChangeKind> kinds, params string[] modelIds)
    {
        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(FilePath);
        graph.AddNode(new FileNode(fileId, FilePath));

        var models = new List<ModelNode>();
        foreach (var id in modelIds)
        {
            var model = Model(id);
            models.Add(model);
            graph.AddNode(model);
            graph.AddFileContainsModel(fileId, id);
        }

        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(graph);
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(models);
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());

        // The tree asks for this on every refresh, and Moq would otherwise hand back null for a
        // reference type - which the interface does not allow and the real service never does.
        library.Setup(l => l.ModelsWithDescendantParserErrors())
               .Returns(new HashSet<string>(StringComparer.Ordinal));

        _changes.Add(new VcsWorkingCopyFile { Path = ChangePath, Status = VcsFileStatus.Modified });
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1")).Returns(_changes);

        var classifier = new Mock<IModelChangeClassifier>();
        classifier
            .Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(kinds);

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(classifier.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        RenderProviders();
        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, _repository));
        return (browser, repositories, classifier);
    }

    /// <summary>
    /// A repository whose changed file holds <c>MyLib.Components.Resistor</c>, nested two deep, so
    /// the pruned tree has packages to bring along with it.
    /// </summary>
    /// <remarks>
    /// The packages come back from <c>GetModelById</c> because that is where the browser climbs
    /// from — containment as the graph records it, not the dotted id split up.
    /// </remarks>
    private IRenderedComponent<LibraryBrowser> RenderNestedBrowser(ClassChangeKind kind) =>
        RenderNestedBrowser(("MyLib.Components.Resistor", kind));

    private IRenderedComponent<LibraryBrowser> RenderNestedBrowser(
        params (string Id, ClassChangeKind Kind)[] changed)
    {
        var library = new List<ModelNode>
        {
            Nested("MyLib", "MyLib", parent: null),
            Nested("MyLib.Components", "Components", parent: "MyLib"),
        };
        library.AddRange(changed.Select(c => Nested(c.Id, c.Id[(c.Id.LastIndexOf('.') + 1)..], "MyLib.Components")));
        var byId = library.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var graph = new DirectedGraph();
        var fileId = GraphBuilder.GenerateFileId(FilePath);
        graph.AddNode(new FileNode(fileId, FilePath));
        foreach (var model in library)
        {
            graph.AddNode(model);
            graph.AddFileContainsModel(fileId, model.Id);
        }

        var libraryService = new Mock<ILibraryDataService>();
        libraryService.SetupGet(l => l.CombinedGraph).Returns(graph);
        libraryService.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync([byId["MyLib"]]);
        libraryService.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());
        libraryService.Setup(l => l.GetModelById(It.IsAny<string>()))
                      .Returns<string>(id => byId.GetValueOrDefault(id));
        libraryService.Setup(l => l.ModelsWithDescendantParserErrors())
                      .Returns(new HashSet<string>(StringComparer.Ordinal));

        _changes.Add(new VcsWorkingCopyFile { Path = ChangePath, Status = VcsFileStatus.Modified });
        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1")).Returns(_changes);

        var classifier = new Mock<IModelChangeClassifier>();
        classifier
            .Setup(c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()))
            .Returns(new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal)
            {
                ["MyLib"] = ClassChangeKind.Unchanged,
                ["MyLib.Components"] = ClassChangeKind.Unchanged,
            }.Concat(changed.Select(c => KeyValuePair.Create(c.Id, c.Kind)))
             .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal));

        Services.AddSingleton(libraryService.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(classifier.Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        RenderProviders();
        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, _repository));
    }

    private static ModelNode Nested(string id, string name, string? parent) =>
        new(id, name, $"package {name} end {name};")
        {
            LibraryId = "lib-1",
            ParentModelName = parent,
            ClassType = parent is null ? "package" : "model",
        };

    private static IReadOnlyDictionary<string, ClassChangeKind> Kinds(
        params (string Id, ClassChangeKind Kind)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Kind, StringComparer.Ordinal);

    /// <summary>
    /// The chip letters the tree is showing, in document order. Scoped to the tree because the
    /// repository header has a chip of its own — the "Reference only" label.
    /// </summary>
    private static string[] Chips(IRenderedComponent<LibraryBrowser> browser) =>
        browser.FindAll(".mud-treeview .mud-chip").Select(e => e.TextContent.Trim()).ToArray();

    /// <summary>
    /// The class names the tree is showing, packages included, in document order.
    /// </summary>
    private static string[] Listed(IRenderedComponent<LibraryBrowser> browser) =>
        browser.FindAll(".mud-treeview-item-content .mud-typography-body1")
               .Select(e => e.TextContent.Trim()).ToArray();

    /// <summary>
    /// Applies a filter and waits for the list it produces.
    /// </summary>
    /// <remarks>
    /// The working copy's status is read on a background thread, so the first render happens before
    /// any of it has arrived. Every assertion here waits.
    /// </remarks>
    private static void Filter(IRenderedComponent<LibraryBrowser> browser, LibraryBrowser.ChangeFilter filter) =>
        browser.WaitForAssertion(() =>
        {
            browser.InvokeAsync(() => browser.Instance.OnChangeFilterChanged(filter));
            Assert.Single(browser.FindAll(".mlqt-change-filter"));
        });

    /// <summary>The filter chips' labels, which carry the count behind each one.</summary>
    private static string[] FilterChips(IRenderedComponent<LibraryBrowser> browser) =>
        browser.FindAll(".mlqt-change-filter .mud-chip").Select(e => e.TextContent.Trim()).ToArray();

    // ---------------------------------------------------------------- the marker

    [Fact]
    public void AChangeThatAffectsSimulationIsMarkedDifferentlyFromACosmeticOne()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic)),
            "MyLib.Resistor", "MyLib.Capacitor");

        browser.WaitForAssertion(() =>
        {
            var chips = Chips(browser);
            Assert.Equal(2, chips.Length);
            Assert.Equal(2, chips.Distinct().Count());
            Assert.Contains("M", chips);
        });
    }

    /// <summary>
    /// The case the feature exists for: a class in a modified file that was not itself touched gets
    /// no marker. Before B191 every class in the file got one.
    /// </summary>
    [Fact]
    public void AnUnchangedClassInAModifiedFileIsNotMarked()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.Unchanged)), "MyLib.Resistor");

        // Waits for the filter control, which appears only once the working copy has been read, so
        // "no chips" cannot be read off a render that has not got there yet.
        browser.WaitForAssertion(() => Assert.Single(browser.FindAll(".mlqt-change-filter")));
        Assert.Empty(Chips(browser));
    }

    /// <summary>
    /// The fallback: a repository whose committed versions could not be read reads as it did before
    /// B191, rather than as unmodified.
    /// </summary>
    [Fact]
    public void AnUnclassifiedChangeStillShowsTheModifiedMarker()
    {
        var browser = RenderBrowser(Kinds(), "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Equal(["M"], Chips(browser)));
    }

    // ---------------------------------------------------------------- the filter

    [Fact]
    public void TheFilterIsOfferedWhenThereAreChanges()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        // The select's own element, not its items: MudSelect renders those into a popover that a
        // headless render never opens.
        browser.WaitForAssertion(() => Assert.Single(browser.FindAll(".mlqt-change-filter")));
    }

    [Fact]
    public void TheFilterIsNotOfferedWhenNothingHasChanged()
    {
        var graph = new DirectedGraph();
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(graph);
        library.Setup(l => l.GetTopLevelModelsAsync()).ReturnsAsync(new List<ModelNode>());
        library.Setup(l => l.GetChildModelsAsync(It.IsAny<ModelNode>())).ReturnsAsync(new List<ModelNode>());

        // The tree asks for this on every refresh, and Moq would otherwise hand back null for a
        // reference type - which the interface does not allow and the real service never does.
        library.Setup(l => l.ModelsWithDescendantParserErrors())
               .Returns(new HashSet<string>(StringComparer.Ordinal));

        var repositories = new Mock<IRepositoryService>();
        repositories.Setup(r => r.GetWorkingCopyChanges("repo-1")).Returns([]);

        Services.AddSingleton(library.Object);
        Services.AddSingleton(repositories.Object);
        Services.AddSingleton(new Mock<IModelChangeClassifier>().Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);

        RenderProviders();
        var browser = Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, _repository));

        Assert.Empty(browser.FindAll(".mlqt-change-filter"));
    }

    [Fact]
    public void FilteringToSimulationChangesShowsOnlyThose()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic),
                  ("MyLib.Inductor", ClassChangeKind.Unchanged)),
            "MyLib.Resistor", "MyLib.Capacitor", "MyLib.Inductor");

        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);

        browser.WaitForAssertion(() => Assert.Equal(["Resistor"], Listed(browser)));
    }

    [Fact]
    public void FilteringToCosmeticChangesShowsOnlyThose()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic)),
            "MyLib.Capacitor", "MyLib.Resistor");

        Filter(browser, LibraryBrowser.ChangeFilter.Cosmetic);

        browser.WaitForAssertion(() => Assert.Equal(["Capacitor"], Listed(browser)));
    }

    /// <summary>
    /// The filter is still a tree: a changed class arrives with the packages that contain it, and
    /// clicking one of those is how the user gets at the rest of it.
    /// </summary>
    [Fact]
    public void AFilteredClassIsShownUnderThePackagesThatContainIt()
    {
        var browser = RenderNestedBrowser(ClassChangeKind.AffectsSimulation);

        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);

        browser.WaitForAssertion(() => Assert.Equal(["MyLib", "Components", "Resistor"], Listed(browser)));
    }

    /// <summary>
    /// Each chip says how many classes are behind it, so "nothing here is cosmetic" is readable
    /// without selecting anything and reading an empty tree.
    /// </summary>
    [Fact]
    public void EachChipCarriesItsCount()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic),
                  ("MyLib.Inductor", ClassChangeKind.Unchanged)),
            "MyLib.Resistor", "MyLib.Capacitor", "MyLib.Inductor");

        browser.WaitForAssertion(() => Assert.Equal(
            ["All", "Changed (2)", "Simulation (1)", "Cosmetic (1)"],
            FilterChips(browser)));
    }

    /// <summary>
    /// Clicking a chip is what applies the filter — the handler being callable is not the same as
    /// the chips being wired to it.
    /// </summary>
    [Fact]
    public void ClickingAChipAppliesItsFilter()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic)),
            "MyLib.Capacitor", "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Equal(4, FilterChips(browser).Length));
        browser.FindAll(".mlqt-change-filter .mud-chip")[3].Click();

        browser.WaitForAssertion(() => Assert.Equal(["Capacitor"], Listed(browser)));
    }

    /// <summary>
    /// A filter that matches nothing says so, rather than leaving an empty pane that reads as
    /// something still loading.
    /// </summary>
    [Fact]
    public void AFilterThatMatchesNothingSaysSo()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        Filter(browser, LibraryBrowser.ChangeFilter.Cosmetic);

        browser.WaitForAssertion(
            () => Assert.Contains("No classes in this repository match that filter", browser.Markup));
    }

    /// <summary>
    /// Committing while a filter is on takes the filter away with the changes it was filtering.
    /// Without this the tree stays hidden behind an empty list, and the control that would bring it
    /// back has just disappeared.
    /// </summary>
    [Fact]
    public void CommittingWhileFilteredBringsTheTreeBack()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");
        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);
        browser.WaitForAssertion(() => Assert.NotEmpty(Listed(browser)));

        // What a commit leaves behind: the working copy is clean, and the browser is told to re-read it.
        _changes.Clear();
        browser.InvokeAsync(() => NavState.VcsFilesChanged("repo-1"));

        browser.WaitForAssertion(() =>
        {
            Assert.Empty(browser.FindAll(".mlqt-change-filter"));
            Assert.Equal(["Resistor"], Listed(browser));
        });
    }

    // ---------------------------------------------------------------- reference-only repositories

    /// <summary>
    /// A repository the user marked reference only is never formatted, checked, committed or
    /// written to, so there is nothing for a change marker or the change filter to be about. It is
    /// also not file-monitored, so anything shown would only refresh on a project load — a stale
    /// marker rather than a useful one.
    /// </summary>
    /// <remarks>
    /// The changed file and its classification are set up exactly as for the tests above, so this
    /// fails against a browser that shows them; <see cref="TheSameRepositoryShowsItAllOnceItIsNoLongerReferenceOnly"/>
    /// is the control that the fixture would otherwise have produced them.
    /// </remarks>
    [Fact]
    public void AReferenceOnlyRepositoryOffersNoChangeFilter()
    {
        _repository.IsReferenceOnly = true;

        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Contains("Reference only", browser.Markup));
        Assert.Empty(browser.FindAll(".mlqt-change-filter"));
    }

    [Fact]
    public void AReferenceOnlyRepositoryMarksNothingAsChanged()
    {
        _repository.IsReferenceOnly = true;

        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Contains("Reference only", browser.Markup));
        Assert.Empty(Chips(browser));
    }

    /// <summary>
    /// Nothing is read from the version control system for one, either. This is the part that is
    /// worth having beyond the markup: a project with several vendor checkouts in it pays a
    /// working-copy query and a committed-version read per changed file for each of them, at
    /// startup and after every refresh, for an answer nothing is allowed to act on.
    /// </summary>
    [Fact]
    public void AReferenceOnlyRepositoryIsNotAskedForItsWorkingCopyAtAll()
    {
        _repository.IsReferenceOnly = true;

        var (browser, repositories, classifier) = RenderBrowserWithMocks(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Contains("Reference only", browser.Markup));
        repositories.Verify(r => r.GetWorkingCopyChanges(It.IsAny<string>()), Times.Never);
        classifier.Verify(
            c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()), Times.Never);
    }

    /// <summary>The control: the same fixture, not reference only, produces all of it.</summary>
    [Fact]
    public void TheSameRepositoryShowsItAllOnceItIsNoLongerReferenceOnly()
    {
        var (browser, repositories, classifier) = RenderBrowserWithMocks(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation)), "MyLib.Resistor");

        browser.WaitForAssertion(() => Assert.Single(browser.FindAll(".mlqt-change-filter")));
        Assert.Equal(["M"], Chips(browser));
        repositories.Verify(r => r.GetWorkingCopyChanges("repo-1"), Times.AtLeastOnce);
        classifier.Verify(
            c => c.Classify(It.IsAny<Repository>(), It.IsAny<IReadOnlyList<VcsWorkingCopyFile>>()), Times.AtLeastOnce);
    }
    /// <summary>
    /// A filter is a question, not a rearrangement: the pruned tree opens where the user had the
    /// full one open and nowhere else.
    /// </summary>
    /// <remarks>
    /// The two views share one expansion record, so a filter that opened everything would also
    /// leave the full tree spread out once it was cleared.
    /// </remarks>
    [Fact]
    public void FilteringDoesNotOpenAnythingTheUserHadClosed()
    {
        var browser = RenderNestedBrowser(ClassChangeKind.AffectsSimulation);
        browser.WaitForAssertion(() => Assert.Single(browser.Instance.ActiveTreeItems));

        // The library is open; the package inside it is not.
        browser.InvokeAsync(() =>
            browser.Instance.OnNodeExpandedChanged(browser.Instance.ActiveTreeItems[0], true));

        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);

        browser.WaitForAssertion(() =>
        {
            var root = Assert.Single(browser.Instance.ActiveTreeItems);
            Assert.True(root.Expanded);
            Assert.All(root.Children!, child => Assert.False(child.Expanded));
        });
    }

    [Fact]
    public void FilteringAClosedTreeLeavesItClosed()
    {
        var browser = RenderNestedBrowser(ClassChangeKind.AffectsSimulation);

        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);

        browser.WaitForAssertion(() =>
            Assert.All(browser.Instance.ActiveTreeItems, item => Assert.False(item.Expanded)));
    }
    /// <summary>
    /// The dot on a package says what is below it <i>in the view being looked at</i>. A package
    /// that also holds a simulation change the Cosmetic filter excluded must not carry that
    /// change's colour there — it would be true of the repository and a contradiction of the
    /// filter.
    /// </summary>
    [Fact]
    public void ThePackageDotFollowsTheFilter()
    {
        var browser = RenderNestedBrowser(
            ("MyLib.Components.Resistor", ClassChangeKind.AffectsSimulation),
            ("MyLib.Components.Capacitor", ClassChangeKind.Cosmetic));

        Filter(browser, LibraryBrowser.ChangeFilter.Cosmetic);

        browser.WaitForAssertion(() => Assert.Equal(
            ChangeMarker.For(null, ClassChangeKind.Unchanged, ClassChangeKind.Cosmetic),
            browser.Instance.MarkerFor(browser.Instance.ActiveTreeItems[0].Value)));
    }

    /// <summary>The control: unfiltered, the same package reports the strongest change under it.</summary>
    [Fact]
    public void ThePackageDotReportsEverythingWhenNothingIsFiltered()
    {
        var browser = RenderNestedBrowser(
            ("MyLib.Components.Resistor", ClassChangeKind.AffectsSimulation),
            ("MyLib.Components.Capacitor", ClassChangeKind.Cosmetic));

        browser.WaitForAssertion(() => Assert.Equal(
            ChangeMarker.For(null, ClassChangeKind.Unchanged, ClassChangeKind.AffectsSimulation),
            browser.Instance.MarkerFor(browser.Instance.ActiveTreeItems[0].Value)));
    }
}

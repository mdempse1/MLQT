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
    private const string FilePath = @"C:\repo\MyLib\Components.mo";

    private readonly Repository _repository = new()
    {
        Id = "repo-1",
        Name = "MyLib",
        LocalPath = @"C:\repo",
        VcsRootPath = @"C:\repo",
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

        _changes.Add(new VcsWorkingCopyFile { Path = @"MyLib\Components.mo", Status = VcsFileStatus.Modified });
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
        return Render<LibraryBrowser>(p => p
            .Add(c => c.LibraryOnly, false)
            .Add(c => c.Repository, _repository));
    }

    private static IReadOnlyDictionary<string, ClassChangeKind> Kinds(
        params (string Id, ClassChangeKind Kind)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Kind, StringComparer.Ordinal);

    /// <summary>The chip letters the tree is showing, in document order.</summary>
    private static string[] Chips(IRenderedComponent<LibraryBrowser> browser) =>
        browser.FindAll(".mud-chip").Select(e => e.TextContent.Trim()).ToArray();

    /// <summary>
    /// The class names the filtered list is showing.
    /// </summary>
    private static string[] Listed(IRenderedComponent<LibraryBrowser> browser) =>
        browser.FindAll(".mud-list-item .mud-typography-caption").Select(e => e.TextContent.Trim()).ToArray();

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
    public void FilteringToSimulationChangesListsOnlyThose()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic),
                  ("MyLib.Inductor", ClassChangeKind.Unchanged)),
            "MyLib.Resistor", "MyLib.Capacitor", "MyLib.Inductor");

        Filter(browser, LibraryBrowser.ChangeFilter.AffectsSimulation);

        browser.WaitForAssertion(() => Assert.Equal(["MyLib.Resistor"], Listed(browser)));
    }

    [Fact]
    public void FilteringToCosmeticChangesListsOnlyThose()
    {
        var browser = RenderBrowser(
            Kinds(("MyLib.Resistor", ClassChangeKind.AffectsSimulation),
                  ("MyLib.Capacitor", ClassChangeKind.Cosmetic)),
            "MyLib.Resistor", "MyLib.Capacitor");

        Filter(browser, LibraryBrowser.ChangeFilter.Cosmetic);

        browser.WaitForAssertion(() => Assert.Equal(["MyLib.Capacitor"], Listed(browser)));
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
            Assert.Empty(Listed(browser));
            Assert.NotEmpty(browser.FindAll(".mud-treeview"));
        });
    }
}

using Bunit;
using DymolaInterface.Interfaces;
using OpenModelicaInterface.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using MLQT.Shared.Pages;
using MLQT.TestSupport;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Moq;
using RevisionControl;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// A rendered Code Review page over one Git repository and a real graph (B377).
/// </summary>
/// <remarks>
/// <para><b>Why a harness at all.</b> The page injects fifteen services and its handlers were covered
/// only through the static helpers pulled out of them, so every click path through it - selecting a
/// class, switching to the diff, a load finishing after the user moved on (B302, B344, B346) - was
/// left for a check by hand. This supplies all fifteen so a test states only what it is about.</para>
///
/// <para><b>What is real and what is not.</b> The graph is real: a test loads Modelica text with
/// <see cref="LoadFile"/> and the page looks classes up in it exactly as it does in the application.
/// <see cref="CodeReviewService"/>, <see cref="AppState"/> and the settings are the real classes (the
/// settings in memory). The rest are Moq doubles, and the ones a test is likely to arrange are
/// exposed: <see cref="Libraries"/>, <see cref="Repositories"/>, <see cref="Baseline"/>. The two
/// external-tool services are concrete classes whose factories are never asked for a session, since
/// nothing here presses a check button.</para>
///
/// <para><b>The repository.</b> One Git repository, <c>repo-1</c>, rooted at <see cref="Root"/>,
/// owning every class in the graph. <see cref="SetHead"/> says what a file held at HEAD and marks it
/// modified in the working copy, which is what makes the diff buttons available; the file itself
/// never has to exist on disk.</para>
///
/// <para><b>Holding a load open.</b> <see cref="HoldHeadReads"/> makes every HEAD read wait on a gate
/// until <see cref="ReleaseHeadRead"/> lets one through - which is how a test puts the page in the
/// state a slow repository leaves it in, and then decides which of two loads lands first.</para>
/// </remarks>
public abstract class CodeReviewTestBase : MlqtComponentTestBase
{
    protected const string RepositoryId = "repo-1";

    /// <summary>The working copy's root. Never created: nothing the page does here reads the disk.</summary>
    protected static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-tests", "CodeReview", "Lib");

    protected DirectedGraph Graph { get; } = new();
    protected Mock<ILibraryDataService> Libraries { get; } = new();
    protected Mock<IRepositoryService> Repositories { get; } = new();
    protected Mock<IBaselineStatusService> Baseline { get; } = new();
    protected CodeReviewService Findings { get; } = new();

    private readonly LoadedLibrary _library = new() { Name = "Lib", RepositoryId = RepositoryId };
    private readonly Dictionary<string, string> _headByRelativePath = new(StringComparer.Ordinal);
    private readonly List<VcsWorkingCopyFile> _changes = [];

    // Gated HEAD reads. Each read takes a ticket; a test releases them one at a time, in any order.
    private readonly object _gateLock = new();
    private bool _holdHeadReads;
    private readonly List<SemaphoreSlim> _waitingReads = [];

    /// <summary>How many times the page has asked the repository for a file at HEAD.</summary>
    protected int HeadReads { get; private set; }

    protected CodeReviewTestBase()
    {
        Libraries.SetupGet(l => l.CombinedGraph).Returns(Graph);
        Libraries.SetupGet(l => l.Libraries).Returns(() => [_library]);
        Libraries.Setup(l => l.GetModelById(It.IsAny<string>()))
            .Returns((string id) => Graph.GetNode<ModelNode>(id));
        Libraries.Setup(l => l.GetOwningLibrary(It.IsAny<string>()))
            .Returns((string id) => Graph.GetNode<ModelNode>(id) is null ? null : _library);

        var repository = new Repository
        {
            Id = RepositoryId,
            Name = "Lib",
            LocalPath = Root,
            VcsRootPath = Root,
            VcsType = RepositoryVcsType.Git,
            CurrentBranch = "main",
            CurrentRevision = "a1b2c3d4e5f6",
        };
        Repositories.Setup(r => r.GetRepository(RepositoryId)).Returns(repository);
        Repositories.Setup(r => r.GetWorkingCopyChanges(RepositoryId)).Returns(() => [.. _changes]);
        Repositories.Setup(r => r.GetFileContentAtRevision(RepositoryId, It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string _, string path, string? _) => ReadHead(path));

        Baseline.Setup(b => b.RefreshAsync()).Returns(Task.CompletedTask);
        Baseline.SetupGet(b => b.Snapshot).Returns(BaselineStatusSnapshot.Empty);

        Services.AddSingleton(Libraries.Object);
        Services.AddSingleton(Repositories.Object);
        Services.AddSingleton(Baseline.Object);
        Services.AddSingleton<ICodeReviewService>(Findings);
        Services.AddSingleton<ISettingsService>(new InMemorySettingsService());
        Services.AddSingleton(new Mock<IFilePickerService>().Object);
        Services.AddSingleton(new Mock<IStyleCheckingService>().Object);
        Services.AddSingleton(new Mock<ICustomDictionaryService>().Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        Services.AddSingleton(new DymolaCheckingService(new Mock<IDymolaInterfaceFactory>().Object));
        Services.AddSingleton(new OpenModelicaCheckingService(new Mock<IOpenModelicaInterfaceFactory>().Object));
    }

    /// <summary>
    /// Loads <paramref name="content"/> into the graph as the working copy of
    /// <paramref name="relativePath"/>, and returns the ids of the classes it holds.
    /// </summary>
    protected IReadOnlyList<string> LoadFile(string relativePath, string content)
    {
        var ids = GraphBuilder.LoadModelicaFile(Graph, FullPath(relativePath), Lf(content));
        foreach (var id in ids)
            _library.ModelIds.Add(id);
        return ids;
    }

    /// <summary>
    /// What <paramref name="relativePath"/> held at HEAD, and that it is modified in the working copy.
    /// Null for a file HEAD does not have, which the repository reports as a new file.
    /// </summary>
    protected void SetHead(string relativePath, string? headContent)
    {
        if (headContent is null)
            _headByRelativePath.Remove(relativePath);
        else
            _headByRelativePath[relativePath] = Lf(headContent);

        _changes.RemoveAll(c => c.Path == relativePath);
        _changes.Add(new VcsWorkingCopyFile
        {
            Path = relativePath,
            Status = headContent is null ? VcsFileStatus.Added : VcsFileStatus.Modified,
        });
    }

    protected static string FullPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    protected static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    /// <summary>From now on, every HEAD read waits until a test releases it.</summary>
    protected void HoldHeadReads()
    {
        lock (_gateLock)
            _holdHeadReads = true;
    }

    /// <summary>Stops holding HEAD reads - those already waiting still need releasing.</summary>
    protected void StopHoldingHeadReads()
    {
        lock (_gateLock)
            _holdHeadReads = false;
    }

    /// <summary>How many HEAD reads are waiting at the gate, released or not.</summary>
    protected int HeldReads
    {
        get { lock (_gateLock) return _waitingReads.Count; }
    }

    /// <summary>
    /// Waits until <paramref name="count"/> HEAD reads are at the gate. Polled rather than asked of
    /// bUnit: a read arrives on a pool thread and renders nothing, and bUnit re-checks a condition
    /// only when something renders - so a wait on it could miss the arrival and never look again.
    /// </summary>
    protected void WaitForHeldReads(int count)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (HeldReads < count)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Expected {count} HEAD reads to be held, but {HeldReads} arrived.");
            Thread.Sleep(10);
        }
        Assert.Equal(count, HeldReads);
    }

    /// <summary>Lets the <paramref name="index"/>th held read (in the order they arrived) finish.</summary>
    protected void ReleaseHeadRead(int index)
    {
        lock (_gateLock)
            _waitingReads[index].Release();
    }

    private string? ReadHead(string relativePath)
    {
        SemaphoreSlim? gate = null;
        lock (_gateLock)
        {
            HeadReads++;
            if (_holdHeadReads)
            {
                gate = new SemaphoreSlim(0);
                _waitingReads.Add(gate);
            }
        }

        // Bounded, so a test that forgets to release fails rather than hanging the run.
        if (gate is not null && !gate.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("A held HEAD read was never released.");

        return _headByRelativePath.GetValueOrDefault(relativePath);
    }

    /// <summary>Renders the page with the providers its dialogs and menus need.</summary>
    protected IRenderedComponent<CodeReview> RenderPage()
    {
        RenderProviders();
        return Render<CodeReview>();
    }

    /// <summary>Selects a class, as the tree does.</summary>
    protected void Select(IRenderedComponent<CodeReview> page, string modelId) =>
        page.InvokeAsync(() => NavState.ChangeModelID(modelId)).GetAwaiter().GetResult();

    // The toolbar's view buttons carry no text or label, only a tooltip bUnit cannot see - so they are
    // found by their icons, which is also what the user goes by. By the icon's path data rather than
    // its whole text: the parser re-serialises the SVG, so the constant is not in InnerHtml verbatim.
    // Every path, not the first: many Material icons open with the same empty 24x24 box, so the first
    // path alone matched the Article button when asked for Compare.
    private static AngleSharp.Dom.IElement ButtonWithIcon(IRenderedComponent<CodeReview> page, string icon)
    {
        var pathData = System.Text.RegularExpressions.Regex.Matches(icon, "d=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        return page.FindAll("button").First(b =>
            b.QuerySelectorAll("path").Select(p => p.GetAttribute("d")).SequenceEqual(pathData));
    }

    protected static AngleSharp.Dom.IElement SingleViewButton(IRenderedComponent<CodeReview> page) =>
        ButtonWithIcon(page, MudBlazor.Icons.Material.Filled.Article);

    protected static AngleSharp.Dom.IElement UnifiedDiffButton(IRenderedComponent<CodeReview> page) =>
        ButtonWithIcon(page, MudBlazor.Icons.Material.Filled.Difference);

    protected static AngleSharp.Dom.IElement SideBySideDiffButton(IRenderedComponent<CodeReview> page) =>
        ButtonWithIcon(page, MudBlazor.Icons.Material.Filled.Compare);

    /// <summary>
    /// How long a test waits for the page to catch up. bUnit's default is one second, and the first
    /// parse in a cold test process can take longer than that on its own - which reads as the page
    /// not doing what it should rather than as slowness.
    /// </summary>
    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    /// <summary>Waits, with <see cref="Patience"/>, until <paramref name="assertion"/> passes.</summary>
    protected static void Eventually(IRenderedComponent<CodeReview> page, Action assertion) =>
        page.WaitForAssertion(assertion, Patience);

    /// <summary>
    /// Waits until the diff buttons are offered, which is when the page has asked the repository
    /// whether the selected class's file is modified and heard that it is.
    /// </summary>
    protected static void WaitForDiffAvailable(IRenderedComponent<CodeReview> page) =>
        Eventually(page, () => Assert.False(UnifiedDiffButton(page).HasAttribute("disabled")));

    /// <summary>The diff viewer on the page now, which must have finished preparing its diff.</summary>
    protected static IRenderedComponent<DiffViewer> ShownDiff(IRenderedComponent<CodeReview> page)
    {
        var viewer = page.FindComponent<DiffViewer>();
        Assert.False(viewer.Instance.IsPreparing);
        return viewer;
    }

    /// <summary>The diff viewer on the page, once the diff it shows has been prepared.</summary>
    protected static IRenderedComponent<DiffViewer> WaitForDiff(IRenderedComponent<CodeReview> page)
    {
        Eventually(page, () => ShownDiff(page));
        return ShownDiff(page);
    }
}

using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// B243 — a package nested inside <em>another class's</em> file, which is the shape that broke the
/// split.
///
/// <para>Reported from the Modelica Standard Library: splitting
/// <c>Modelica.Electrical.Spice3.Internal.Jfet</c> wrote Jfet into the Spice3 directory instead of
/// the Internal one, and then <b>deleted the file it came from</b> — a file holding twenty-two other
/// packages, including Internal itself, which duly vanished from the library browser.</para>
///
/// <para>Two causes, and this file covers both. The rule reported a package that has no file of its
/// own: Jfet is not "stored as a single file", it is written inside the file <c>Spice3</c> owns, and
/// it cannot be given a directory without Internal and Spice3 becoming directories first. And the
/// splitter took the directory of that file as the place to write, then deleted it because what it
/// had written was somewhere else. On MSL the rule raised <b>302 findings over 71 files</b>;
/// reporting only the class that owns the file makes it 65, one per file, and every one of them
/// splittable.</para>
///
/// <para>Nothing covered this before — every fixture had the package owning its file — which is why
/// a full suite passed over a fix that could delete a user's work.</para>
/// </summary>
public class SplitNestedPackageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-split-nested", Guid.NewGuid().ToString("N"));

    public SplitNestedPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Spice3's shape: one file, a package inside it, and a package inside that.</summary>
    private async Task<(LibraryDataService Service, string LibraryPath)> LibraryLikeSpice3()
    {
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(lib);

        File.WriteAllText(Path.Combine(lib, "package.mo"), "package Lib \"A library\"\nend Lib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Outer\n");
        File.WriteAllText(Path.Combine(lib, "Outer.mo"),
            "within Lib;\n"
            + "package Outer \"everything in one file\"\n"
            + "  package Inner \"nested in Outer's file\"\n"
            + "    package Deep \"nested in Inner\"\n"
            + "      model Alpha \"a class\"\n      end Alpha;\n"
            + "    end Deep;\n"
            + "  end Inner;\n"
            + "  model Sibling \"also in Outer's file\"\n  end Sibling;\n"
            + "end Outer;\n");

        var service = new LibraryDataService();
        await service.AddLibraryFromDirectoryAsync(lib);
        return (service, lib);
    }

    private static List<Finding> Findings(DirectedGraph graph)
    {
        var context = new GraphAnalysisContext(graph, new StyleCheckingSettings(), graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(context)
            .Where(f => f.RuleId == RuleIds.SingleFilePackage)
            .ToList();
    }

    [Fact]
    public async Task OnlyTheClassThatOwnsTheFileIsReported()
    {
        // One file, one finding. Reporting Inner and Deep as well says the same thing three times
        // and offers two fixes that cannot be applied.
        var (service, _) = await LibraryLikeSpice3();

        var finding = Assert.Single(Findings(service.CombinedGraph));

        Assert.Equal("Lib.Outer", finding.ModelId);
    }

    [Fact]
    public async Task ANestedPackageCannotBeSplitOnItsOwn()
    {
        // The button is not offered, because the fix would have to move Outer too — and that is the
        // finding on Outer, which is offered.
        var (service, _) = await LibraryLikeSpice3();
        var graph = service.CombinedGraph;

        Assert.False(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Outer.Inner")!));
        Assert.False(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Outer.Inner.Deep")!));
        Assert.True(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Outer")!));
    }

    [Fact]
    public async Task AskingToSplitANestedPackageChangesNothingOnDisk()
    {
        // The assertion that matters: the file it lives in is still there, with everything in it.
        var (service, lib) = await LibraryLikeSpice3();
        var graph = service.CombinedGraph;
        var before = File.ReadAllBytes(Path.Combine(lib, "Outer.mo"));

        var result = PackageSplitter.Split(
            graph, graph.GetNode<ModelNode>("Lib.Outer.Inner.Deep")!, FormattingOptions.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.RemovedFiles);
        Assert.True(File.Exists(Path.Combine(lib, "Outer.mo")));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(lib, "Outer.mo")));
        Assert.False(Directory.Exists(Path.Combine(lib, "Deep")));
    }

    [Fact]
    public async Task SplittingTheOwnerMovesEverythingBelowIt()
    {
        // And the fix that <em>is</em> offered does the whole job: the nested packages become
        // directories of their own, which is the only way any of them gets a file.
        var (service, lib) = await LibraryLikeSpice3();

        var graph = service.CombinedGraph;
        var result = PackageSplitter.Split(
            graph, graph.GetNode<ModelNode>("Lib.Outer")!, FormattingOptions.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(Path.Combine(lib, "Outer", "package.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Outer", "Sibling.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Outer", "Inner", "package.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Outer", "Inner", "Deep", "package.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Outer", "Inner", "Deep", "Alpha.mo")));
        Assert.False(File.Exists(Path.Combine(lib, "Outer.mo")));
    }

    [Fact]
    public async Task TheLibraryStillHoldsTheSameClassesAfterwards()
    {
        var (service, lib) = await LibraryLikeSpice3();
        var before = service.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id).ToList();

        var graph = service.CombinedGraph;
        PackageSplitter.Split(graph, graph.GetNode<ModelNode>("Lib.Outer")!, FormattingOptions.None);

        var reloaded = new LibraryDataService();
        await reloaded.AddLibraryFromDirectoryAsync(lib);

        Assert.Equal(before, reloaded.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task TheGraphIsUpdatedInPlaceToMatchTheDisk()
    {
        // What the user saw after the split was a library browser missing a package. The tree is
        // built from the graph, and the graph is brought up to date in place rather than by
        // reloading the project — so "the same classes on disk" is not enough, the in-memory answer
        // has to agree too.
        var (service, lib) = await LibraryLikeSpice3();
        var graph = service.CombinedGraph;

        var result = PackageSplitter.Split(
            graph, graph.GetNode<ModelNode>("Lib.Outer")!, FormattingOptions.None);
        await service.UpdateChangedFilesAsync(
            [.. result.WrittenFiles, .. result.RemovedFiles], lib);

        var reloaded = new LibraryDataService();
        await reloaded.AddLibraryFromDirectoryAsync(lib);

        Assert.Equal(
            reloaded.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id),
            service.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id));
    }

    [Fact]
    public void TheGuardRefusesToDeleteAFileThatStillHoldsSomething()
    {
        // Defence behind the rule rather than instead of it: the package owns its file, so
        // everything in it should have moved. This is what checks that "should", because the cost of
        // being wrong is somebody else's classes deleted from their working copy.
        var graph = new DirectedGraph();
        graph.AddNode(new FileNode("f", Path.Combine(_root, "Outer.mo")));
        graph.AddNode(new ModelNode("Outer", "Outer", "package Outer\nend Outer;")
        {
            ClassType = "package",
            ContainingFileId = "f",
        });
        graph.AddNode(new ModelNode("Outer.Kept", "Kept", "model Kept end Kept;")
        {
            ClassType = "model",
            ParentModelName = "Outer",
            ContainingFileId = "f",
        });

        var moved = new HashSet<string>(StringComparer.Ordinal) { "Outer" };

        var stranded = PackageSplitter.StillLivingIn(graph, Path.Combine(_root, "Outer.mo"), moved);

        Assert.Equal("Outer.Kept", stranded);
    }

    [Fact]
    public void TheGuardAllowsADeleteWhenEverythingMoved()
    {
        // The control: with the whole file accounted for there is nothing to protect, and a guard
        // that never lets go would stop the fix working at all.
        var graph = new DirectedGraph();
        graph.AddNode(new FileNode("f", Path.Combine(_root, "Outer.mo")));
        graph.AddNode(new ModelNode("Outer", "Outer", "package Outer\nend Outer;")
        {
            ClassType = "package",
            ContainingFileId = "f",
        });

        var moved = new HashSet<string>(StringComparer.Ordinal) { "Outer" };

        Assert.Null(PackageSplitter.StillLivingIn(graph, Path.Combine(_root, "Outer.mo"), moved));
    }
}

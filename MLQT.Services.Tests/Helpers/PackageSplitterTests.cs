using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Visitors;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// B242 — splitting one single-file package into a directory, the fix offered on an
/// <c>MLQT.Structure.SingleFilePackage</c> finding.
///
/// <para>Real files on disk, because the operation writes several and deletes one, and the order of
/// those two matters: the package would be lost if the delete went first and the save then failed,
/// and duplicated if the delete were skipped.</para>
/// </summary>
public class PackageSplitterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-package-split", Guid.NewGuid().ToString("N"));

    public PackageSplitterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A split library, plus one package that arrived as a single file — the shape this exists for.
    /// </summary>
    private async Task<(LibraryDataService Service, string LibraryPath)> LibraryWithASingleFilePackage(
        string packageSource = """
            within Lib;
            package Arrived "saved by another tool as one file"
              model Alpha "first"
                Real a;
              end Alpha;

              model Beta "second"
                Real b;
              end Beta;
            end Arrived;
            """)
    {
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(lib);

        File.WriteAllText(Path.Combine(lib, "package.mo"), "package Lib \"A library\"\nend Lib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Existing\nArrived\n");
        File.WriteAllText(Path.Combine(lib, "Existing.mo"),
            "within Lib;\nmodel Existing \"already one class per file\"\nend Existing;\n");
        File.WriteAllText(Path.Combine(lib, "Arrived.mo"), packageSource);

        var service = new LibraryDataService();
        await service.AddLibraryFromDirectoryAsync(lib);
        return (service, lib);
    }

    private static PackageSplitter.SplitResult Split(LibraryDataService service, string packageId)
    {
        var graph = service.CombinedGraph;
        return PackageSplitter.Split(graph, graph.GetNode<ModelNode>(packageId)!, FormattingOptions.None);
    }

    [Fact]
    public async Task ThePackageBecomesADirectoryWithAFilePerClass()
    {
        var (service, lib) = await LibraryWithASingleFilePackage();

        var result = Split(service, "Lib.Arrived");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(Path.Combine(lib, "Arrived", "package.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Arrived", "Alpha.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Arrived", "Beta.mo")));
    }

    [Fact]
    public async Task TheSingleFileIsGone()
    {
        // Left behind, the library would load Lib.Arrived twice — from the file and from the
        // directory — which is worse than not splitting at all.
        var (service, lib) = await LibraryWithASingleFilePackage();

        var result = Split(service, "Lib.Arrived");

        Assert.False(File.Exists(Path.Combine(lib, "Arrived.mo")));
        Assert.Contains(result.RemovedFiles, f => f.EndsWith("Arrived.mo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheNewDirectoryGetsAPackageOrder()
    {
        var (service, lib) = await LibraryWithASingleFilePackage();

        Split(service, "Lib.Arrived");

        var order = File.ReadAllText(Path.Combine(lib, "Arrived", "package.order"));
        Assert.Contains("Alpha", order);
        Assert.Contains("Beta", order);
    }

    [Fact]
    public async Task NothingElseInTheLibraryIsTouched()
    {
        // The whole point of splitting one package rather than reformatting the repository. The
        // parent's package.order already names Arrived and still should — what changed is where the
        // package is stored, not what it is called.
        var (service, lib) = await LibraryWithASingleFilePackage();

        var existingBefore = File.ReadAllBytes(Path.Combine(lib, "Existing.mo"));
        var packageBefore = File.ReadAllBytes(Path.Combine(lib, "package.mo"));
        var orderBefore = File.ReadAllText(Path.Combine(lib, "package.order"));

        Split(service, "Lib.Arrived");

        Assert.Equal(existingBefore, File.ReadAllBytes(Path.Combine(lib, "Existing.mo")));
        Assert.Equal(packageBefore, File.ReadAllBytes(Path.Combine(lib, "package.mo")));
        Assert.Equal(orderBefore, File.ReadAllText(Path.Combine(lib, "package.order")));
    }

    [Fact]
    public async Task TheSplitLibraryLoadsBackWithTheSameClasses()
    {
        // The result has to be a library, not just a set of files. Reloading from scratch is the
        // only way to find a missing within clause or a package.order that lost a name.
        var (service, lib) = await LibraryWithASingleFilePackage();
        var before = service.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id).ToList();

        Split(service, "Lib.Arrived");

        var reloaded = new LibraryDataService();
        await reloaded.AddLibraryFromDirectoryAsync(lib);
        var after = reloaded.CombinedGraph.ModelNodes.Select(m => m.Id).OrderBy(id => id).ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task TheRuleStopsReportingIt()
    {
        // The fix has to clear the finding that offered it, or the user presses the button and
        // nothing appears to happen.
        var (service, lib) = await LibraryWithASingleFilePackage();

        Split(service, "Lib.Arrived");

        var reloaded = new LibraryDataService();
        await reloaded.AddLibraryFromDirectoryAsync(lib);
        var graph = reloaded.CombinedGraph;

        Assert.False(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Arrived")!));
    }

    [Fact]
    public async Task APackageWithNothingInlineIsRefused()
    {
        // Lib's children each have a file already, so there is nothing to move and the fix is not
        // offered — the same judgement the rule makes before reporting.
        var (service, _) = await LibraryWithASingleFilePackage();
        var graph = service.CombinedGraph;

        var result = PackageSplitter.Split(
            graph, graph.GetNode<ModelNode>("Lib")!, FormattingOptions.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.WrittenFiles);
    }

    [Fact]
    public async Task ADirectoryPackageHoldingItsClassesInlineIsAlsoSplit()
    {
        // A package can be a directory and still hold its classes inline. The rule reports it for
        // the same reason, so the fix has to apply to it — refusing, which this did at first, offers
        // a fix on a finding and then declines to apply it. The package.mo stays where it is and the
        // children get files beside it, so nothing is deleted.
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(Path.Combine(lib, "Sub"));

        File.WriteAllText(Path.Combine(lib, "package.mo"), "package Lib \"A library\"\nend Lib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Sub\n");
        File.WriteAllText(Path.Combine(lib, "Sub", "package.mo"),
            "within Lib;\npackage Sub \"a directory package with inline classes\"\n"
            + "  model Alpha \"first\"\n  end Alpha;\n\n  model Beta \"second\"\n  end Beta;\nend Sub;\n");
        File.WriteAllText(Path.Combine(lib, "Sub", "package.order"), "Alpha\nBeta\n");

        var service = new LibraryDataService();
        await service.AddLibraryFromDirectoryAsync(lib);

        var result = Split(service, "Lib.Sub");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(Path.Combine(lib, "Sub", "Alpha.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Sub", "Beta.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Sub", "package.mo")));
        Assert.Empty(result.RemovedFiles);
    }

    [Fact]
    public async Task IfTheOldFileCannotBeDeleted_TheUserIsToldTheyHaveItTwice()
    {
        // The worst outcome this operation has: the package is written to its new directory and the
        // file it came from is still there, so the library now defines every one of those classes
        // twice. It happens when something else holds the file open — another editor, a virus
        // scanner — and the one thing that must not happen is reporting success.
        var (service, lib) = await LibraryWithASingleFilePackage();
        var arrived = Path.Combine(lib, "Arrived.mo");

        using (File.Open(arrived, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = Split(service, "Lib.Arrived");

            Assert.False(result.Succeeded);
            Assert.Contains("twice", result.Error!);

            // ...and it says so having done the write, not instead of it, so the message describes
            // what is actually on disk.
            Assert.NotEmpty(result.WrittenFiles);
            Assert.True(File.Exists(Path.Combine(lib, "Arrived", "Alpha.mo")));
            Assert.True(File.Exists(arrived));
        }
    }

    [Fact]
    public void APackageWithNoFileIsRefusedRatherThanGuessed()
    {
        // A node with no file behind it cannot be written anywhere. Saying so beats picking a
        // directory and hoping.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;") { ClassType = "package" });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        {
            ClassType = "model",
            ParentModelName = "P",
        });

        var result = PackageSplitter.Split(graph, graph.GetNode<ModelNode>("P")!, FormattingOptions.None);

        Assert.False(result.Succeeded);
        Assert.Contains("which file", result.Error!);
    }

    [Fact]
    public void APackageWhoseFileHasNoDirectoryIsRefused()
    {
        // A bare file name has no directory to write the package's folder into.
        var graph = new DirectedGraph();
        graph.AddNode(new FileNode("f", "P.mo"));
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;")
        {
            ClassType = "package",
            ContainingFileId = "f",
        });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        {
            ClassType = "model",
            ParentModelName = "P",
            ContainingFileId = "f",
        });

        var result = PackageSplitter.Split(graph, graph.GetNode<ModelNode>("P")!, FormattingOptions.None);

        Assert.False(result.Succeeded);
        Assert.Contains("which directory", result.Error!);
    }

    [Fact]
    public async Task APackageWhoseClassesMustStayInlineIsRefused()
    {
        // Same judgement the rule makes: a replaceable class cannot be stored on its own, so there
        // is nothing to move and the button is not offered.
        var (service, lib) = await LibraryWithASingleFilePackage("""
            within Lib;
            package Arrived "cannot be split"
              replaceable model Inner "has to be inline"
              end Inner;
            end Arrived;
            """);

        var result = Split(service, "Lib.Arrived");

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(lib, "Arrived.mo")));
    }

    [Fact]
    public async Task ANestedPackageGoesWithIt()
    {
        // Every class below the package moves, not just its direct children — one left out would be
        // written nowhere and lost with the old file.
        var (service, lib) = await LibraryWithASingleFilePackage("""
            within Lib;
            package Arrived "with a nested package"
              package Deep "nested"
                model Inner "inside the nested package"
                end Inner;
              end Deep;
            end Arrived;
            """);

        var result = Split(service, "Lib.Arrived");

        Assert.True(result.Succeeded, result.Error);
        Assert.True(File.Exists(Path.Combine(lib, "Arrived", "Deep", "package.mo")));
        Assert.True(File.Exists(Path.Combine(lib, "Arrived", "Deep", "Inner.mo")));
    }

    [Fact]
    public async Task CanSplitAgreesWithTheRule()
    {
        // The button is offered exactly where the finding is raised. Asking two different questions
        // would mean a finding with no fix, or a fix that does nothing.
        var (service, _) = await LibraryWithASingleFilePackage();
        var graph = service.CombinedGraph;

        Assert.True(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Arrived")!));
        Assert.False(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib")!));
        Assert.False(PackageSplitter.CanSplit(graph, graph.GetNode<ModelNode>("Lib.Existing")!));
    }
}

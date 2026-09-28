using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// B170 — which file an external tool has to open before it can see a class.
///
/// <para><b>Not the class's own.</b> A class stored at
/// <c>MSL\Modelica\Blocks\Continuous\Integrator.mo</c> is only
/// <c>Modelica.Blocks.Continuous.Integrator</c> because of the packages above it, and a tool handed
/// that file alone sees a class called <c>Integrator</c> with nothing to resolve its <c>within</c>
/// against. OpenModelica refuses to load it; Dymola accepts it and finds the enclosing package
/// itself. That difference is why the same code worked for one tool and not the other.</para>
///
/// <para>These are the half of the fix that can be tested without either tool installed, which is
/// the reason the resolution is a separate thing to resolve rather than two lines inside a service.
/// </para>
/// </summary>
public class LibraryRootFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-root-file", Guid.NewGuid().ToString("N"));

    public LibraryRootFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A library shaped like MSL: a root package, a sub-package, and a class in its own file.</summary>
    private DirectedGraph DirectoryLibrary()
    {
        var graph = new DirectedGraph();

        Directory.CreateDirectory(Path.Combine(_root, "Modelica", "Blocks"));
        var rootPackage = Path.Combine(_root, "Modelica", "package.mo");
        var subPackage = Path.Combine(_root, "Modelica", "Blocks", "package.mo");
        var leaf = Path.Combine(_root, "Modelica", "Blocks", "Integrator.mo");

        File.WriteAllText(rootPackage, "package Modelica \"MSL\"\nend Modelica;\n");
        File.WriteAllText(subPackage, "within Modelica;\npackage Blocks \"b\"\nend Blocks;\n");
        File.WriteAllText(leaf, "within Modelica.Blocks;\nmodel Integrator \"i\"\nend Integrator;\n");

        GraphBuilder.LoadModelicaFiles(graph, [rootPackage, subPackage, leaf]);
        return graph;
    }

    [Fact]
    public void AClassDeepInAPackageResolvesToTheLibrarysOwnPackageFile()
    {
        // The reported case, in the shape it was reported in.
        var graph = DirectoryLibrary();
        var model = graph.GetNode<ModelNode>("Modelica.Blocks.Integrator");
        Assert.NotNull(model);

        var file = LibraryRootFile.For(graph, model!);

        Assert.Equal(Path.Combine(_root, "Modelica", "package.mo"), file);
    }

    [Fact]
    public void ItIsNotTheClassesOwnFile()
    {
        // Stated separately because that is what it used to be, and the difference is the whole fix.
        var graph = DirectoryLibrary();
        var model = graph.GetNode<ModelNode>("Modelica.Blocks.Integrator")!;

        Assert.NotEqual(
            Path.Combine(_root, "Modelica", "Blocks", "Integrator.mo"),
            LibraryRootFile.For(graph, model));
    }

    [Fact]
    public void AnIntermediatePackageResolvesToTheRootToo()
    {
        // Checking a sub-package has the same requirement as checking a class in one.
        var graph = DirectoryLibrary();
        var model = graph.GetNode<ModelNode>("Modelica.Blocks")!;

        Assert.Equal(Path.Combine(_root, "Modelica", "package.mo"), LibraryRootFile.For(graph, model));
    }

    [Fact]
    public void ATopLevelClassIsItsOwnAnswer()
    {
        var graph = DirectoryLibrary();
        var model = graph.GetNode<ModelNode>("Modelica")!;

        Assert.Equal(Path.Combine(_root, "Modelica", "package.mo"), LibraryRootFile.For(graph, model));
    }

    [Fact]
    public void AOneFileLibraryResolvesToThatFile()
    {
        // A library that is a single .mo file has no package.mo to find, and the file it is in is
        // the right answer rather than a failure.
        var graph = new DirectedGraph();
        var path = Path.Combine(_root, "Small.mo");
        File.WriteAllText(path, "package Small \"s\"\n  model M \"m\"\n  end M;\nend Small;\n");
        GraphBuilder.LoadModelicaFile(graph, path, File.ReadAllText(path));

        var model = graph.GetNode<ModelNode>("Small.M")!;

        Assert.Equal(path, LibraryRootFile.For(graph, model));
    }

    [Fact]
    public void AClassWithNoFileAtAllIsNull()
    {
        // A synthesized class — an external stub, say — has no file, and the caller has to be able
        // to tell that apart from "here is a path" rather than being handed something wrong.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Ghost", "Ghost", "model Ghost end Ghost;"));

        Assert.Null(LibraryRootFile.For(graph, graph.GetNode<ModelNode>("Ghost")!));
    }
}

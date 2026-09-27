using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// B445 — the text of a file that is outside every class: a licence header above <c>within</c>, a
/// comment between the clause and the class, and one after the class's final <c>end X;</c>.
///
/// <para>Every path that rebuilds a file from a class's stored source used to drop it, because the
/// stored source is the class's own span and nothing else. It is now carried on the class that heads
/// the file (<see cref="ModelNode.FileText"/>) and written back by <see cref="WithinClause"/>. What
/// the user decided: it stays with that class — a split puts it at the top of the new
/// <c>package.mo</c> and the new per-class files get none.</para>
/// </summary>
public class FileHeaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-file-header", Guid.NewGuid().ToString("N"));

    public FileHeaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private const string ModelFile =
        "// Copyright header\n// Licensed to everyone\n\nwithin Lib;\nmodel M \"m\"\n  Real x;\nend M;\n";

    private const string PackageFile =
        "/* Library header\n   second line */\nwithin;\npackage Lib \"a library\"\nend Lib;\n";

    private string WriteLibrary(string modelFile = ModelFile)
    {
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), PackageFile);
        File.WriteAllText(Path.Combine(lib, "package.order"), "M\n");
        File.WriteAllText(Path.Combine(lib, "M.mo"), modelFile);
        return lib;
    }

    private static DirectedGraph Load(string lib)
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaDirectory(graph, lib);
        return graph;
    }

    private static string Read(string path) => ModelicaFileEncoding.ReadAllTextOnly(path).Replace("\r\n", "\n");

    private SaveResult FormatAll(DirectedGraph graph, StyleCheckingSettings? settings = null)
        => ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, graph.ModelNodes.Select(m => m.Id).ToHashSet(), _root, false, FormattingOptions.None, settings);

    [Fact]
    public void FormatAll_KeepsTheHeaderAboveTheWithinClause()
    {
        var lib = WriteLibrary();
        var graph = Load(lib);

        FormatAll(graph);

        Assert.StartsWith("// Copyright header\n// Licensed to everyone\nwithin Lib;\nmodel M", Read(Path.Combine(lib, "M.mo")));
        Assert.StartsWith("/* Library header\n   second line */\nwithin;\npackage Lib", Read(Path.Combine(lib, "package.mo")));
    }

    [Fact]
    public void FormatAll_WritesTheHeaderExactlyAsTheIncrementalFormatterDoes()
    {
        // The two formatters must agree, or a file one of them wrote reads as modified to the other
        // (B236). The incremental one renders the file from disk, header and all.
        var lib = WriteLibrary();
        var graph = Load(lib);
        var incremental = ModelicaFileEncoding.EnsureFinalNewline(
            ModelicaPackageSaver.RenderFileSource(ModelFile, "Lib", FormattingOptions.None));
        var incrementalPackage = ModelicaFileEncoding.EnsureFinalNewline(
            ModelicaPackageSaver.RenderFileSource(PackageFile, null, FormattingOptions.None));

        FormatAll(graph);

        Assert.Equal(incremental, Read(Path.Combine(lib, "M.mo")));
        Assert.Equal(incrementalPackage, Read(Path.Combine(lib, "package.mo")));
    }

    [Fact]
    public void FormatAll_TwiceWritesTheSameFile()
    {
        var lib = WriteLibrary();
        var graph = Load(lib);

        FormatAll(graph);
        var first = Read(Path.Combine(lib, "M.mo"));
        FormatAll(graph);

        Assert.Equal(first, Read(Path.Combine(lib, "M.mo")));
    }

    [Fact]
    public void FormatAll_DoesNotPutTheHeaderIntoTheStoredSource()
    {
        // A class's stored source never carries file-level text: it is what every line number is
        // counted from, and a header in it would be written twice by the next save.
        var lib = WriteLibrary();
        var graph = Load(lib);

        FormatAll(graph);

        Assert.DoesNotContain("Copyright", graph.GetNode<ModelNode>("Lib.M")!.Definition.ModelicaCode);
        Assert.DoesNotContain("Library header", graph.GetNode<ModelNode>("Lib")!.Definition.ModelicaCode);
    }

    [Fact]
    public void FormatAll_WritesAClassExcludedFromFormattingWithItsHeaderAsItWas()
    {
        var lib = WriteLibrary();
        var graph = Load(lib);

        FormatAll(graph, new StyleCheckingSettings { ApplyFormattingRules = true, FormattingExcludedModels = ["Lib.M"] });

        Assert.Equal(ModelFile, Read(Path.Combine(lib, "M.mo")));
        Assert.DoesNotContain("Copyright", graph.GetNode<ModelNode>("Lib.M")!.Definition.ModelicaCode);
    }

    [Fact]
    public void Split_PutsTheHeaderAtTheTopOfTheNewPackageFile_AndGivesTheNewFilesNone()
    {
        var lib = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "within;\npackage Lib\nend Lib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Arrived\n");
        File.WriteAllText(Path.Combine(lib, "Arrived.mo"),
            "// Copyright header\nwithin Lib;\npackage Arrived \"one file\"\n  model Alpha\n    Real a;\n  end Alpha;\n\n  model Beta\n    Real b;\n  end Beta;\nend Arrived;\n");
        var graph = Load(lib);

        var result = PackageSplitter.Split(graph, graph.GetNode<ModelNode>("Lib.Arrived")!, FormattingOptions.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("// Copyright header\nwithin Lib;\npackage Arrived", Read(Path.Combine(lib, "Arrived", "package.mo")));
        Assert.DoesNotContain("Copyright", Read(Path.Combine(lib, "Arrived", "Alpha.mo")));
        Assert.DoesNotContain("Copyright", Read(Path.Combine(lib, "Arrived", "Beta.mo")));
    }

    [Fact]
    public void Split_ASingleFileLibrary_PutsItsHeaderAtTheTopOfTheNewPackageFile()
    {
        var file = Path.Combine(_root, "Lib.mo");
        File.WriteAllText(file,
            "// Copyright header\nwithin;\npackage Lib\n  model Alpha\n    Real a;\n  end Alpha;\nend Lib;\n");
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, file, File.ReadAllText(file));

        var result = PackageSplitter.Split(graph, graph.GetNode<ModelNode>("Lib")!, FormattingOptions.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("// Copyright header\nwithin;\npackage Lib", Read(Path.Combine(_root, "Lib", "package.mo")));
        Assert.DoesNotContain("Copyright", Read(Path.Combine(_root, "Lib", "Alpha.mo")));
    }

    [Fact]
    public void RenderFileOwnerModel_KeepsTheHeader()
    {
        var lib = WriteLibrary();
        var graph = Load(lib);

        var rendered = ModelicaPackageSaver.RenderFileOwnerModel(graph.GetNode<ModelNode>("Lib.M")!, FormattingOptions.None);

        Assert.Equal(
            ModelicaPackageSaver.RenderFileSource(ModelFile, "Lib", FormattingOptions.None),
            rendered);
    }

    [Fact]
    public void RenderFileOwnerModel_KeepsCommentsAfterTheClauseAndAfterTheClass()
    {
        // The grammar does not accept either position yet (B430), so the file loads with a syntax
        // error - but a writer must not be the thing that quietly removes them.
        var lib = WriteLibrary("within Lib; // after\nmodel M \"m\"\n  Real x;\nend M; // trailer\n");
        var graph = Load(lib);

        var rendered = ModelicaPackageSaver.RenderFileOwnerModel(graph.GetNode<ModelNode>("Lib.M")!, FormattingOptions.None);

        Assert.Equal("within Lib;\n// after\nmodel M \"m\"\n  Real x;\nend M;\n// trailer", rendered);
    }

    [Fact]
    public void RenderFileOwnerModel_OfAWholeFile_DoesNotWriteTheHeaderTwice()
    {
        // format_class hands it the file as it is on disk, header included.
        var lib = WriteLibrary();
        var graph = Load(lib);
        var owner = graph.GetNode<ModelNode>("Lib.M")!;
        owner.Definition.ModelicaCode = ModelFile;

        var rendered = ModelicaPackageSaver.RenderFileOwnerModel(owner, FormattingOptions.None);

        Assert.Single(rendered.Split("Copyright").Skip(1));
    }
}

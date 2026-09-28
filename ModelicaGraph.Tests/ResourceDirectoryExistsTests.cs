using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That a resource reference resolving to a directory that is really there is not reported missing
/// (B208).
///
/// <para><c>loadResource</c> may name a directory — <c>modelica://ModelicaTest/Resources/Data</c> in
/// the Modelica Standard Library does — and nothing in the reference says which it is. It was assumed
/// to be a file and tested with <c>File.Exists</c>, so a directory sitting on disk was reported as a
/// missing resource. It was one of four entries in MSL's missing list, two of which were wrong.</para>
/// </summary>
public class ResourceDirectoryExistsTests : IDisposable
{
    private readonly string _root;

    public ResourceDirectoryExistsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b208-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "MyLib", "Resources", "Data"));
        File.WriteAllText(Path.Combine(_root, "MyLib", "Resources", "Data", "table.mat"), "data");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private async Task<DirectedGraph> GraphReferencing(string uri)
    {
        var library = Path.Combine(_root, "MyLib");
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(library, "Uses.mo"), $$"""
            within MyLib;
            model Uses
              parameter String p = Modelica.Utilities.Files.loadResource("{{uri}}");
            end Uses;
            """);

        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("MyLib", library)]);
        return graph;
    }

    [Fact]
    public async Task ADirectoryThatExistsIsNotMissing()
    {
        var graph = await GraphReferencing("modelica://MyLib/Resources/Data");

        Assert.DoesNotContain(graph.ResourceFileNodes, n => !n.FileExists);
        var directory = Assert.Single(graph.ResourceDirectoryNodes,
            n => n.ResolvedPath.EndsWith("Data", StringComparison.OrdinalIgnoreCase));
        Assert.True(directory.DirectoryExists);
    }

    [Fact]
    public async Task AFileThatExistsIsStillAFile()
    {
        // The control: this must not turn every reference into a directory.
        var graph = await GraphReferencing("modelica://MyLib/Resources/Data/table.mat");

        var file = Assert.Single(graph.ResourceFileNodes);
        Assert.True(file.FileExists);
    }

    [Fact]
    public async Task APathThatIsNotThereIsStillReportedMissing()
    {
        // The case the reporting exists for. A reference nobody can satisfy stays a missing file.
        var graph = await GraphReferencing("modelica://MyLib/Resources/Data/absent.mat");

        var file = Assert.Single(graph.ResourceFileNodes);
        Assert.False(file.FileExists);
    }
}

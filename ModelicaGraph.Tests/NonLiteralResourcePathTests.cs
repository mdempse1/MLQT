using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That a resource path MLQT cannot know is not reported as a missing file (B210).
///
/// <para>Reported from Claytex as
/// <c>Resource file not found: ...\Bushes\Components\{fileName_1_xr,fileName_1_yr,fileName_1_zr}</c> —
/// the text of an array expression, treated as a relative path and resolved against the class's own
/// directory. A modification's value was taken as raw text, so anything bound to a file-valued
/// parameter became a path.</para>
///
/// <para>An array, a reference to another parameter, a concatenation or a function call has a value
/// only when the model is translated. MLQT can neither resolve it nor say it is absent, so it records
/// nothing rather than something false.</para>
/// </summary>
public class NonLiteralResourcePathTests : IDisposable
{
    private readonly string _root;

    public NonLiteralResourcePathTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b210-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "MyLib"));
        File.WriteAllText(Path.Combine(_root, "MyLib", "real.txt"), "data");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A library with a table component whose fileName parameter carries a loadSelector, and a user
    /// of it whose modification binds <paramref name="binding"/>.
    /// </summary>
    private async Task<DirectedGraph> GraphWhereFileNameIsBoundTo(string binding)
    {
        var library = Path.Combine(_root, "MyLib");
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(library, "Table.mo"), """
            within MyLib;
            model Table
              parameter String fileName = "NoName"
                "File where the table is stored"
                annotation (Dialog(loadSelector(filter="Text files (*.txt)", caption="Open")));
            end Table;
            """);

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(library, "Uses.mo"), $$"""
            within MyLib;
            model Uses
              parameter String other = "elsewhere.txt";
              Table t(fileName={{binding}});
            end Uses;
            """);

        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("MyLib", library)]);
        return graph;
    }

    private static IEnumerable<string> ResourceNames(DirectedGraph graph) =>
        graph.ResourceFileNodes.Select(n => Path.GetFileName(n.ResolvedPath));

    [Theory]
    [InlineData("{fileName_a,fileName_b,fileName_c}")]   // the reported shape: an array of parameters
    [InlineData("other")]                                // another parameter
    [InlineData("other + \".txt\"")]                     // a concatenation
    [InlineData("{\"a.txt\",\"b.txt\"}")]                // an array *of literals* is still not a path
    public async Task AValueThatIsNotAStringLiteralIsNotAResource(string binding)
    {
        var graph = await GraphWhereFileNameIsBoundTo(binding);

        Assert.Empty(graph.ResourceFileNodes);
    }

    [Fact]
    public async Task AStringLiteralIsStillAResource()
    {
        // The control. Silencing what cannot be resolved must not silence what can.
        var graph = await GraphWhereFileNameIsBoundTo("\"real.txt\"");

        Assert.Contains("real.txt", ResourceNames(graph));
    }

    [Fact]
    public async Task AStringLiteralThatIsAbsentIsStillReported()
    {
        // The case the reporting exists for.
        var graph = await GraphWhereFileNameIsBoundTo("\"absent.txt\"");

        var node = Assert.Single(graph.ResourceFileNodes);
        Assert.Equal("absent.txt", Path.GetFileName(node.ResolvedPath));
        Assert.False(node.FileExists);
    }

    [Fact]
    public async Task ThePlaceholderDefaultIsStillIgnored()
    {
        // "NoName" was already skipped and has to stay skipped.
        var graph = await GraphWhereFileNameIsBoundTo("\"NoName\"");

        Assert.Empty(graph.ResourceFileNodes);
    }

    /// <summary>
    /// A model calling loadResource with <paramref name="argument"/>.
    /// </summary>
    private async Task<DirectedGraph> GraphCallingLoadResourceWith(string argument)
    {
        var library = Path.Combine(_root, "MyLib");
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(library, "Caller.mo"), $$"""
            within MyLib;
            function Caller
              input String packageName;
              output String file;
            algorithm
              file := Modelica.Utilities.Files.loadResource({{argument}});
            end Caller;
            """);

        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("MyLib", library)]);
        return graph;
    }

    [Fact]
    public async Task AComposedLoadResourceArgumentIsNotAResource()
    {
        // The second half of B210, found in Claytex's Functions.Files.packageFile:
        //     loadResource("modelica://" + packageName + "/package.mo")
        // Every literal inside the call used to be captured, so this produced two resources and the
        // second - "/package.mo" - was reported as a missing file.
        var graph = await GraphCallingLoadResourceWith(
            "\"modelica://\" + packageName + \"/package.mo\"");

        Assert.Empty(graph.ResourceFileNodes);
        Assert.Empty(graph.ResourceDirectoryNodes);
    }

    [Fact]
    public async Task ALiteralLoadResourceArgumentIsStillAResource()
    {
        // The control.
        var graph = await GraphCallingLoadResourceWith("\"modelica://MyLib/real.txt\"");

        Assert.Contains("real.txt", ResourceNames(graph));
    }

    [Fact]
    public async Task ALiteralIsCapturedOnlyOnce()
    {
        // The capture clears itself, so a literal that appears once yields one resource rather than
        // one per primary the walk happens to pass.
        var graph = await GraphCallingLoadResourceWith("\"modelica://MyLib/real.txt\"");

        Assert.Single(graph.ResourceFileNodes);
    }
}

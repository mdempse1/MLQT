using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That an <c>#include</c> is looked for in every declared <c>IncludeDirectory</c> (B211).
///
/// <para>Reported from <c>VeSyMA.Roads.Functions.Internal.readNormal</c>, which declares</para>
/// <code>
/// Include={"#include \"speedProfile.c\"","#include \"generic_road.c\"","#include \"ModelicaTables.c\""},
/// IncludeDirectory={"modelica://VeSyMA/Resources/Include","modelica://Claytex/Resources/Include"}
/// </code>
/// <para>Both are arrays, and the resolution loop assigned each directory to a single variable, so
/// only the last survived. Every header in the class was then looked for in Claytex's directory
/// alone, and <c>generic_road.c</c> — which is in VeSyMA's — was reported missing from a directory
/// it was never meant to be in.</para>
///
/// <para>It had a second symptom worth knowing, because it looks like a different bug: the file
/// <i>did</i> appear in the tree, under VeSyMA, put there by the directory scan, and said no model
/// referenced it. The reference had gone to a separate node under the wrong directory. Resolving to
/// the right directory reunites the two, because the node is keyed by path.</para>
/// </summary>
public class MultipleIncludeDirectoryTests : IDisposable
{
    private readonly string _root;

    public MultipleIncludeDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b211-" + Guid.NewGuid().ToString("N"));

        // Two libraries, each with an Include directory; the header is only in the first.
        Directory.CreateDirectory(Path.Combine(_root, "FirstLib", "Resources", "Include"));
        Directory.CreateDirectory(Path.Combine(_root, "SecondLib", "Resources", "Include"));
        File.WriteAllText(
            Path.Combine(_root, "FirstLib", "Resources", "Include", "generic_road.c"), "/* code */");
        File.WriteAllText(
            Path.Combine(_root, "SecondLib", "Resources", "Include", "other.c"), "/* code */");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private async Task<DirectedGraph> GraphFor(string includeDirectories, string header = "generic_road.c")
    {
        var library = Path.Combine(_root, "FirstLib");
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(library, "Reader.mo"), $$"""
            within FirstLib;
            function Reader
              input Real s;
              output Real y;
              external "C" y = read(s) annotation (
                Include="#include \"{{header}}\"",
                IncludeDirectory={{includeDirectories}});
            end Reader;
            """);

        await GraphBuilder.AnalyzeDependenciesAsync(graph,
        [
            new LibraryInfo("FirstLib", library),
            new LibraryInfo("SecondLib", Path.Combine(_root, "SecondLib"))
        ]);
        return graph;
    }

    /// <summary>
    /// Every node for a file of this name. There can be more than one, and that is the defect: the
    /// directory scan creates a node for the header where it really is, and the unresolved #include
    /// creates a second one, missing, under the directory it was wrongly looked for in. Matching one
    /// by name and asserting on it finds whichever comes first and proves nothing.
    /// </summary>
    private static List<ResourceFileNode> HeaderNodes(DirectedGraph graph, string name) =>
        graph.ResourceFileNodes
            .Where(n => string.Equals(Path.GetFileName(n.ResolvedPath), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

    [Fact]
    public async Task AHeaderIsFoundInTheFirstDirectoryThatHoldsIt()
    {
        var graph = await GraphFor(
            """{"modelica://FirstLib/Resources/Include","modelica://SecondLib/Resources/Include"}""");

        var node = Assert.Single(HeaderNodes(graph, "generic_road.c"));
        Assert.True(node.FileExists);
        Assert.Contains("FirstLib", node.ResolvedPath);
    }

    [Fact]
    public async Task AndAlsoWhenThatDirectoryIsListedSecond()
    {
        // The other ordering, which the previous code got right by accident: it kept the last
        // directory, and here that is the one holding the header. Kept as a control - the fix must
        // work for both orders, not swap which one is broken.
        var graph = await GraphFor(
            """{"modelica://SecondLib/Resources/Include","modelica://FirstLib/Resources/Include"}""");

        var node = Assert.Single(HeaderNodes(graph, "generic_road.c"));
        Assert.True(node.FileExists);
        Assert.Contains("FirstLib", node.ResolvedPath);
    }

    [Fact]
    public async Task TheModelIsRecordedAsReferencingTheHeader()
    {
        // The second symptom: the file showed in the tree with no model against it, because the
        // reference had been attached to a different node under the wrong directory. Written in the
        // order that fails - the directory holding the header first - so that the assertion is about
        // the fix and not about an ordering that happened to work.
        var graph = await GraphFor(
            """{"modelica://FirstLib/Resources/Include","modelica://SecondLib/Resources/Include"}""");

        var node = Assert.Single(HeaderNodes(graph, "generic_road.c"));
        Assert.NotEmpty(graph.GetIncomingNodes(node.Id));
    }

    [Fact]
    public async Task AHeaderInNoneOfThemIsStillReportedMissing()
    {
        // The case the reporting exists for. Which of the directories names it is arbitrary; it is
        // absent from all of them.
        var graph = await GraphFor(
            """{"modelica://FirstLib/Resources/Include","modelica://SecondLib/Resources/Include"}""",
            header: "absent.c");

        var node = Assert.Single(HeaderNodes(graph, "absent.c"));
        Assert.False(node.FileExists);
    }

    [Fact]
    public async Task ASingleIncludeDirectoryStillWorks()
    {
        // The control: the ordinary one-directory case is unchanged.
        var graph = await GraphFor("\"modelica://FirstLib/Resources/Include\"");

        var node = Assert.Single(HeaderNodes(graph, "generic_road.c"));
        Assert.True(node.FileExists);
    }
}

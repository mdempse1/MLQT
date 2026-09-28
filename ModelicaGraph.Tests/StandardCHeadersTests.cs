using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That a C standard-library header named by an external function is not reported as a missing file
/// (B172).
///
/// <para><b>What broke.</b> An <c>Include</c> annotation is resolved against the library's
/// <c>Resources/Include</c> directory, and the delimiter the directive used was discarded. So
/// <c>#include &lt;stdio.h&gt;</c> resolved to <c>&lt;library&gt;/Resources/Include/stdio.h</c>,
/// which is never there, and every external function that touches the C standard library reported a
/// missing resource with nothing wrong with the library.</para>
///
/// <para><b>The delimiter carries the rule.</b> C already separates the two cases — <c>"foo.h"</c> is
/// the project's own, <c>&lt;foo.h&gt;</c> is on the compiler's search path — so this needs no list of
/// platform include directories and is not platform-specific. The name list in
/// <see cref="StandardCHeaders"/> is a second line for libraries that write <c>#include "math.h"</c>.
/// </para>
/// </summary>
public class StandardCHeadersTests
{
    [Theory]
    [InlineData("stdio.h")]
    [InlineData("stdlib.h")]
    [InlineData("math.h")]
    [InlineData("string.h")]
    [InlineData("sys/types.h")]
    public void AStandardHeaderIsSupplied_EvenInQuotes(string header)
    {
        Assert.True(StandardCHeaders.IsSupplied(header, isSystemInclude: false));
    }

    [Fact]
    public void AnyBracketedHeaderIsSupplied_WhateverItIsCalled()
    {
        // The rule that does the real work: the program has said this is on the compiler's search
        // path, and MLQT does not know that path, so its absence here means nothing.
        Assert.True(StandardCHeaders.IsSupplied("SomeVendorSdk.h", isSystemInclude: true));
    }

    [Theory]
    [InlineData("ModelicaStandardTables.h")]
    [InlineData("ExternalMedia.h")]
    public void ALibrarysOwnHeaderIsNotSupplied(string header)
    {
        // The other direction, and the one that matters: a header the library ships must still be
        // reported when it is missing.
        Assert.False(StandardCHeaders.IsSupplied(header, isSystemInclude: false));
    }

    [Fact]
    public void ASeparatorWrittenTheWindowsWayStillMatches()
    {
        Assert.True(StandardCHeaders.IsSupplied(@"sys\types.h", isSystemInclude: false));
    }
}

/// <summary>
/// The same rule where it is actually applied: the resource graph built for a model whose external
/// function includes a standard header.
/// </summary>
public class StandardHeaderResourceTests : IDisposable
{
    private readonly string _root;

    public StandardHeaderResourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b172-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "MyLib", "Resources", "Include"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string LibraryRoot => Path.Combine(_root, "MyLib");

    private async Task<DirectedGraph> BuildGraphFor(string includeAnnotation)
    {
        var graph = new DirectedGraph();
        var source = $$"""
            within MyLib;
            function Compute
              input Real x;
              output Real y;
              external "C" y = compute(x)
                annotation({{includeAnnotation}});
            end Compute;
            """;

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(LibraryRoot, "Compute.mo"), source);
        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("MyLib", LibraryRoot)]);
        return graph;
    }

    private static IEnumerable<string> ResourceFileNames(DirectedGraph graph) =>
        graph.ResourceFileNodes.Select(n => Path.GetFileName(n.ResolvedPath));

    [Fact]
    public async Task ABracketedStandardHeaderGetsNoResourceNode()
    {
        var graph = await BuildGraphFor("""Include="#include <stdio.h>" """.Trim());

        Assert.DoesNotContain("stdio.h", ResourceFileNames(graph));
    }

    [Fact]
    public async Task AQuotedStandardHeaderGetsNoResourceNode()
    {
        var graph = await BuildGraphFor("""Include="#include \"math.h\"" """.Trim());

        Assert.DoesNotContain("math.h", ResourceFileNames(graph));
    }

    [Fact]
    public async Task ALibrarysOwnMissingHeaderIsStillTracked()
    {
        // The control. Silencing standard headers must not silence the case the tracking exists for.
        var graph = await BuildGraphFor("""Include="#include \"MyLibExternal.h\"" """.Trim());

        var node = Assert.Single(graph.ResourceFileNodes);
        Assert.Equal("MyLibExternal.h", Path.GetFileName(node.ResolvedPath));
        Assert.False(node.FileExists);
    }

    [Fact]
    public async Task AHeaderTheLibraryReallyShipsIsTracked_EvenInAngleBrackets()
    {
        // IncludeDirectory behaves like a -I path, so a bracketed include can legitimately resolve
        // inside the library. The file being there is what settles it, which is why existence is
        // checked before the name is judged.
        File.WriteAllText(
            Path.Combine(LibraryRoot, "Resources", "Include", "MyLibExternal.h"), "/* header */");

        var graph = await BuildGraphFor("""Include="#include <MyLibExternal.h>" """.Trim());

        var node = Assert.Single(graph.ResourceFileNodes);
        Assert.Equal("MyLibExternal.h", Path.GetFileName(node.ResolvedPath));
        Assert.True(node.FileExists);
    }
}

using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// That a resource resolves against the copy of the library the referencing model is in (B169).
///
/// <para><b>Library names are not unique across loaded libraries.</b> A commercial library is
/// routinely present twice: the encrypted build a tool ships, and the source the team has checked
/// out. Both register under the same name — the encrypted one drops its version suffix, so
/// "Claytex 2026.1" registers as "Claytex" — and both carry a <c>Resources/</c> directory. Resolution
/// took the first match by name, so it picked whichever loaded first and a model's own resources were
/// attached to the other copy's directory.</para>
///
/// <para>Nine libraries collide this way in the setup it was reported from: a Dymola installation's
/// library folder alongside the same libraries as source.</para>
/// </summary>
public class DuplicateLibraryNameResourceTests : IDisposable
{
    private readonly string _root;

    public DuplicateLibraryNameResourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b169-" + Guid.NewGuid().ToString("N"));
        CreateCopy("Shipped");
        CreateCopy("Source");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A library called "SharedName" under its own root, each with a Resources/Data/table.mat, so
    /// both copies can satisfy the same modelica:// URI and only the referencing file tells them
    /// apart.
    /// </summary>
    private void CreateCopy(string which)
    {
        var library = Path.Combine(_root, which, "SharedName");
        Directory.CreateDirectory(Path.Combine(library, "Resources", "Data"));
        File.WriteAllText(Path.Combine(library, "Resources", "Data", "table.mat"), which);
    }

    private string LibraryRoot(string which) => Path.Combine(_root, which, "SharedName");

    /// <summary>
    /// Loads a model into the given copy and resolves its resource, with the library list in the
    /// order given — which is what used to decide the answer.
    /// </summary>
    private async Task<string?> ResolvedPathFor(string modelIn, params string[] libraryOrder)
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(LibraryRoot(modelIn), "Uses.mo"), """
            within SharedName;
            model Uses
              parameter String p = Modelica.Utilities.Files.loadResource("modelica://SharedName/Resources/Data/table.mat");
            end Uses;
            """);

        await GraphBuilder.AnalyzeDependenciesAsync(
            graph, libraryOrder.Select(w => new LibraryInfo("SharedName", LibraryRoot(w))).ToList());

        return graph.ResourceFileNodes.SingleOrDefault()?.ResolvedPath;
    }

    [Theory]
    [InlineData("Shipped", "Source")]
    [InlineData("Source", "Shipped")]
    public async Task AModelResolvesAgainstItsOwnCopy_WhicheverLoadedFirst(string first, string second)
    {
        // The defect: the answer used to be `first`, whatever copy the model was actually in.
        var resolved = await ResolvedPathFor("Source", first, second);

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Source"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Shipped", "Source")]
    [InlineData("Source", "Shipped")]
    public async Task AndSoDoesAModelInTheOtherCopy(string first, string second)
    {
        var resolved = await ResolvedPathFor("Shipped", first, second);

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Shipped"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OneCopyStillResolvesNormally()
    {
        // The control: nothing about the ordinary single-library case changes.
        var resolved = await ResolvedPathFor("Source", "Source");

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Source"), resolved, StringComparison.OrdinalIgnoreCase);
    }
}

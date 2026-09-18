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

    /// <summary>
    /// A second library, whose models reference SharedName's resources from outside either copy of
    /// it — which is what most references actually look like.
    /// </summary>
    private void CreateReferencingLibrary()
    {
        var library = Path.Combine(_root, "Other", "Referencing");
        Directory.CreateDirectory(library);
    }

    private async Task<string?> ResolvedPathForACrossLibraryReference(bool shippedIsEncrypted)
    {
        CreateReferencingLibrary();
        var referencing = Path.Combine(_root, "Other", "Referencing");
        var graph = new DirectedGraph();

        GraphBuilder.LoadModelicaFile(graph, Path.Combine(referencing, "Uses.mo"), """
            within Referencing;
            model Uses
              parameter String p = Modelica.Utilities.Files.loadResource("modelica://SharedName/Resources/Data/table.mat");
            end Uses;
            """);

        // The encrypted copy first, so a first-match rule would choose it.
        await GraphBuilder.AnalyzeDependenciesAsync(graph,
        [
            new LibraryInfo("SharedName", LibraryRoot("Shipped"), isEncrypted: shippedIsEncrypted),
            new LibraryInfo("SharedName", LibraryRoot("Source")),
            new LibraryInfo("Referencing", referencing)
        ]);

        return graph.ResourceFileNodes.SingleOrDefault()?.ResolvedPath;
    }

    [Fact]
    public async Task AReferenceFromAnotherLibraryResolvesToTheReadableCopy()
    {
        // The case the first attempt at this missed, and the one most references fall into: the
        // referencing model sits inside neither copy, so the file cannot separate them. An encrypted
        // library cannot be the answer while a readable one exists - nothing can read its code, so
        // nothing knows what it references, and every reference naming it was written elsewhere.
        var resolved = await ResolvedPathForACrossLibraryReference(shippedIsEncrypted: true);

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Source"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithNeitherCopyEncryptedTheFirstIsStillTaken()
    {
        // Two readable copies of one name is a genuinely ambiguous setup and nothing here can rank
        // them, so the previous behaviour is kept rather than a preference invented.
        var resolved = await ResolvedPathForACrossLibraryReference(shippedIsEncrypted: false);

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Shipped"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheReferencingFileStillWinsOverTheReadablePreference()
    {
        // Order matters between the two rules: a model inside the encrypted copy - which only
        // happens if that copy has readable files after all - resolves against its own copy rather
        // than being sent to the other one.
        var resolved = await ResolvedPathFor("Shipped", "Shipped", "Source");

        Assert.NotNull(resolved);
        Assert.StartsWith(LibraryRoot("Shipped"), resolved, StringComparison.OrdinalIgnoreCase);
    }
}

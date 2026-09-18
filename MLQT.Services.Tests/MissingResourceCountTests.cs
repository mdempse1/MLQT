using MLQT.Services;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// What the External Resources tab's "Missing" chip is counting, and whether it is the same thing
/// the tree shows when that chip is clicked.
///
/// <para><b>Reported:</b> the chip reads "Missing (5)" and selecting it empties the tree. Before
/// B172 it read 7 and showed 2. Both numbers fell by the two standard C headers B172 stopped
/// reporting, so the gap of five was there all along and is nothing to do with that fix.</para>
///
/// <para><b>The two numbers come from different collections.</b> The chip counts <c>GetWarnings()</c>
/// entries of type <see cref="ResourceWarningType.MissingFile"/>, and a warning is generated per
/// <i>reference</i> — once for every model that mentions the resource, with no de-duplication. The
/// tree is built from <c>GetAllResources()</c> and keyed by resolved path, so the same missing file
/// mentioned by six models is six warnings and one node. The chip is therefore counting mentions
/// while its label, and the filter it drives, are about files.</para>
/// </summary>
public class MissingResourceCountTests : IDisposable
{
    private readonly string _root;
    private readonly ExternalResourceService _service = new();

    public MissingResourceCountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A graph in which <paramref name="modelCount"/> models all reference the same missing file —
    /// the ordinary case for a library whose models share a data file or a header.
    /// </summary>
    private DirectedGraph ModelsSharingOneMissingFile(int modelCount)
    {
        var graph = new DirectedGraph();
        var missing = Path.Combine(_root, "Resources", "nonexistent.mat");

        for (var i = 1; i <= modelCount; i++)
        {
            var modelId = $"TestLib.Model{i}";
            graph.AddNode(new ModelNode(modelId, $"Model{i}"));

            var node = graph.GetOrCreateResourceFileNode(missing);
            graph.AddModelReferencesResource(modelId, node.Id, new ResourceEdge
            {
                RawPath = "modelica://TestLib/Resources/nonexistent.mat",
                ReferenceType = ResourceReferenceType.LoadResource
            });
        }

        return graph;
    }

    private static int DistinctMissingPaths(ExternalResourceService service) =>
        service.GetAllResources()
               .Where(r => !r.FileExists && !string.IsNullOrEmpty(r.ResolvedPath))
               .Select(r => r.ResolvedPath!)
               .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
               .Count();

    [Fact]
    public async Task OneMissingFileSharedBySixModels_IsOneMissingFile()
    {
        // The tree will show one node. That is what the chip should be counting.
        await _service.AnalyzeResourcesAsync(ModelsSharingOneMissingFile(6));

        Assert.Equal(1, DistinctMissingPaths(_service));
    }

    [Fact]
    public async Task ButTheWarningListHasOnePerModelThatMentionsIt()
    {
        // The chip's current source. Six mentions of one absent file read as six missing files.
        await _service.AnalyzeResourcesAsync(ModelsSharingOneMissingFile(6));

        var missingWarnings = _service.GetWarnings()
            .Count(w => w.WarningType == ResourceWarningType.MissingFile);

        Assert.Equal(6, missingWarnings);
    }

    [Fact]
    public async Task TheChipAndTheTreeDisagreeWheneverAResourceIsSharedAtAll()
    {
        // Stated as the relationship rather than as two numbers, because the size of the gap is just
        // however many models happen to share a missing resource.
        await _service.AnalyzeResourcesAsync(ModelsSharingOneMissingFile(6));

        var warnings = _service.GetWarnings().Count(w => w.WarningType == ResourceWarningType.MissingFile);

        Assert.True(warnings > DistinctMissingPaths(_service),
            "expected the warning count to exceed the number of missing files, which is the reported defect");
    }

    [Fact]
    public async Task ASingleReferenceAgreesWithItself()
    {
        // The case that made this look right for as long as nobody shared a resource.
        await _service.AnalyzeResourcesAsync(ModelsSharingOneMissingFile(1));

        var warnings = _service.GetWarnings().Count(w => w.WarningType == ResourceWarningType.MissingFile);

        Assert.Equal(DistinctMissingPaths(_service), warnings);
    }
}

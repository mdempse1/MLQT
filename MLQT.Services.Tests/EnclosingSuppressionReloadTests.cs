using MLQT.Services;
using MLQT.Services.Checking;
using ModelicaGraph;
using ModelicaParser.StyleRules;

namespace MLQT.Services.Tests;

/// <summary>
/// A package's class-level <c>__MLQT</c> waivers reach every class nested in it, including the ones
/// in their own files. A reload that changes them has to hand those classes back as affected, or the
/// desktop app goes on showing the findings the old waivers let through - or hiding the ones they no
/// longer cover - until the next full check (B499).
/// </summary>
public class EnclosingSuppressionReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mlqt-waiver-{Guid.NewGuid():N}");
    private readonly string _lib;

    private const string Unwaived = "package P \"The package\"\nend P;\n";
    private const string Waived = "package P \"The package\"\n  annotation(__MLQT(suppress=\"Doc.ClassDescription\"));\nend P;\n";

    public EnclosingSuppressionReloadTests()
    {
        _lib = Path.Combine(_root, "P");
        Directory.CreateDirectory(Path.Combine(_lib, "Sub"));
        // Neither child has a description, so each carries a ClassDescription finding unless waived.
        File.WriteAllText(Path.Combine(_lib, "Child.mo"), "within P;\nmodel Child\nend Child;\n");
        File.WriteAllText(Path.Combine(_lib, "Sub", "package.mo"), "within P;\npackage Sub \"Below\"\nend Sub;\n");
        File.WriteAllText(Path.Combine(_lib, "Sub", "Deep.mo"), "within P.Sub;\nmodel Deep\nend Deep;\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string PackageFile => Path.Combine(_lib, "package.mo");

    private async Task<LibraryDataService> LoadAsync(string package)
    {
        File.WriteAllText(PackageFile, package);
        var service = new LibraryDataService();
        await service.AddLibraryFromDirectoryAsync(_lib);
        return service;
    }

    private static HashSet<string> Undescribed(LibraryDataService service, IEnumerable<string> modelIds)
    {
        var graph = service.CombinedGraph;
        return LibraryCheckSession.Check(
                graph, modelIds.Select(id => graph.GetNode<ModelicaGraph.DataTypes.ModelNode>(id)!).ToList(),
                new StyleCheckingSettings { ClassHasDescription = true },
                new CustomDictionaryService(), new DictionaryManagerService())
            .Where(f => f.RuleId == RuleIds.ClassDescription)
            .Select(f => f.ModelId)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddingAWaiver_ReturnsTheClassesBelowInFilesOfTheirOwn()
    {
        var service = await LoadAsync(Unwaived);
        Assert.Contains("P.Child", Undescribed(service, ["P.Child", "P.Sub.Deep"]));   // the positive control

        File.WriteAllText(PackageFile, Waived);
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Sub", affected);
        Assert.Contains("P.Sub.Deep", affected);
        // What MainLayout does with the answer: check them again, and the waiver now holds.
        Assert.Empty(Undescribed(service, affected));
    }

    [Fact]
    public async Task RemovingAWaiver_ReturnsTheClassesBelowInFilesOfTheirOwn()
    {
        var service = await LoadAsync(Waived);
        Assert.Empty(Undescribed(service, ["P.Child", "P.Sub.Deep"]));

        File.WriteAllText(PackageFile, Unwaived);
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Sub.Deep", affected);
        Assert.Equal(["P.Child", "P.Sub.Deep"], Undescribed(service, affected).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASpellingWordAdded_ReturnsTheClassesBelow()
    {
        var service = await LoadAsync(Unwaived);

        File.WriteAllText(PackageFile, "package P \"The package\"\n  annotation(__MLQT(spelling=\"Hx\"));\nend P;\n");
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Sub.Deep", affected);
    }

    [Fact]
    public async Task TheBatchUpdate_WidensTheSameWay()
    {
        // UpdateChangedFilesAsync is Refresh's and Split into files' path; it goes through
        // GraphBuilder.UpdateGraphForChangedFiles rather than ReloadFileAsync.
        var service = await LoadAsync(Unwaived);

        File.WriteAllText(PackageFile, Waived);
        var affected = await service.UpdateChangedFilesAsync([PackageFile], _root);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Sub.Deep", affected);
    }

    [Fact]
    public async Task AnEditThatLeavesTheWaiversAlone_DoesNotWiden()
    {
        // Widening on every package edit would re-check a whole library for a changed description.
        var service = await LoadAsync(Waived);

        File.WriteAllText(PackageFile,
            "package P \"Now described differently\"\n  annotation(__MLQT(suppress=\"Doc.ClassDescription\", reason=\"why\"));\nend P;\n");
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P", affected);
        Assert.DoesNotContain("P.Child", affected);
        Assert.DoesNotContain("P.Sub.Deep", affected);
    }

    [Fact]
    public async Task AWaiverOnANestedPackage_ReturnsOnlyWhatItEncloses()
    {
        var service = await LoadAsync(Unwaived);
        var subPackage = Path.Combine(_lib, "Sub", "package.mo");

        File.WriteAllText(subPackage,
            "within P;\npackage Sub \"Below\"\n  annotation(__MLQT(suppress=\"*\"));\nend Sub;\n");
        var affected = await service.ReloadFileAsync(subPackage);

        Assert.Contains("P.Sub.Deep", affected);
        Assert.DoesNotContain("P.Child", affected);
    }
}

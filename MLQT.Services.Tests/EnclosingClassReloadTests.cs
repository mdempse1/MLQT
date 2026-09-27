using MLQT.Services;
using ModelicaGraph;

namespace MLQT.Services.Tests;

/// <summary>
/// The classes a package declares are part of what names mean in every class below it, as its
/// imports are (B347): a simple name is looked for in each enclosing scope. A reload that adds or
/// removes one has to hand back the classes in other files that name it, or every incremental path
/// leaves them resolving the name the old way until a full re-analysis (B387).
/// </summary>
public class EnclosingClassReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mlqt-enclosing-class-{Guid.NewGuid():N}");
    private readonly string _lib;

    private const string Without = "package P\nend P;\n";
    private const string With = "package P\n  type Voltage = Real;\nend P;\n";

    public EnclosingClassReloadTests()
    {
        _lib = Path.Combine(_root, "P");
        Directory.CreateDirectory(_lib);
        // Voltage means P.Voltage only while P declares one.
        File.WriteAllText(Path.Combine(_lib, "Child.mo"), "within P;\nmodel Child\n  Voltage v;\nend Child;\n");
        // Names nothing P declares, so no change to P's classes can alter what it means.
        File.WriteAllText(Path.Combine(_lib, "Other.mo"), "within P;\nmodel Other\n  Real x;\nend Other;\n");
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
        await service.EnsureDependenciesAnalyzedAsync();
        return service;
    }

    private static List<string> Uses(LibraryDataService service, string id) =>
        service.CombinedGraph.GetUsedModels(id).Select(m => m.Id).ToList();

    [Fact]
    public async Task AddingAClassToThePackage_ReanalysesAChildInItsOwnFileThatNamesIt()
    {
        var service = await LoadAsync(Without);
        Assert.DoesNotContain("P.Voltage", Uses(service, "P.Child"));   // the positive control

        File.WriteAllText(PackageFile, With);
        var affected = await service.ReloadFileAsync(PackageFile);
        await service.RefreshDependenciesAsync(affected);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Voltage", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task RemovingAClassFromThePackage_ReanalysesAChildInItsOwnFileThatNamedIt()
    {
        var service = await LoadAsync(With);
        Assert.Contains("P.Voltage", Uses(service, "P.Child"));

        File.WriteAllText(PackageFile, Without);
        var affected = await service.ReloadFileAsync(PackageFile);
        await service.RefreshDependenciesAsync(affected);

        Assert.Contains("P.Child", affected);
        Assert.DoesNotContain("P.Voltage", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task ANewFileInThePackage_ReachesItsSiblingsThatNameIt_ThroughTheBatchUpdate()
    {
        // UpdateChangedFilesAsync is the VCS pipeline's and Refresh's path. A class added as a file
        // of its own changes the package as much as one added to package.mo does, and a pull brings
        // new files far more often than it edits package.mo.
        var service = await LoadAsync(Without);

        var voltageFile = Path.Combine(_lib, "Voltage.mo");
        File.WriteAllText(voltageFile, "within P;\ntype Voltage = Real;\n");
        var affected = await service.UpdateChangedFilesAsync([voltageFile], _root);
        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            service.CombinedGraph, affected, service.GetLibraryInfos());
        service.CombinedGraph.ReconcileDependencyEdges();

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Voltage", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task AClassNobodyBelowNames_WidensToNobody()
    {
        // Widening to the whole package on every added class would re-check a subtree for each new
        // file. Only a class whose text names the new one can have looked it up.
        var service = await LoadAsync(Without);

        File.WriteAllText(PackageFile, With);
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P.Voltage", affected);
        Assert.DoesNotContain("P.Other", affected);
    }

    [Fact]
    public async Task AnEditThatAddsAndRemovesNothing_DoesNotWiden()
    {
        var service = await LoadAsync(With);

        File.WriteAllText(PackageFile, "package P \"Now described\"\n  type Voltage = Real;\nend P;\n");
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P", affected);
        Assert.DoesNotContain("P.Child", affected);
    }

    [Fact]
    public async Task ANameMatchedInsideALongerIdentifier_DoesNotCount()
    {
        // `HighVoltage` and `Voltage_x` are not references to `Voltage`: the match is on whole
        // identifiers, and each of the two is bounded on one side only.
        File.WriteAllText(Path.Combine(_lib, "Other.mo"),
            "within P;\nmodel Other\n  Real HighVoltage;\n  Real Voltage_x;\nend Other;\n");
        var service = await LoadAsync(Without);

        File.WriteAllText(PackageFile, With);
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P.Child", affected);
        Assert.DoesNotContain("P.Other", affected);
    }
}

using MLQT.Services;
using ModelicaGraph;

namespace MLQT.Services.Tests;

/// <summary>
/// A package's imports are part of what names mean in every class below it, including the ones in
/// their own files (B292). A reload that changes them has to hand those classes back as affected, or
/// every incremental path - the VCS pipeline, Refresh, a Code Review edit - leaves them with the edges
/// and findings of the old imports (B347).
/// </summary>
public class EnclosingImportReloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mlqt-enclosing-{Guid.NewGuid():N}");
    private readonly string _lib;

    private const string WithoutImport = "package P\nend P;\n";
    private const string WithImport = "package P\n  import T = P.Types;\nend P;\n";

    public EnclosingImportReloadTests()
    {
        _lib = Path.Combine(_root, "P");
        Directory.CreateDirectory(_lib);
        File.WriteAllText(Path.Combine(_lib, "Types.mo"), "within P;\npackage Types\n  type Time = Real;\nend Types;\n");
        // The child writes T.Time, which means P.Types.Time only while P imports T.
        File.WriteAllText(Path.Combine(_lib, "Child.mo"), "within P;\nmodel Child\n  T.Time t;\nend Child;\n");
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
    public async Task AddingAnImport_ReanalysesAChildInItsOwnFile()
    {
        var service = await LoadAsync(WithoutImport);
        Assert.DoesNotContain("P.Types.Time", Uses(service, "P.Child"));   // the positive control

        File.WriteAllText(PackageFile, WithImport);
        var affected = await service.ReloadFileAsync(PackageFile);
        await service.RefreshDependenciesAsync(affected);

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Types.Time", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task RemovingAnImport_ReanalysesAChildInItsOwnFile()
    {
        var service = await LoadAsync(WithImport);
        Assert.Contains("P.Types.Time", Uses(service, "P.Child"));

        File.WriteAllText(PackageFile, WithoutImport);
        var affected = await service.ReloadFileAsync(PackageFile);
        await service.RefreshDependenciesAsync(affected);

        Assert.Contains("P.Child", affected);
        Assert.DoesNotContain("P.Types.Time", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task TheBatchUpdate_WidensTheSameWay()
    {
        // UpdateChangedFilesAsync is the VCS pipeline's and Refresh's path; it goes through
        // GraphBuilder.UpdateGraphForChangedFiles rather than ReloadFileAsync.
        var service = await LoadAsync(WithoutImport);

        File.WriteAllText(PackageFile, WithImport);
        var affected = await service.UpdateChangedFilesAsync([PackageFile], _root);
        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            service.CombinedGraph, affected, service.GetLibraryInfos());
        service.CombinedGraph.ReconcileDependencyEdges();

        Assert.Contains("P.Child", affected);
        Assert.Contains("P.Types.Time", Uses(service, "P.Child"));
    }

    [Fact]
    public async Task AnEditThatLeavesTheImportsAlone_DoesNotWiden()
    {
        // Widening on every package edit would re-check a whole subtree for a changed description.
        var service = await LoadAsync(WithImport);

        File.WriteAllText(PackageFile, "package P \"Now described\"\n  import T = P.Types;\nend P;\n");
        var affected = await service.ReloadFileAsync(PackageFile);

        Assert.Contains("P", affected);
        Assert.DoesNotContain("P.Child", affected);
        Assert.DoesNotContain("P.Types", affected);
    }
}

using MLQT.Services.Checking;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// B428 — a library that is one <c>.mo</c> file is rooted at the directory holding it, whatever its
/// source type says.
/// </summary>
/// <remarks>
/// <para>A library found in a repository has its source type overwritten with Git or SVN whatever
/// shape it has on disk, so asking <c>SourceType == File</c> took a single-file repository library's
/// <c>.mo</c> file as its root: <c>modelica://MyLib/Resources/x.png</c> resolved to
/// <c>.../MyLib.mo/Resources/x.png</c>, in resource analysis, icon bitmaps and exported finding
/// paths alike. B306's shape, and the same answer: ask the path.</para>
/// </remarks>
public class SingleFileLibraryRootTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlqt-single-file-root", Guid.NewGuid().ToString("N"));

    public SingleFileLibraryRootTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(LibrarySourceType.File)]
    [InlineData(LibrarySourceType.Git)]
    [InlineData(LibrarySourceType.SVN)]
    public void ASingleFileLibrary_IsRootedAtItsDirectory_WhateverItsSourceType(LibrarySourceType type)
    {
        var library = new LoadedLibrary { SourceType = type, SourcePath = Path.Combine(_dir, "MyLib.mo") };

        Assert.True(library.IsSingleFile);
        Assert.Equal(_dir, library.RootDirectory);
    }

    [Theory]
    [InlineData(LibrarySourceType.Directory)]
    [InlineData(LibrarySourceType.Git)]
    [InlineData(LibrarySourceType.SVN)]
    public void ADirectoryLibrary_IsRootedAtItself(LibrarySourceType type)
    {
        var library = new LoadedLibrary { SourceType = type, SourcePath = Path.Combine(_dir, "MyLib") };

        Assert.False(library.IsSingleFile);
        Assert.Equal(Path.Combine(_dir, "MyLib"), library.RootDirectory);
    }

    [Fact]
    public void ADirectoryWhoseNameEndsInMo_IsADirectory()
    {
        // Asked of the disk where the name alone cannot say: a package directory may be called
        // anything, and one called "X.mo" is still the library's root.
        var path = Path.Combine(_dir, "Odd.mo");
        Directory.CreateDirectory(path);
        var library = new LoadedLibrary { SourceType = LibrarySourceType.Git, SourcePath = path };

        Assert.False(library.IsSingleFile);
        Assert.Equal(path, library.RootDirectory);
    }

    [Fact]
    public async Task GetLibraryInfos_RootsARepositorySingleFileLibraryAtItsDirectory()
    {
        var (service, _) = await LoadRepositorySingleFileLibrary();

        var info = Assert.Single(service.GetLibraryInfos());

        Assert.Equal(_dir, info.RootPath);
    }

    [Fact]
    public async Task AnIconBitmap_InARepositorySingleFileLibrary_ResolvesBesideTheFile()
    {
        var (service, _) = await LoadRepositorySingleFileLibrary();

        var lib = (await service.GetTopLevelModelsAsync()).Single(m => m.Id == "MyLib");

        Assert.Contains("data:image/png;base64,", lib.IconSvg);
    }

    [Fact]
    public async Task AFindingExport_RootsARepositorySingleFileLibraryAtItsDirectory()
    {
        var (service, _) = await LoadRepositorySingleFileLibrary();

        var roots = FindingExport.LibraryRootsByModel(service.Libraries);

        Assert.Equal(_dir, roots["MyLib"]);
    }

    /// <summary>
    /// A single-file library with a bitmap icon and the image beside it, loaded and then given the
    /// source type a repository gives it.
    /// </summary>
    private async Task<(LibraryDataService Service, LoadedLibrary Library)> LoadRepositorySingleFileLibrary()
    {
        var resources = Path.Combine(_dir, "Resources");
        Directory.CreateDirectory(resources);
        // A 1x1 PNG: only its existence and extension matter to the resolver.
        await File.WriteAllBytesAsync(Path.Combine(resources, "x.png"), Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));

        var file = Path.Combine(_dir, "MyLib.mo");
        var code = """
            package MyLib "a single-file library with a bitmap icon"
              annotation (Icon(graphics={Bitmap(extent={{-100,-100},{100,100}}, fileName="modelica://MyLib/Resources/x.png")}));
            end MyLib;
            """.Replace("\r\n", "\n");
        await File.WriteAllTextAsync(file, code);

        var service = new LibraryDataService();
        var library = await service.AddLibraryFromFileAsync(file, code);
        // What RepositoryService does to every library it finds in a working copy.
        library.SourceType = LibrarySourceType.Git;
        return (service, library);
    }
}

using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

public class LibraryDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-disc-" + Guid.NewGuid().ToString("N"));

    public LibraryDiscoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void Write(string relativePath, string content = "x")
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void RootWithPackageMo_IsSingleLibrary()
    {
        Write("package.mo");
        Write("A.mo");
        Write("Sub/package.mo"); // sub-package is part of the root library, not separate
        Assert.Equal(new[] { _root }, LibraryDiscovery.DiscoverLibraryPaths(_root));
    }

    [Fact]
    public void SubdirectoriesWithPackageMo_AreEachALibrary()
    {
        Write("LibA/package.mo");
        Write("LibB/package.mo");
        var libs = LibraryDiscovery.DiscoverLibraryPaths(_root);
        Assert.Equal(2, libs.Count);
        Assert.Contains(Path.Combine(_root, "LibA"), libs);
        Assert.Contains(Path.Combine(_root, "LibB"), libs);
    }

    [Fact]
    public void LooseTopLevelMoFiles_AreDiscovered_WhenNoRootPackage()
    {
        Write("A.mo");
        Write("B.mo");
        Assert.Equal(2, LibraryDiscovery.DiscoverLibraryPaths(_root).Count);
    }

    [Fact]
    public void HiddenDirectories_AreSkipped()
    {
        Write(".git/package.mo");
        Write("LibA/package.mo");
        Assert.Equal(new[] { Path.Combine(_root, "LibA") }, LibraryDiscovery.DiscoverLibraryPaths(_root));
    }

    [Fact]
    public void SingleMoFilePath_ReturnsThatFile()
    {
        Write("Standalone.mo");
        var path = Path.Combine(_root, "Standalone.mo");
        Assert.Equal(new[] { path }, LibraryDiscovery.DiscoverLibraryPaths(path));
    }

    [Fact]
    public void NonexistentPath_ReturnsEmpty()
        => Assert.Empty(LibraryDiscovery.DiscoverLibraryPaths(Path.Combine(_root, "nope")));
}

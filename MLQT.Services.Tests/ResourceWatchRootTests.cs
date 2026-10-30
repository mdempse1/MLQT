using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// One watcher per library resource tree, not one per referenced directory.
/// </summary>
/// <remarks>
/// <para>A <see cref="FileSystemWatcher"/> costs one inotify <i>instance</i> on Linux, against a
/// default per-user limit of 128 — so the watcher-per-directory version could not monitor the Modelica
/// Standard Library at all (265 failures, 84 watchers created) and starved the rest of the user's
/// session, which is how it stopped the MCP server from starting (B163). Windows has no comparable
/// limit, so nothing before the host migration would have shown this.</para>
///
/// <para>These tests are therefore about <b>how many watchers exist</b>, not about whether events
/// arrive: the filtering downstream is unchanged, because
/// <c>OnResourceFileSystemEvent</c> has always decided on the reverse index rather than on the
/// directory the event came through.</para>
/// </remarks>
public class ResourceWatchRootTests : IDisposable
{
    private readonly ExternalResourceService _service = new();
    private readonly string _tempDir;

    public ResourceWatchRootTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ExtResWatch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void ADirectoryUnderResources_CollapsesToTheResourcesTree()
    {
        var dir = Path.Combine("/libs", "MyLib", "Resources", "Images", "Fluid");

        var (root, recursive) = ExternalResourceService.WatchRootFor(dir);

        Assert.Equal(Path.GetFullPath(Path.Combine("/libs", "MyLib", "Resources")), root);
        Assert.True(recursive);
    }

    [Fact]
    public void TheResourcesDirectoryItself_IsItsOwnRootAndStillRecursive()
    {
        // It has to be recursive even when it is the directory that was asked about: the next referenced
        // directory below it must collapse onto this same watcher rather than add another.
        var dir = Path.Combine("/libs", "MyLib", "Resources");

        var (root, recursive) = ExternalResourceService.WatchRootFor(dir);

        Assert.Equal(Path.GetFullPath(dir), root);
        Assert.True(recursive);
    }

    [Fact]
    public void NestedResourcesDirectories_CollapseToTheOutermost()
    {
        var dir = Path.Combine("/libs", "MyLib", "Resources", "Data", "Resources", "Inner");

        var (root, _) = ExternalResourceService.WatchRootFor(dir);

        Assert.Equal(Path.GetFullPath(Path.Combine("/libs", "MyLib", "Resources")), root);
    }

    [Fact]
    public void ADirectoryOutsideAnyResourcesTree_KeepsTheNarrowWatch()
    {
        // Nothing bounds what its parent holds, so this one is watched exactly as before: itself, and
        // not recursively.
        var dir = Path.Combine("/data", "shared", "tables");

        var (root, recursive) = ExternalResourceService.WatchRootFor(dir);

        Assert.Equal(Path.GetFullPath(dir), root);
        Assert.False(recursive);
    }

    [Fact]
    public async Task ALibrarysResourceDirectories_ShareOneWatcher()
    {
        // The regression test: this is the shape that produced 190 watchers and 128 was the ceiling.
        var resources = Path.Combine(_tempDir, "MyLib", "Resources");
        var files = new[]
        {
            Path.Combine(resources, "Images", "Fluid", "tank.png"),
            Path.Combine(resources, "Images", "Electrical", "diode.png"),
            Path.Combine(resources, "Data", "Tables", "curve.mat"),
            Path.Combine(resources, "Scripts", "Dymola", "run.mos")
        };

        foreach (var file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }

        await _service.AnalyzeResourcesAsync(GraphWith("MyLib.Model", files));
        _service.StartMonitoringResources();

        // Compared the way the service compares paths. On Windows the roots are derived from the
        // reverse index, whose keys NormalizePath has already case-folded, so the root comes back
        // lowercased — true before this change too, and not something a watcher or a caller can tell.
        var watched = _service.GetWatchedRoots();
        Assert.Equal(
            new[] { Path.GetFullPath(resources) },
            watched,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    [Fact]
    public async Task ResourcesOutsideAnyLibrary_AreStillWatchedIndividually()
    {
        var first = Path.Combine(_tempDir, "shared", "one", "a.mat");
        var second = Path.Combine(_tempDir, "shared", "two", "b.mat");

        foreach (var file in new[] { first, second })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "x");
        }

        await _service.AnalyzeResourcesAsync(GraphWith("Other.Model", new[] { first, second }));
        _service.StartMonitoringResources();

        Assert.Equal(2, _service.GetWatchedRoots().Count);
    }

    [Fact]
    public async Task AFileInAFolderThatDoesNotExist_IsWatchedFromTheNearestFolderThatDoes()
    {
        // The folder a missing file would come back in cannot be watched while it does not exist,
        // so the watch goes up to what does - for its own entries only. Recursive, an absolute path
        // written on another machine walked up to C:\Users or / and watched everything below it;
        // Data arriving is an event at this level, which queues the file (see
        // ExternalResourceServiceTests.AFolderCreatedOrDeletedWhole_QueuesTheReferencedFilesInsideIt).
        var outside = Path.Combine(_tempDir, "shared");
        Directory.CreateDirectory(outside);
        var file = Path.Combine(outside, "Data", "Tables", "curve.mat");

        await _service.AnalyzeResourcesAsync(GraphWith("Other.Model", new[] { file }));
        _service.StartMonitoringResources();

        var root = Assert.Single(_service.GetWatchedRoots());
        Assert.Equal(Path.GetFullPath(outside), root,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.False(_service.IsWatchedRecursively(root));
    }

    [Fact]
    public async Task OnceTheFolderIsBack_TheWatcherMovesDownToIt()
    {
        // What a re-check does after the folder event: re-analysis puts the watcher on the nearest
        // folder that now exists.
        var outside = Path.Combine(_tempDir, "shared");
        Directory.CreateDirectory(outside);
        var file = Path.Combine(outside, "Data", "Tables", "curve.mat");
        var graph = GraphWith("Other.Model", new[] { file });

        await _service.AnalyzeResourcesAsync(graph);
        _service.StartMonitoringResources();
        Directory.CreateDirectory(Path.Combine(outside, "Data"));
        await _service.AnalyzeResourcesForModelsAsync(new[] { "Other.Model" }, graph);

        var root = Assert.Single(_service.GetWatchedRoots());
        Assert.Equal(Path.GetFullPath(Path.Combine(outside, "Data")), root,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.False(_service.IsWatchedRecursively(root));
    }

    [Fact]
    public async Task AWatcherIsDroppedWhenItsTreeIsNoLongerReferenced()
    {
        var file = Path.Combine(_tempDir, "MyLib", "Resources", "Data", "curve.mat");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");

        await _service.AnalyzeResourcesForModelsAsync(new[] { "MyLib.Model" }, GraphWith("MyLib.Model", new[] { file }));
        Assert.Single(_service.GetWatchedRoots());

        // Re-analysed with the resource gone, the watcher on the collapsed root has to go with it.
        await _service.AnalyzeResourcesForModelsAsync(new[] { "MyLib.Model" }, GraphWith("MyLib.Model", Array.Empty<string>()));
        Assert.Empty(_service.GetWatchedRoots());
    }

    private static DirectedGraph GraphWith(string modelId, IReadOnlyList<string> resolvedPaths)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode(modelId, modelId.Split('.').Last()));

        foreach (var path in resolvedPaths)
        {
            var node = graph.GetOrCreateResourceFileNode(path);
            graph.AddModelReferencesResource(modelId, node.Id, new ResourceEdge
            {
                RawPath = path,
                ReferenceType = ResourceReferenceType.LoadResource,
                IsAbsolutePath = true
            });
        }

        return graph;
    }
}

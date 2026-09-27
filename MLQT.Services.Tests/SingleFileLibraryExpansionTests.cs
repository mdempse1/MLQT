using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// B417 — Format All expands a single-file library in a repository (<c>MyLib.mo</c>) into the
/// package directory <c>MyLib/</c>, as <c>code-formatting.md</c> says it does, and the library has
/// to be the directory afterwards.
///
/// <para>It went on naming the deleted file. <c>LibraryContainingPath</c> asks whether a file lies
/// within a library's source, and nothing lies within a file, so a Refresh, a Code Review reload or
/// a VCS update of any of the new files put its classes in no library — until the project was
/// reloaded, which found the directory and gave a different answer. These run the real services
/// against a real Git working copy: expand, then reload a file, then reload the project.</para>
/// </summary>
public sealed class SingleFileLibraryExpansionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-b417-" + Guid.NewGuid().ToString("N"));

    private const string SingleFileLibrary = """
        package MyLib "a library in one file"
          model A "a"
            Real x;
          equation
            x = 1;
          end A;

          package Sub "a nested package"
            model C "c"
            end C;
          end Sub;

          model B "b"
          end B;
        end MyLib;
        """;

    private string OriginalFile => Path.Combine(_root, "MyLib.mo");
    private string ExpandedDirectory => Path.Combine(_root, "MyLib");

    public SingleFileLibraryExpansionTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(OriginalFile, SingleFileLibrary.ReplaceLineEndings("\n"));
        Git("init");
        Git("add .");
        Git("-c user.name=Test -c user.email=test@example.com commit -m initial");
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);   // git's object files are read-only
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private void Git(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {arguments} failed: {stderr.Result}");
    }

    private sealed record Session(
        RepositoryService Repositories, LibraryDataService Libraries, InMemorySettingsService Settings, string RepositoryId)
    {
        public Repository Repository => Repositories.GetRepository(RepositoryId)!;
        public LoadedLibrary Library => Assert.Single(Libraries.Libraries);
    }

    /// <summary>The project as a user has it: the repository added, its library loaded, formatting on.</summary>
    private async Task<Session> OpenAsync()
    {
        var settings = new InMemorySettingsService();
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, settings, new FileMonitoringService());

        var added = await repositories.AddRepositoryAsync(_root, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        Assert.Equal(RepositoryVcsType.Git, added.Repository!.VcsType);
        added.Repository.StyleSettings = new StyleCheckingSettings { ApplyFormattingRules = true };
        await repositories.LoadLibrariesAsync(added.Repository.Id);

        return new Session(repositories, libraries, settings, added.Repository.Id);
    }

    /// <summary>Opens the project and runs Format All over it.</summary>
    private async Task<Session> ExpandAsync()
    {
        var session = await OpenAsync();
        Assert.Equal(OriginalFile, session.Library.SourcePath);   // the starting point: a file

        await new FormattingPipeline(session.Libraries, session.Repositories)
            .SaveAllLibrariesWithFormattingAsync(session.RepositoryId);

        Assert.False(File.Exists(OriginalFile));
        Assert.True(File.Exists(Path.Combine(ExpandedDirectory, "package.mo")));
        return session;
    }

    [Fact]
    public async Task AnExpandedLibrary_IsTheDirectoryItBecame()
    {
        var session = await ExpandAsync();

        Assert.Equal(ExpandedDirectory, session.Library.SourcePath);
        Assert.Equal("MyLib", session.Library.RelativePathInRepository);
        Assert.Equal(LibrarySourceType.Git, session.Library.SourceType);
        Assert.Equal(["MyLib"], session.Repository.DiscoveredLibraries.Keys);
        Assert.Equal("MyLib", session.Repository.DiscoveredLibraries["MyLib"]);
    }

    [Fact]
    public async Task TheSavedProject_ListsTheDirectory()
    {
        // What a project reload starts from: the repository's libraries by path, and the file is gone.
        var session = await ExpandAsync();

        var saved = await session.Settings.GetAsync("Repositories", new RepositorySettingsCollection());
        var entry = Assert.Single(Assert.Single(saved.Projects).Repositories);
        Assert.Equal(["MyLib"], entry.LibraryPaths);
    }

    [Fact]
    public async Task ReloadingOneOfTheNewFiles_PutsItsClassesInTheLibrary()
    {
        // Refresh's path: the file monitor reports A.mo changed, and UpdateChangedFilesAsync reloads
        // it against the working copy. With the library still naming MyLib.mo, A was reloaded into
        // the graph and into no library - gone from the tree, the checks and the baseline.
        var session = await ExpandAsync();
        var fileOfA = Path.Combine(ExpandedDirectory, "A.mo");
        Assert.True(File.Exists(fileOfA));
        File.WriteAllText(fileOfA, "within MyLib;\nmodel A \"a, edited\"\n  Real x;\nequation\n  x = 2;\nend A;\n");

        var affected = await session.Libraries.UpdateChangedFilesAsync([fileOfA], session.Repository.VcsRootPath);

        Assert.Contains("MyLib.A", affected);
        Assert.Same(session.Library, session.Libraries.GetOwningLibrary("MyLib.A"));
        Assert.Contains("MyLib.A", session.Library.ChildrenByParent["MyLib"]);
    }

    [Fact]
    public async Task ReloadingTheProject_GivesTheSameLibrary()
    {
        // Re-registration is only right if it is what a reload would have produced: the same
        // location, the same classes, and each class in the same file.
        var expanded = await ExpandAsync();

        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, expanded.Settings, new FileMonitoringService());
        await repositories.LoadRepositorySettingsAsync();
        var reloaded = new Session(repositories, libraries, expanded.Settings, expanded.RepositoryId);

        var before = expanded.Library;
        var after = reloaded.Library;
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.SourcePath, after.SourcePath);
        Assert.Equal(before.SourceType, after.SourceType);
        Assert.Equal(before.RelativePathInRepository, after.RelativePathInRepository);
        Assert.Equal(expanded.Repository.DiscoveredLibraries, reloaded.Repository.DiscoveredLibraries);
        Assert.Equal(before.ModelIds.Order(), after.ModelIds.Order());
        Assert.Equal(before.TopLevelModelIds.Order(), after.TopLevelModelIds.Order());
        Assert.Equal(before.ChildrenByParent.Keys.Order(), after.ChildrenByParent.Keys.Order());
        foreach (var (parent, children) in before.ChildrenByParent)
            Assert.Equal(children.Order(), after.ChildrenByParent[parent].Order());

        foreach (var modelId in before.ModelIds)
            Assert.Equal(FileOf(expanded.Libraries, modelId), FileOf(reloaded.Libraries, modelId));
    }

    [Fact]
    public async Task ALibraryOpenedOnItsOwn_BecomesADirectoryLibrary()
    {
        // Outside a repository the source type is the shape on disk, so it follows the move too.
        var libraries = new LibraryDataService();
        var library = await libraries.AddLibraryFromPathAsync(OriginalFile);
        var repositories = new RepositoryService(libraries, new InMemorySettingsService(), new FileMonitoringService());

        Assert.True(await repositories.RelocateLibraryAsync(library.Id, ExpandedDirectory));

        Assert.Equal(ExpandedDirectory, library.SourcePath);
        Assert.Equal(LibrarySourceType.Directory, library.SourceType);
    }

    [Fact]
    public async Task RelocatingALibraryThatIsNotLoaded_DoesNothing()
    {
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, new InMemorySettingsService(), new FileMonitoringService());

        Assert.False(await repositories.RelocateLibraryAsync("no such library", ExpandedDirectory));
        Assert.False(libraries.RelocateLibrary("no such library", ExpandedDirectory));
    }

    private static string? FileOf(LibraryDataService libraries, string modelId)
    {
        var model = libraries.GetModelById(modelId);
        return model?.ContainingFileId is { } fileId
            ? libraries.CombinedGraph.GetNode<FileNode>(fileId)?.FilePath
            : null;
    }
}

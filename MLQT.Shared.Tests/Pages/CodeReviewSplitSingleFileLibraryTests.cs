using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Pages;
using MLQT.TestSupport;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MudBlazor;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B429 - Split into files on a repository library held in one <c>MyLib.mo</c> splits it, and the
/// library is the directory afterwards, as it is after Format All (B417).
///
/// <para>It was refused (B306), because the split deleted the file the library was loaded from and
/// the library went on naming it: the reload resolved the deleted file as "." and each new one as
/// <c>../MyLib/...</c>, and nothing lies within a file, so no class in the new files was placed in
/// the library until the project was reloaded. These press the button on the rendered page over the
/// real library and repository services and a real Git working copy, then reload a new file, then
/// reload the project.</para>
/// </summary>
public sealed class CodeReviewSplitSingleFileLibraryTests : CodeReviewTestBase
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mlqt-b429-" + Guid.NewGuid().ToString("N"));

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

    private readonly InMemorySettingsService _settings = new();
    private readonly LibraryDataService _libraries = new();
    private readonly RepositoryService _repositories;
    private readonly string _repositoryId;

    private string OriginalFile => Path.Combine(_root, "MyLib.mo");
    private string ExpandedDirectory => Path.Combine(_root, "MyLib");

    public CodeReviewSplitSingleFileLibraryTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(OriginalFile, Lf(SingleFileLibrary));
        Git("init");
        Git("add .");
        Git("-c user.name=Test -c user.email=test@example.com commit -m initial");

        _repositories = new RepositoryService(_libraries, _settings, new FileMonitoringService());
        var added = _repositories.AddRepositoryAsync(_root, startMonitoring: false).GetAwaiter().GetResult();
        Assert.True(added.Success, added.ErrorMessage);
        _repositoryId = added.Repository!.Id;
        _repositories.LoadLibrariesAsync(_repositoryId).GetAwaiter().GetResult();

        // The real services in place of the harness's doubles: the last registration is the one given.
        Services.AddSingleton<ILibraryDataService>(_libraries);
        Services.AddSingleton<IRepositoryService>(_repositories);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
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

    private LoadedLibrary Library => Assert.Single(_libraries.Libraries);

    /// <summary>Presses Split into files on the library's finding, confirms, and waits for the split.</summary>
    private void Split()
    {
        Assert.Equal(OriginalFile, Library.SourcePath);   // the starting point: a file
        Findings.AddLogMessage(new LogMessage("MyLib", "Style warning", 1, "package MyLib keeps 3 classes inline")
        {
            RuleId = RuleIds.SingleFilePackage,
            Source = LogMessage.StyleCheckingSource,
        });

        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<CodeReview>();

        const string button = "button[aria-label='Split this package into files']";
        Eventually(page, () => Assert.NotNull(page.Find(button)));
        page.Find(button).Click();

        dialogs.WaitForAssertion(() => Assert.Contains(dialogs.FindAll("button"), b => b.TextContent.Trim() == "Split"),
            Patience);
        dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Split").Click();

        // The finding goes once the split has gone through, which is after the reload.
        Eventually(page, () => Assert.DoesNotContain(Findings.LogMessages.ToList(), m => m.RuleId == RuleIds.SingleFilePackage));
        Assert.False(File.Exists(OriginalFile));
        Assert.True(File.Exists(Path.Combine(ExpandedDirectory, "package.mo")));
    }

    [Fact]
    public void TheLibraryIsTheDirectoryItBecame()
    {
        Split();

        Assert.Equal(ExpandedDirectory, Library.SourcePath);
        Assert.Equal("MyLib", Library.RelativePathInRepository);
        Assert.Equal(["MyLib"], _repositories.GetRepository(_repositoryId)!.DiscoveredLibraries.Keys);
    }

    [Fact]
    public void TheReloadAfterTheSplitPutsEveryClassInTheLibrary()
    {
        // The reload was against the deleted file: its classes stayed in the graph under a file
        // node that no longer exists, and the new files' classes were placed in no library.
        Split();

        foreach (var id in new[] { "MyLib", "MyLib.A", "MyLib.B", "MyLib.Sub", "MyLib.Sub.C" })
            Assert.Same(Library, _libraries.GetOwningLibrary(id));
        Assert.DoesNotContain(_libraries.CombinedGraph.FileNodes,
            f => string.Equals(Path.GetFullPath(f.FilePath), OriginalFile, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ANewFileAfterTheSplit_IsPlacedInTheLibrary()
    {
        // Refresh's path, for a file nobody had seen before.
        Split();
        var newFile = Path.Combine(ExpandedDirectory, "D.mo");
        File.WriteAllText(newFile, "within MyLib;\nmodel D \"d\"\nend D;\n");

        var affected = await _libraries.UpdateChangedFilesAsync(
            [newFile], _repositories.GetRepository(_repositoryId)!.VcsRootPath);

        Assert.Contains("MyLib.D", affected);
        Assert.Same(Library, _libraries.GetOwningLibrary("MyLib.D"));
        Assert.Contains("MyLib.D", Library.ChildrenByParent["MyLib"]);
    }

    [Fact]
    public async Task ReloadingTheProject_GivesTheSameLibrary()
    {
        // Re-registration is only right if it is what a reload would have produced.
        Split();

        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, _settings, new FileMonitoringService());
        await repositories.LoadRepositorySettingsAsync(cancellationToken: Xunit.TestContext.Current.CancellationToken);

        var before = Library;
        var after = Assert.Single(libraries.Libraries);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.SourcePath, after.SourcePath);
        Assert.Equal(before.SourceType, after.SourceType);
        Assert.Equal(before.RelativePathInRepository, after.RelativePathInRepository);
        Assert.Equal(_repositories.GetRepository(_repositoryId)!.DiscoveredLibraries,
            repositories.GetRepository(_repositoryId)!.DiscoveredLibraries);
        Assert.Equal(before.ModelIds.Order(), after.ModelIds.Order());
        Assert.Equal(before.TopLevelModelIds.Order(), after.TopLevelModelIds.Order());
        foreach (var modelId in before.ModelIds)
            Assert.Equal(FileOf(_libraries, modelId), FileOf(libraries, modelId));
    }

    private static string? FileOf(LibraryDataService libraries, string modelId)
    {
        var model = libraries.GetModelById(modelId);
        return model?.ContainingFileId is { } fileId
            ? libraries.CombinedGraph.GetNode<FileNode>(fileId)?.FilePath
            : null;
    }
}

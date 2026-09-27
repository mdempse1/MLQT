using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Moq;
using RevisionControl;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// <see cref="FormattingPipeline"/>, extracted from <c>MainLayout</c> in phase 7a-4.
///
/// <para>The point of the extraction was that neither of MLQT's two ways of writing formatted
/// Modelica could be run without starting the application — and <b>B65</b> was a defect that existed
/// because one of them had drifted from the other. These drive it against a temp directory.</para>
/// </summary>
public sealed class FormattingPipelineTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mlqt-fmt-" + Guid.NewGuid().ToString("N"));

    private readonly DirectedGraph _graph = new();
    private readonly Mock<ILibraryDataService> _libraries = new();
    private readonly Mock<IRepositoryService> _repositories = new();

    public FormattingPipelineTests()
    {
        Directory.CreateDirectory(_root);
        _libraries.SetupGet(l => l.CombinedGraph).Returns(_graph);
        _libraries.SetupGet(l => l.Libraries).Returns([]);
        _repositories.SetupGet(r => r.Repositories).Returns([]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private FormattingPipeline Pipeline() => new(_libraries.Object, _repositories.Object);

    /// <summary>Writes a badly laid-out class and registers it in the graph.</summary>
    private string AddUnformattedFile(string name = "Thing.mo")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "model Thing\nReal x;\nequation\nx=1;\nend Thing;\n");

        var fileId = GraphBuilder.GenerateFileId(path);
        _graph.AddNode(new FileNode(fileId, path));
        _graph.AddNode(new ModelNode("Thing", "Thing", File.ReadAllText(path)));
        _graph.AddFileContainsModel(fileId, "Thing");
        return path;
    }

    private static StyleCheckingSettings Formatting(bool on = true) => new() { ApplyFormattingRules = on };

    [Fact]
    public async Task FormattingAChangedFile_RewritesIt()
    {
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);

        await Pipeline().FormatChangedFilesAsync([path], Formatting());

        Assert.NotEqual(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task WithFormattingSwitchedOff_TheFileIsUntouched()
    {
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);

        await Pipeline().FormatChangedFilesAsync([path], Formatting(on: false));

        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task EveryWriteIsRecorded()
    {
        // The monitor tells MLQT's own writes from the user's by their timestamps. A write that is
        // not recorded starts a formatting pass on the formatter's own output.
        var path = AddUnformattedFile();
        var pipeline = Pipeline();

        await pipeline.FormatChangedFilesAsync([path], Formatting());

        Assert.True(pipeline.WrittenFileTimestamps.ContainsKey(path));
    }

    [Fact]
    public async Task TheRecordedTimeMatchesTheFileOnDisk()
    {
        var path = AddUnformattedFile();
        var pipeline = Pipeline();

        await pipeline.FormatChangedFilesAsync([path], Formatting());

        Assert.Equal(File.GetLastWriteTimeUtc(path), pipeline.WrittenFileTimestamps[path]);
    }

    [Fact]
    public async Task AFileThatWasNotWritten_IsNotRecorded()
    {
        var path = AddUnformattedFile();
        var pipeline = Pipeline();

        await pipeline.FormatChangedFilesAsync([path], Formatting(on: false));

        Assert.Empty(pipeline.WrittenFileTimestamps);
    }

    [Fact]
    public async Task ClearingTheRecord_ForgetsIt()
    {
        var path = AddUnformattedFile();
        var pipeline = Pipeline();
        await pipeline.FormatChangedFilesAsync([path], Formatting());

        pipeline.ClearWrittenFileTimestamps();

        Assert.Empty(pipeline.WrittenFileTimestamps);
    }

    [Fact]
    public async Task WithNoRepositories_FormattingModifiedFilesDoesNothing()
    {
        Assert.Equal(0, await Pipeline().FormatModifiedFilesAsync());
    }

    [Fact]
    public async Task AReferenceOnlyRepository_IsNeverFormatted()
    {
        // The settings page promises a reference library is never written to.
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);

        _repositories.SetupGet(r => r.Repositories).Returns(
        [
            new Repository
            {
                Id = "ref", Name = "Reference", LocalPath = _root, VcsRootPath = _root,
                IsReferenceOnly = true, StyleSettings = Formatting(),
            },
        ]);

        var formatted = await Pipeline().FormatModifiedFilesAsync();

        Assert.Equal(0, formatted);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task ARepositoryWithFormattingOff_IsSkipped()
    {
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);

        _repositories.SetupGet(r => r.Repositories).Returns(
        [
            new Repository
            {
                Id = "repo", Name = "Repo", LocalPath = _root, VcsRootPath = _root,
                StyleSettings = Formatting(on: false),
            },
        ]);

        Assert.Equal(0, await Pipeline().FormatModifiedFilesAsync());
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task AModifiedFileInARepository_IsFormatted()
    {
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);

        _repositories.SetupGet(r => r.Repositories).Returns(
        [
            new Repository
            {
                Id = "repo", Name = "Repo", LocalPath = _root, VcsRootPath = _root,
                StyleSettings = Formatting(),
            },
        ]);
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo")).Returns(
        [
            new VcsWorkingCopyFile { Path = "Thing.mo", Status = VcsFileStatus.Modified },
        ]);

        var formatted = await Pipeline().FormatModifiedFilesAsync();

        Assert.Equal(1, formatted);
        Assert.NotEqual(before, File.ReadAllText(path));
    }

    // ============================================================================
    // A reference library inside a maintained repository's folder is never written (B421)
    // ============================================================================

    /// <summary>
    /// A maintained repository at the root, and a vendor library checked out inside its folder that
    /// belongs to a reference-only repository. The watched repository - and so every change's
    /// RepositoryId, and the settings the caller passes - is the outer one.
    /// </summary>
    private string AddVendorFileInsideAMaintainedRepository(bool referenceByLibraryFlag = false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Vendor"));
        var path = AddUnformattedFile(Path.Combine("Vendor", "Thing.mo"));

        var outer = new Repository
        {
            Id = "repo", Name = "Repo", LocalPath = _root, VcsRootPath = _root, StyleSettings = Formatting(),
        };
        var vendor = new Repository
        {
            Id = "ref", Name = "Vendor", LocalPath = Path.Combine(_root, "Vendor"),
            VcsRootPath = Path.Combine(_root, "Vendor"), IsReferenceOnly = true, StyleSettings = Formatting(),
        };
        _repositories.SetupGet(r => r.Repositories).Returns([outer, vendor]);
        _repositories.Setup(r => r.GetRepository("repo")).Returns(outer);
        _repositories.Setup(r => r.GetRepository("ref")).Returns(vendor);
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo")).Returns(
        [
            new VcsWorkingCopyFile { Path = "Vendor/Thing.mo", Status = VcsFileStatus.Modified },
        ]);

        _libraries.Setup(l => l.GetOwningLibrary("Thing")).Returns(referenceByLibraryFlag
            ? new LoadedLibrary { Name = "Vendor", SourcePath = Path.Combine(_root, "Vendor"), IsReferenceOnly = true }
            : new LoadedLibrary { Name = "Vendor", SourcePath = Path.Combine(_root, "Vendor"), RepositoryId = "ref" });
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AChangedFileOwnedByAReferenceLibrary_IsNotWritten_WhateverSettingsItIsHandedWith(
        bool referenceByLibraryFlag)
    {
        // Refresh groups a change by the watched repository, which for a file in a nested vendor
        // checkout is the outer one, and hands it over with the outer repository's settings.
        var path = AddVendorFileInsideAMaintainedRepository(referenceByLibraryFlag);
        var before = File.ReadAllText(path);
        var pipeline = Pipeline();

        await pipeline.FormatChangedFilesAsync([path], Formatting());

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Empty(pipeline.WrittenFileTimestamps);
    }

    [Fact]
    public async Task AModifiedFileOfANestedReferenceLibrary_IsNotFormattedWithTheOuterRepository()
    {
        // The startup path: the outer repository's VCS status reports the vendor's file as modified.
        var path = AddVendorFileInsideAMaintainedRepository();
        var before = File.ReadAllText(path);

        await Pipeline().FormatModifiedFilesAsync();

        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public async Task AChangedFileOwnedByAMaintainedLibrary_IsStillWritten()
    {
        // The control: asking the owner is not a reason to skip the user's own file.
        var path = AddUnformattedFile();
        var before = File.ReadAllText(path);
        var repository = new Repository
        {
            Id = "repo", Name = "Repo", LocalPath = _root, VcsRootPath = _root, StyleSettings = Formatting(),
        };
        _repositories.SetupGet(r => r.Repositories).Returns([repository]);
        _libraries.Setup(l => l.GetOwningLibrary("Thing")).Returns(
            new LoadedLibrary { Name = "Thing", SourcePath = _root, RepositoryId = "repo" });

        await Pipeline().FormatChangedFilesAsync([path], Formatting());

        Assert.NotEqual(before, File.ReadAllText(path));
    }

    // ============================================================================
    // Format All deletes only what it has written somewhere else (B373)
    // ============================================================================

    private const string SingleFileLibrary =
        "package MyLib\nmodel A\nReal x;\nend A;\nmodel B\nend B;\nend MyLib;\n";

    /// <summary>A real library service with the given libraries loaded into repository "repo".</summary>
    private async Task<(LibraryDataService Service, FormattingPipeline Pipeline)> RepositoryWith(
        params string[] libraryPaths)
    {
        var service = new LibraryDataService();
        foreach (var path in libraryPaths)
        {
            var library = await service.AddLibraryFromPathAsync(path);
            library.SourceType = LibrarySourceType.Git;
            library.RepositoryId = "repo";
        }

        var repository = new Repository
        {
            Id = "repo", Name = "Repo", LocalPath = _root, VcsRootPath = _root, StyleSettings = Formatting(),
        };
        _repositories.SetupGet(r => r.Repositories).Returns([repository]);
        _repositories.Setup(r => r.GetRepository("repo")).Returns(repository);
        _repositories.Setup(r => r.GetWorkingCopyChanges("repo")).Returns([]);
        return (service, new FormattingPipeline(service, _repositories.Object));
    }

    [Fact]
    public async Task AClassWhoseNewFileCannotBeWritten_KeepsTheFileItCameFrom()
    {
        // A single-file library in a repository is expanded into a directory. When A's new file
        // could not be written, MyLib.mo - the only file still holding A - was deleted as an orphan.
        var original = Path.Combine(_root, "MyLib.mo");
        File.WriteAllText(original, SingleFileLibrary);
        var obstruction = Path.Combine(_root, "MyLib", "A.mo");
        Directory.CreateDirectory(obstruction);
        File.WriteAllText(Path.Combine(obstruction, "in the way"), "");
        var (_, pipeline) = await RepositoryWith(original);
        var failures = new List<string>();

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo", (name, _) => failures.Add(name));

        Assert.Equal(SingleFileLibrary, File.ReadAllText(original));
        Assert.Equal(["MyLib"], failures);
    }

    [Fact]
    public async Task ALibraryWhoseSaveFailed_IsNotReRegistered()
    {
        // Its file is kept and still holds every class, so it is still the library (B417).
        var original = Path.Combine(_root, "MyLib.mo");
        File.WriteAllText(original, SingleFileLibrary);
        var obstruction = Path.Combine(_root, "MyLib", "A.mo");
        Directory.CreateDirectory(obstruction);
        File.WriteAllText(Path.Combine(obstruction, "in the way"), "");
        var (service, pipeline) = await RepositoryWith(original);

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo");

        _repositories.Verify(r => r.RelocateLibraryAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(original, Assert.Single(service.Libraries).SourcePath);
    }

    [Fact]
    public async Task AnExpandedLibrary_IsReRegisteredAsItsDirectory()
    {
        var original = Path.Combine(_root, "MyLib.mo");
        File.WriteAllText(original, SingleFileLibrary);
        var (service, pipeline) = await RepositoryWith(original);

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo");

        _repositories.Verify(r => r.RelocateLibraryAsync(
            Assert.Single(service.Libraries).Id, Path.Combine(_root, "MyLib")), Times.Once);
    }

    [Fact]
    public async Task ASingleFileWhoseRootIsNotAPackage_StaysAFile()
    {
        // A library that is one model has no directory to become: the save writes it back where it was.
        var original = Path.Combine(_root, "Solo.mo");
        File.WriteAllText(original, "model Solo\nReal x;\nend Solo;\n");
        var (_, pipeline) = await RepositoryWith(original);

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo");

        Assert.True(File.Exists(original));
        _repositories.Verify(r => r.RelocateLibraryAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ALibraryWhoseSaveCompletes_StillLosesItsOrphans()
    {
        // The other half: the protection is for a library with a failed write, not a reason to stop
        // tidying up after one that wrote everything. The expansion is intended (B373, decided
        // 2026-09-27: code-formatting.md's "One File Per Class"); SingleFileLibraryExpansionTests holds
        // what the library is registered as afterwards (B417).
        var original = Path.Combine(_root, "MyLib.mo");
        File.WriteAllText(original, SingleFileLibrary);
        var (_, pipeline) = await RepositoryWith(original);

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo");

        Assert.False(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(_root, "MyLib", "A.mo")));
    }

    [Fact]
    public async Task ASiblingLibraryWhoseNameExtendsThisOnes_IsNotTakenForAnOrphan()
    {
        // `…/Lib` is a prefix of `…/LibExtra/package.mo`. LibExtra belongs to no repository being
        // formatted, so it is not written - and a prefix test counted its file as Lib's original.
        var lib = Path.Combine(_root, "Lib");
        var extra = Path.Combine(_root, "LibExtra");
        Directory.CreateDirectory(lib);
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "package Lib\nend Lib;\n");
        var extraFile = Path.Combine(extra, "package.mo");
        File.WriteAllText(extraFile, "package LibExtra\nend LibExtra;\n");
        var (service, pipeline) = await RepositoryWith(lib);
        await service.AddLibraryFromDirectoryAsync(extra);

        await pipeline.SaveAllLibrariesWithFormattingAsync("repo");

        Assert.True(File.Exists(extraFile));
    }
}

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
}

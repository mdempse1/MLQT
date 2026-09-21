using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaParser.Comparison;
using Moq;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// The layer between "what did version control change" and "what kind of change is it" (B191): it
/// finds the committed version of each changed file, and keys the answer by model id.
/// </summary>
/// <remarks>
/// The comparison itself is <c>ClassChangeClassifier</c>'s and is tested against real class text in
/// <c>ModelicaParser.Tests</c>. What is asserted here is everything around it — which files are
/// looked at, which are not read at all, what a missing committed version means, and that the
/// cache does not outlive its answer.
/// </remarks>
public class ModelChangeClassifierTests : IDisposable
{
    private const string Committed = """
        within MyLib;
        model Resistor "An ideal resistor"
          parameter Real R = 100;
        equation
          annotation (Icon(graphics={Line(points={{0,0},{1,1}})}));
        end Resistor;
        """;

    private readonly string _root;
    private readonly Repository _repository;
    private readonly Mock<IRepositoryService> _repositories = new();
    private readonly ModelChangeClassifier _classifier;

    public ModelChangeClassifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mlqt-b191-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _repository = new Repository
        {
            Id = "repo-1",
            Name = "Lib",
            LocalPath = _root,
            VcsRootPath = _root,
            VcsType = RepositoryVcsType.Git,
            CurrentRevision = "abc123",
        };

        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Committed);

        _classifier = new ModelChangeClassifier(_repositories.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string text)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private IReadOnlyDictionary<string, ClassChangeKind> Classify(params VcsWorkingCopyFile[] changes)
        => _classifier.Classify(_repository, changes);

    private static VcsWorkingCopyFile Change(string path, VcsFileStatus status = VcsFileStatus.Modified)
        => new() { Path = path, Status = status };

    // ---------------------------------------------------------------- the ordinary cases

    [Fact]
    public void AGraphicalEditIsCosmetic()
    {
        Write("Resistor.mo", Committed.Replace("{{0,0},{1,1}}", "{{0,0},{9,9}}"));

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    [Fact]
    public void AChangedParameterAffectsSimulation()
    {
        Write("Resistor.mo", Committed.Replace("R = 100", "R = 220"));

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    /// <summary>
    /// An unchanged class is reported as unchanged rather than left out — absence has to mean "not
    /// asked", or a caller cannot tell a repository it knows nothing about from one with no changes.
    /// </summary>
    [Fact]
    public void AnUnchangedClassIsReportedRatherThanOmitted()
    {
        Write("Resistor.mo", Committed);

        Assert.Equal(ClassChangeKind.Unchanged, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    [Fact]
    public void ModelsAreKeyedByFullModelicaName()
    {
        Write("Components.mo", """
            within MyLib;
            package Components
              model Resistor
                parameter Real R = 1;
              end Resistor;
            end Components;
            """);

        var kinds = Classify(Change("Components.mo", VcsFileStatus.Untracked));

        Assert.Contains("MyLib.Components", kinds);
        Assert.Contains("MyLib.Components.Resistor", kinds);
    }

    // ---------------------------------------------------------------- which files are looked at

    [Fact]
    public void ANonModelicaFileIsIgnored()
    {
        Write("README.md", "not Modelica");

        Assert.Empty(Classify(Change("README.md")));
    }

    [Fact]
    public void ADeletedFileHasNothingLeftToMark()
    {
        Assert.Empty(Classify(Change("Gone.mo", VcsFileStatus.Deleted)));
    }

    /// <summary>
    /// An added or untracked file has no committed version, so every class in it is new — and asking
    /// version control for a version that cannot exist is a round trip for a known answer.
    /// </summary>
    [Theory]
    [InlineData(VcsFileStatus.Added)]
    [InlineData(VcsFileStatus.Untracked)]
    public void ANewFilesClassesAreAddedWithoutAskingForACommittedVersion(VcsFileStatus status)
    {
        Write("New.mo", Committed);

        Assert.Equal(ClassChangeKind.Added, Classify(Change("New.mo", status))["MyLib.Resistor"]);
        _repositories.Verify(
            r => r.GetFileContentAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    /// <summary>
    /// A conflicted working copy holds conflict markers, not Modelica. Reading it would report a
    /// parse failure, which is true and useless.
    /// </summary>
    [Fact]
    public void AConflictedFileIsUnknownRatherThanUnreadable()
    {
        Write("Resistor.mo", Committed);

        Assert.Equal(
            ClassChangeKind.Unknown,
            Classify(Change("Resistor.mo", VcsFileStatus.Conflicted))["MyLib.Resistor"]);
    }

    [Fact]
    public void AFileWhoseCommittedVersionIsUnavailableIsUnknown()
    {
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns("model Broken\r\n  Real x\r\nend Broken;");
        Write("Resistor.mo", Committed);

        Assert.Equal(ClassChangeKind.Unknown, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    [Fact]
    public void APathUsingForwardSlashesIsFoundOnWindowsToo()
    {
        Write(Path.Combine("Components", "Resistor.mo"), Committed.Replace("R = 100", "R = 220"));

        Assert.Equal(
            ClassChangeKind.AffectsSimulation,
            Classify(Change("Components/Resistor.mo"))["MyLib.Resistor"]);
    }

    // ---------------------------------------------------------------- the cache

    [Fact]
    public void TheCommittedVersionIsReadOncePerUnchangedFile()
    {
        Write("Resistor.mo", Committed);

        Classify(Change("Resistor.mo"));
        Classify(Change("Resistor.mo"));

        _repositories.Verify(
            r => r.GetFileContentAtRevision("repo-1", "Resistor.mo", It.IsAny<string?>()), Times.Once);
    }

    /// <summary>
    /// The point of caching on the file's size and write time: an edit has to be seen. The new
    /// value is a digit longer than the old one, so the length differs too and the test does not
    /// rest on the clock's resolution alone.
    /// </summary>
    [Fact]
    public void EditingTheFileIsNoticed()
    {
        var path = Write("Resistor.mo", Committed);
        Assert.Equal(ClassChangeKind.Unchanged, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);

        File.WriteAllText(path, Committed.Replace("R = 100", "R = 2200"));

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    /// <summary>
    /// A pull changes what the committed version is without touching the working copy, which is why
    /// the repository's revision is part of the key.
    /// </summary>
    [Fact]
    public void ANewRevisionIsNoticedEvenThoughTheFileDidNotMove()
    {
        Write("Resistor.mo", Committed);
        Assert.Equal(ClassChangeKind.Unchanged, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);

        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Committed.Replace("R = 100", "R = 220"));
        _repository.CurrentRevision = "def456";

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    [Fact]
    public void InvalidatingARepositoryDiscardsWhatWasCachedForIt()
    {
        Write("Resistor.mo", Committed);
        Classify(Change("Resistor.mo"));

        _classifier.Invalidate("repo-1");
        Classify(Change("Resistor.mo"));

        _repositories.Verify(
            r => r.GetFileContentAtRevision("repo-1", "Resistor.mo", It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public void InvalidatingAnotherRepositoryLeavesThisOneAlone()
    {
        Write("Resistor.mo", Committed);
        Classify(Change("Resistor.mo"));

        _classifier.Invalidate("repo-2");
        Classify(Change("Resistor.mo"));

        _repositories.Verify(
            r => r.GetFileContentAtRevision("repo-1", "Resistor.mo", It.IsAny<string?>()), Times.Once);
    }
}

using ModelicaGraph;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using Moq;
using RevisionControl;

namespace MLQT.Services.Tests;

/// <summary>
/// The desktop issues list shows the whole standing debt, which drowns out what the user just did.
/// Classifying each issue against the repository's committed baseline lets the list be narrowed to
/// what the working copy has actually changed.
///
/// "Touched" is deliberately the working copy's pending changes rather than a commit-to-commit diff:
/// in the app the question is "what have I done right now", and the answer must not depend on which
/// commit the user happens to be sitting on.
/// </summary>
public class BaselineStatusServiceTests : IDisposable
{
    private readonly string _repoDir =
        Path.Combine(Path.GetTempPath(), "mlqt-blstatus-" + Guid.NewGuid().ToString("N"));

    public BaselineStatusServiceTests() => Directory.CreateDirectory(Path.Combine(_repoDir, ".mlqt"));

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch { }
    }

    private static Finding F(string model, string element) => new()
    {
        RuleId = "MLQT.Doc.ParameterDescription",
        ModelId = model,
        ElementPath = element,
        Message = "m",
        Severity = RuleSeverity.Warning
    };

    // --- the snapshot's classification ----------------------------------------------------------

    private static BaselineStatusSnapshot Snapshot(Baseline baseline, params string[] touchedModels)
    {
        var byModel = new[] { "Lib.A", "Lib.B" }
            .ToDictionary(id => id, _ => baseline, StringComparer.Ordinal);
        return new BaselineStatusSnapshot(
            byModel, touchedModels.ToHashSet(StringComparer.Ordinal), touchedModels.Length);
    }

    [Fact]
    public void NotInTheBaseline_IsNew()
    {
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x")]));

        Assert.Equal(FindingStatus.New, snapshot.StatusOf(F("Lib.A", "y").ToLogMessage()));
    }

    [Fact]
    public void InTheBaseline_InAnUntouchedModel_IsAcceptedDebt()
    {
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x")]));

        Assert.Equal(FindingStatus.AcceptedDebt, snapshot.StatusOf(F("Lib.A", "x").ToLogMessage()));
    }

    [Fact]
    public void InTheBaseline_InAFileWaitingToBeCommitted_IsTouchedDebt()
    {
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x")]), "Lib.A");

        Assert.Equal(FindingStatus.TouchedDebt, snapshot.StatusOf(F("Lib.A", "x").ToLogMessage()));
    }

    [Fact]
    public void TouchingOneModelDoesNotTouchAnother()
    {
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x"), F("Lib.B", "x")]), "Lib.A");

        Assert.Equal(FindingStatus.TouchedDebt, snapshot.StatusOf(F("Lib.A", "x").ToLogMessage()));
        Assert.Equal(FindingStatus.AcceptedDebt, snapshot.StatusOf(F("Lib.B", "x").ToLogMessage()));
    }

    [Fact]
    public void AModelWithNoBaseline_IsUnclassified_AndCountsAsWorthShowing()
    {
        // "No baseline for it" is not "already accepted" — hiding it would lose real issues.
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x")]));
        var message = F("Other.C", "x").ToLogMessage();

        Assert.Null(snapshot.StatusOf(message));
        Assert.True(snapshot.IsChangedFromBaseline(message));
    }

    [Fact]
    public void OnlyAcceptedDebtIsFilteredOut()
    {
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x"), F("Lib.B", "x")]), "Lib.B");

        Assert.False(snapshot.IsChangedFromBaseline(F("Lib.A", "x").ToLogMessage()));   // accepted
        Assert.True(snapshot.IsChangedFromBaseline(F("Lib.A", "y").ToLogMessage()));    // new
        Assert.True(snapshot.IsChangedFromBaseline(F("Lib.B", "x").ToLogMessage()));    // touched debt
    }

    [Fact]
    public void AMessageWithNoFingerprint_IsNew()
    {
        // An external tool's output carries no finding identity, so it can never match a baseline.
        var snapshot = Snapshot(Baseline.FromFindings([F("Lib.A", "x")]));
        var message = new LogMessage("Lib.A", "Error", 1, "Dymola reported an error") { Source = "Dymola" };

        Assert.Equal(FindingStatus.New, snapshot.StatusOf(message));
    }

    [Fact]
    public void EmptySnapshot_ClassifiesNothing()
    {
        Assert.Null(BaselineStatusSnapshot.Empty.StatusOf(F("Lib.A", "x").ToLogMessage()));
        Assert.False(BaselineStatusSnapshot.Empty.HasBaseline);
    }

    // --- mapping changed files to models --------------------------------------------------------

    [Fact]
    public void ModelsInFiles_MapsByGraphFileNode()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(_repoDir, "A.mo"), "model A \"a\" end A;");
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(_repoDir, "B.mo"), "model B \"b\" end B;");

        var models = BaselineStatusSnapshot.ModelsInFiles(graph, [Path.Combine(_repoDir, "A.mo")]);

        Assert.Equal(["A"], models);
    }

    [Fact]
    public void ModelsInFiles_IgnoresPathsTheGraphDoesNotKnow()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(_repoDir, "A.mo"), "model A \"a\" end A;");

        Assert.Empty(BaselineStatusSnapshot.ModelsInFiles(graph, [Path.Combine(_repoDir, "Nope.mo")]));
    }

    // --- the service's assembly of a snapshot ---------------------------------------------------

    private BaselineStatusService BuildService(params VcsWorkingCopyFile[] workingCopyChanges)
    {
        var libraries = new LibraryDataService();
        var library = libraries.AddLibraryFromFileAsync(
                Path.Combine(_repoDir, "Lib.mo"),
                "package Lib \"lib\"\n  model A \"a\"\n    parameter Real x = 1.0;\n  end A;\nend Lib;")
            .GetAwaiter().GetResult();

        var repository = new Repository { Name = "R", LocalPath = _repoDir, VcsRootPath = _repoDir };
        library.RepositoryId = repository.Id;

        var repositories = new Mock<IRepositoryService>();
        repositories.SetupGet(r => r.Repositories).Returns([repository]);
        repositories.Setup(r => r.GetWorkingCopyChanges(repository.Id)).Returns(workingCopyChanges.ToList());

        return new BaselineStatusService(libraries, repositories.Object, new Mock<IFileMonitoringService>().Object);
    }

    private void WriteBaseline(params Finding[] accepted)
        => Baseline.FromFindings(accepted).Save(Path.Combine(_repoDir, ".mlqt", "baseline.json"));

    [Fact]
    public void Refresh_WithNoBaselineFile_ClassifiesNothing()
    {
        var service = BuildService();

        service.Refresh();

        Assert.False(service.HasBaseline);
        Assert.Null(service.StatusOf(F("Lib.A", "x").ToLogMessage()));
    }

    [Fact]
    public void Refresh_LoadsTheRepositoryBaseline()
    {
        WriteBaseline(F("Lib.A", "x"));
        var service = BuildService();

        service.Refresh();

        Assert.True(service.HasBaseline);
        Assert.Equal(FindingStatus.AcceptedDebt, service.StatusOf(F("Lib.A", "x").ToLogMessage()));
        Assert.Equal(FindingStatus.New, service.StatusOf(F("Lib.A", "y").ToLogMessage()));
    }

    [Fact]
    public void Refresh_TreatsAPendingModificationAsTouched()
    {
        WriteBaseline(F("Lib.A", "x"));
        var service = BuildService(new VcsWorkingCopyFile { Path = "Lib.mo", Status = VcsFileStatus.Modified });

        service.Refresh();

        Assert.Equal(FindingStatus.TouchedDebt, service.StatusOf(F("Lib.A", "x").ToLogMessage()));
        Assert.Equal(1, service.TouchedFileCount);
    }

    [Fact]
    public void Refresh_TreatsAnUntrackedFileAsTouched()
    {
        // A new .mo file is as much "waiting to be committed" as a modified one.
        WriteBaseline(F("Lib.A", "x"));
        var service = BuildService(new VcsWorkingCopyFile { Path = "Lib.mo", Status = VcsFileStatus.Untracked });

        service.Refresh();

        Assert.Equal(FindingStatus.TouchedDebt, service.StatusOf(F("Lib.A", "x").ToLogMessage()));
    }

    [Fact]
    public void Refresh_IgnoresDeletedFilesAndNonModelicaFiles()
    {
        WriteBaseline(F("Lib.A", "x"));
        var service = BuildService(
            new VcsWorkingCopyFile { Path = "Lib.mo", Status = VcsFileStatus.Deleted },
            new VcsWorkingCopyFile { Path = "README.md", Status = VcsFileStatus.Modified });

        service.Refresh();

        Assert.Equal(0, service.TouchedFileCount);
        Assert.Equal(FindingStatus.AcceptedDebt, service.StatusOf(F("Lib.A", "x").ToLogMessage()));
    }

    [Fact]
    public void Refresh_SurvivesAMalformedBaseline()
    {
        // A broken file must not take the issues list down with it.
        File.WriteAllText(Path.Combine(_repoDir, ".mlqt", "baseline.json"), "{ not json");
        var service = BuildService();

        service.Refresh();

        Assert.False(service.HasBaseline);
    }

    [Fact]
    public void Refresh_RaisesOnChangedOnlyWhenTheAnswerMoves()
    {
        WriteBaseline(F("Lib.A", "x"));
        var service = BuildService(new VcsWorkingCopyFile { Path = "Lib.mo", Status = VcsFileStatus.Modified });
        var raised = 0;
        service.OnChanged += () => raised++;

        service.Refresh();      // nothing -> a baseline and a touched file
        var afterFirst = raised;
        service.Refresh();      // same again

        Assert.Equal(1, afterFirst);
        Assert.Equal(1, raised);   // no spurious second notification
    }
}

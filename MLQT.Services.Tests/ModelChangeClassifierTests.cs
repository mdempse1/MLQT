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

    // ---------------------------------------------------------------- a class moved between files (B350)

    private const string CommittedPackage = """
        within MyLib;
        package Components
          model Resistor "An ideal resistor"
            parameter Real R = 100;
          end Resistor;
          model Capacitor "An ideal capacitor"
            parameter Real C = 1;
          end Capacitor;
        end Components;
        """;

    /// <summary>The package after Split into files has taken the resistor out.</summary>
    private const string PackageWithoutResistor = """
        within MyLib;
        package Components
          model Capacitor "An ideal capacitor"
            parameter Real C = 1;
          end Capacitor;
        end Components;
        """;

    private const string SplitOutResistor = """
        within MyLib.Components;
        model Resistor "An ideal resistor"
          parameter Real R = 100;
        end Resistor;
        """;

    private void CommitPackage() =>
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", "Components/package.mo", It.IsAny<string?>()))
            .Returns(CommittedPackage);

    /// <summary>
    /// Split into files: the package keeps one class and the other moves to a file of its own, which
    /// is untracked. Compared with nothing, the moved class read as Added - the simulation marker, and
    /// hidden by the Cosmetic filter - for a change that alters nothing.
    /// </summary>
    [Fact]
    public void AClassSplitOutOfItsPackage_IsComparedWithWhereItCameFrom()
    {
        CommitPackage();
        Write(Path.Combine("Components", "package.mo"), PackageWithoutResistor);
        Write(Path.Combine("Components", "Resistor.mo"), SplitOutResistor);

        var kinds = Classify(
            Change("Components/package.mo"),
            Change("Components/Resistor.mo", VcsFileStatus.Untracked));

        // Cosmetic rather than Unchanged: moving it out of the package took away a level of
        // indentation, which is layout - and the Cosmetic filter is where a reviewer looks for that.
        Assert.Equal(ClassChangeKind.Cosmetic, kinds["MyLib.Components.Resistor"]);
        Assert.Equal(ClassChangeKind.Unchanged, kinds["MyLib.Components.Capacitor"]);
    }

    [Fact]
    public void AClassMovedAndEdited_IsTheEditNotAnAddition()
    {
        CommitPackage();
        Write(Path.Combine("Components", "package.mo"), PackageWithoutResistor);
        Write(Path.Combine("Components", "Resistor.mo"), SplitOutResistor.Replace("R = 100", "R = 220"));

        var kinds = Classify(
            Change("Components/package.mo"),
            Change("Components/Resistor.mo", VcsFileStatus.Untracked));

        Assert.Equal(ClassChangeKind.AffectsSimulation, kinds["MyLib.Components.Resistor"]);
    }

    /// <summary>
    /// Moved without telling version control: the old file is deleted and the new one untracked.
    /// </summary>
    [Fact]
    public void AFileMovedOnDisk_IsComparedWithTheDeletedOne()
    {
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", "Old.mo", It.IsAny<string?>()))
            .Returns(Committed);
        Write("New.mo", Committed);

        var kinds = Classify(Change("Old.mo", VcsFileStatus.Deleted), Change("New.mo", VcsFileStatus.Untracked));

        Assert.Equal(ClassChangeKind.Unchanged, kinds["MyLib.Resistor"]);
    }

    /// <summary>
    /// A staged rename (<c>git mv</c>) is reported at its new path, which has no committed version.
    /// </summary>
    [Fact]
    public void ARenamedFile_IsComparedWithItsOldPath()
    {
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string?)null);
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", "Old.mo", It.IsAny<string?>()))
            .Returns(Committed);
        Write("New.mo", Committed.Replace("{{0,0},{1,1}}", "{{0,0},{9,9}}"));

        var kinds = Classify(new VcsWorkingCopyFile { Path = "New.mo", OldPath = "Old.mo", Status = VcsFileStatus.Renamed });

        Assert.Equal(ClassChangeKind.Cosmetic, kinds["MyLib.Resistor"]);
    }

    [Fact]
    public void AClassNoCommittedFileHad_IsStillAdded()
    {
        // The positive control: looking elsewhere must not make everything look moved.
        CommitPackage();
        Write(Path.Combine("Components", "package.mo"), CommittedPackage);
        Write(Path.Combine("Components", "Inductor.mo"), """
            within MyLib.Components;
            model Inductor "An ideal inductor"
              parameter Real L = 1;
            end Inductor;
            """);

        var kinds = Classify(
            Change("Components/package.mo"),
            Change("Components/Inductor.mo", VcsFileStatus.Untracked));

        Assert.Equal(ClassChangeKind.Added, kinds["MyLib.Components.Inductor"]);
    }

    /// <summary>
    /// B350: nothing in production called Invalidate, so the cache kept every project's files for
    /// the rest of the session. A project change clears it.
    /// </summary>
    [Fact]
    public void AProjectChange_DiscardsTheCache()
    {
        Write("Resistor.mo", Committed);
        Classify(Change("Resistor.mo"));

        _repositories.Raise(r => r.OnProjectChanged += null, "another-project");
        Classify(Change("Resistor.mo"));

        _repositories.Verify(
            r => r.GetFileContentAtRevision("repo-1", "Resistor.mo", It.IsAny<string?>()), Times.Exactly(2));
    }

    // ---------------------------------------------------------------- a large change set
    // 17,372 changed files in an SVN checkout took eighteen minutes to classify, one svn process
    // after another, while the repository's tree waited for the answer.

    private VcsWorkingCopyFile[] ManyChangedFiles(int count)
    {
        var changes = new VcsWorkingCopyFile[count];
        for (var i = 0; i < count; i++)
        {
            Write($"R{i}.mo", Committed.Replace("Resistor", $"R{i}").Replace("R = 100", "R = 220"));
            changes[i] = Change($"R{i}.mo");
        }
        return changes;
    }

    [Fact]
    public void MoreChangedFilesThanTheCap_AreNotClassified_AndNothingIsRead()
    {
        // Empty is "not asked", so the files are still marked modified, without a kind.
        var classifier = new ModelChangeClassifier(_repositories.Object, maxFilesToClassify: 2);

        Assert.Empty(classifier.Classify(_repository, ManyChangedFiles(3)));
        _repositories.Verify(
            r => r.GetFileContentAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    /// <summary>Each file <c>Rn.mo</c> committed holding a class <c>Rn</c>, as ManyChangedFiles writes them.</summary>
    private void CommitEachFile() =>
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string, string?>((_, path, _) =>
                Committed.Replace("Resistor", Path.GetFileNameWithoutExtension(path)));

    [Fact]
    public void AsManyChangedFilesAsTheCap_AreClassified()
    {
        CommitEachFile();
        var classifier = new ModelChangeClassifier(_repositories.Object, maxFilesToClassify: 2);

        var kinds = classifier.Classify(_repository, ManyChangedFiles(2));

        Assert.Equal(ClassChangeKind.AffectsSimulation, kinds["MyLib.R0"]);
        Assert.Equal(ClassChangeKind.AffectsSimulation, kinds["MyLib.R1"]);
    }

    [Fact]
    public void OnlyModelicaFiles_CountTowardsTheCap()
    {
        var classifier = new ModelChangeClassifier(_repositories.Object, maxFilesToClassify: 2);
        Write("README.md", "not Modelica");

        var kinds = classifier.Classify(_repository, [.. ManyChangedFiles(2), Change("README.md")]);

        Assert.Contains("MyLib.R0", kinds);
    }

    [Fact]
    public void TheCommittedVersions_AreReadSeveralAtOnce()
    {
        var inFlight = 0;
        var mostAtOnce = 0;
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string, string?>((_, path, _) =>
            {
                var now = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref mostAtOnce, now);
                Thread.Sleep(50);
                Interlocked.Decrement(ref inFlight);
                return Committed.Replace("Resistor", Path.GetFileNameWithoutExtension(path));
            });

        var kinds = Classify(ManyChangedFiles(16));

        Assert.True(mostAtOnce > 1, $"the reads ran one at a time (at most {mostAtOnce} at once)");
        // And every file's answer is there, whichever read finished first.
        for (var i = 0; i < 16; i++)
            Assert.Equal(ClassChangeKind.AffectsSimulation, kinds[$"MyLib.R{i}"]);
    }

    [Fact]
    public void TwoRepositoriesInOneCheckoutAskingAtOnce_BothAnswer_ReadingEachFileOnce()
    {
        // Each used to read every committed version for itself, which is why two svn processes were
        // running side by side. The second waits for the first and finds its reads in the cache.
        _repositories
            .Setup(r => r.GetFileContentAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string, string?>((_, path, _) =>
            {
                Thread.Sleep(50);
                return Committed.Replace("Resistor", Path.GetFileNameWithoutExtension(path));
            });
        var changes = ManyChangedFiles(8);
        var sibling = Sibling();

        using var start = new Barrier(2);
        var callers = new[] { _repository, sibling }
            .Select(repository => Task.Run(() => { start.SignalAndWait(); return _classifier.Classify(repository, changes); },
                TestContext.Current.CancellationToken))
            .ToArray();
        Task.WaitAll(callers, TestContext.Current.CancellationToken);

        Assert.Equal(8, callers[0].Result.Count);
        Assert.Equal(callers[0].Result, callers[1].Result);
        for (var i = 0; i < 8; i++)
            _repositories.Verify(
                r => r.GetFileContentAtRevision(It.IsAny<string>(), $"R{i}.mo", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void ARequestOvertakenByANewerOneForTheSameRepository_StopsAndAnswersNotAsked()
    {
        // Every refresh of a tree asks again, and the browser drops all but the newest answer. The
        // older request used to queue for the gate holding a thread, and then run in full.
        var firstRead = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string, string?>((_, path, _) =>
            {
                firstRead.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return Committed.Replace("Resistor", Path.GetFileNameWithoutExtension(path));
            });
        var changes = ManyChangedFiles(64);
        var token = TestContext.Current.CancellationToken;

        var older = Task.Run(() => _classifier.Classify(_repository, changes), token);
        Assert.True(firstRead.Wait(TimeSpan.FromSeconds(10), token));

        // On a thread of its own, never the pool's: the older run's held reads fill the pool (four
        // and the run itself on a four-core runner), and a pool task queued behind them waited longer
        // than the sleep below for a thread - so it took its ticket after the release, the older run
        // finished in full, and the test failed on CI.
        var newer = Task.Factory.StartNew(() => _classifier.Classify(_repository, changes),
            token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Give the newer one time to take its ticket - its first statement - before the older one's
        // reads come back.
        Thread.Sleep(200);
        release.Set();
        Task.WaitAll([older, newer], token);

        // Overtaken, by reference: not an empty answer the browser would apply over the kinds it has.
        Assert.Same(IModelChangeClassifier.Overtaken, older.Result);
        Assert.Equal(64, newer.Result.Count);
        // What the older one read was kept for the newer one: nothing was read twice.
        for (var i = 0; i < 64; i++)
            _repositories.Verify(
                r => r.GetFileContentAtRevision("repo-1", $"R{i}.mo", It.IsAny<string?>()), Times.AtMostOnce());
    }

    /// <summary>A classification of 64 changed files whose committed-version reads are held.</summary>
    private (Task<IReadOnlyDictionary<string, ClassChangeKind>> Running, ManualResetEventSlim Release) StartHeldClassification()
    {
        var firstRead = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string, string?>((_, path, _) =>
            {
                firstRead.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return Committed.Replace("Resistor", Path.GetFileNameWithoutExtension(path));
            });
        var changes = ManyChangedFiles(64);
        var running = Task.Run(() => _classifier.Classify(_repository, changes), TestContext.Current.CancellationToken);
        Assert.True(firstRead.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        return (running, release);
    }

    [Fact]
    public void AProjectSwitchWhileAClassificationRuns_StopsIt()
    {
        // Invalidate cleared the request table, and a missing entry read as "not overtaken": the old
        // project's run carried on to the end, holding the checkout's gate. Worse, a run that had just
        // stopped for a newer request went back to being current and merged the files it never read.
        var (running, release) = StartHeldClassification();

        _classifier.Invalidate();
        release.Set();

        Assert.Same(IModelChangeClassifier.Overtaken, running.Result);
    }

    [Fact]
    public void ARequestWithNoChanges_OvertakesTheRunningOne()
    {
        // After a commit the status has no changes. The run for the changes there were is now for
        // nothing, and used to hold the gate to the end because the new request never took a ticket.
        var (running, release) = StartHeldClassification();

        Assert.Empty(_classifier.Classify(_repository, []));
        release.Set();

        Assert.Same(IModelChangeClassifier.Overtaken, running.Result);
    }

    [Fact]
    public void ARequestOverTheLimit_OvertakesTheRunningOne()
    {
        var (running, release) = StartHeldClassification();
        var classifier = _classifier;

        // Over the default limit by counting: the same classifier, so the same tickets.
        var tooMany = Enumerable.Range(0, ModelChangeClassifier.DefaultMaxFilesToClassify + 1)
            .Select(i => Change($"Big{i}.mo")).ToArray();
        Assert.Empty(classifier.Classify(_repository, tooMany));
        release.Set();

        Assert.Same(IModelChangeClassifier.Overtaken, running.Result);
    }

    // ---------------------------------------------------------------- a failed read

    [Fact]
    public void AModifiedFileWhoseCommittedVersionCannotBeRead_IsNotCalledAdded_AndIsAskedAgain()
    {
        // Version control answers null both for "there is no committed version" and for a read that
        // failed. A modified file has one, so null here is a failure - and taken as "none" every class
        // in it read as Added, the simulation marker, and was cached until the revision changed.
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string?)null);
        Write("Resistor.mo", Committed);

        Assert.Equal(ClassChangeKind.Unknown, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);

        // And the next refresh tries again, finding it this time.
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Committed);
        Assert.Equal(ClassChangeKind.Unchanged, Classify(Change("Resistor.mo"))["MyLib.Resistor"]);
    }

    [Fact]
    public void ARenamedFileWithNoOldPath_HasNoCommittedVersion_AndItsClassesAreAdded()
    {
        // The control: where nothing need be committed, null still means "none".
        _repositories
            .Setup(r => r.GetFileContentAtRevision("repo-1", It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((string?)null);
        Write("New.mo", Committed);

        var kinds = Classify(new VcsWorkingCopyFile { Path = "New.mo", Status = VcsFileStatus.Renamed });

        Assert.Equal(ClassChangeKind.Added, kinds["MyLib.Resistor"]);
    }

    [Fact]
    public void NewFiles_DoNotCountTowardsTheLimit()
    {
        // They cost no read. A pile of generated files beside two edited ones used to switch the
        // comparison off for the edited ones.
        CommitEachFile();
        var classifier = new ModelChangeClassifier(_repositories.Object, maxFilesToClassify: 2);
        Write("New1.mo", Committed.Replace("Resistor", "New1"));
        Write("New2.mo", Committed.Replace("Resistor", "New2"));
        Write("New3.mo", Committed.Replace("Resistor", "New3"));

        var kinds = classifier.Classify(_repository,
            [.. ManyChangedFiles(2),
             Change("New1.mo", VcsFileStatus.Untracked), Change("New2.mo", VcsFileStatus.Untracked),
             Change("New3.mo", VcsFileStatus.Added)]);

        Assert.Equal(ClassChangeKind.AffectsSimulation, kinds["MyLib.R0"]);
        Assert.Equal(ClassChangeKind.Added, kinds["MyLib.New1"]);
    }

    // ---------------------------------------------------------------- two repositories, one checkout

    /// <summary>A second library checked out in the same working copy (B330).</summary>
    private Repository Sibling() => new()
    {
        Id = "repo-2",
        Name = "Lib2",
        LocalPath = Path.Combine(_root, "Lib2"),
        VcsRootPath = _root + Path.DirectorySeparatorChar,   // written differently, the same folder
        VcsType = RepositoryVcsType.Git,
        CurrentRevision = "abc123",
    };

    [Fact]
    public void TwoRepositoriesInOneCheckout_ReadEachCommittedVersionOnce()
    {
        // Each is given the whole working copy's changes, so keyed by repository each read every
        // committed version for itself.
        _repositories
            .Setup(r => r.GetFileContentAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Committed);
        Write("Resistor.mo", Committed.Replace("R = 100", "R = 220"));

        var first = _classifier.Classify(_repository, [Change("Resistor.mo")]);
        var second = _classifier.Classify(Sibling(), [Change("Resistor.mo")]);

        Assert.Equal(first, second);
        _repositories.Verify(
            r => r.GetFileContentAtRevision(It.IsAny<string>(), "Resistor.mo", It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public void InvalidatingOneRepository_DropsWhatItsCheckoutShares()
    {
        _repositories
            .Setup(r => r.GetFileContentAtRevision(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Committed);
        Write("Resistor.mo", Committed);
        _classifier.Classify(_repository, [Change("Resistor.mo")]);

        // They are the same files, so dropping them for one drops them for the other too.
        _classifier.Invalidate("repo-1");
        _classifier.Classify(Sibling(), [Change("Resistor.mo")]);

        _repositories.Verify(
            r => r.GetFileContentAtRevision(It.IsAny<string>(), "Resistor.mo", It.IsAny<string?>()), Times.Exactly(2));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value
               && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }
}

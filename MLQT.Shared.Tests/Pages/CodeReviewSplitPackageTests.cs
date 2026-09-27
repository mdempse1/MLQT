using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using Moq;
using MLQT.Shared.Pages;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B242 — which findings offer <b>Split into files</b>.
///
/// <para>The work is <c>PackageSplitter</c>'s and is tested there against real files. What is here
/// is the page's own decision: which rows get the button. Offering it on a finding it cannot fix is
/// a button that reports failure; not offering it on one it can leaves the user with <b>Format All
/// Files</b>, which restructures the whole repository to correct one package.</para>
/// </summary>
public class CodeReviewSplitPackageTests
{
    private static LogMessage Finding(string ruleId, string source = LogMessage.StyleCheckingSource) =>
        new("Lib.Arrived", "Style warning", 1, "package Arrived is stored as a single file")
        {
            RuleId = ruleId,
            Source = source,
        };

    [Fact]
    public void ASingleFilePackageFindingOffersTheFix()
    {
        Assert.True(CodeReview.CanSplitPackage(Finding(RuleIds.SingleFilePackage)));
    }

    [Fact]
    public void EveryOtherRuleDoesNot()
    {
        // The button restructures files on disk. It belongs to one rule, and a finding that merely
        // mentions a package is not that rule.
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.PackageOrder)));
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.ClassDescription)));
        Assert.False(CodeReview.CanSplitPackage(Finding(RuleIds.UnusedClass)));
    }

    [Fact]
    public void AFindingFromSomewhereElseDoesNot()
    {
        // Parse diagnostics and external-tool results reach this list too. Only a style finding
        // carries a rule id this page can act on.
        Assert.False(CodeReview.CanSplitPackage(
            Finding(RuleIds.SingleFilePackage, source: "Dymola")));
    }

    [Fact]
    public void NothingIsNotAFinding()
    {
        Assert.False(CodeReview.CanSplitPackage(null));
    }

    // ── B306, B429: a library that is one file ────────────────────────────────────

    private static LoadedLibrary Library(LibrarySourceType type, string sourcePath) =>
        new() { SourceType = type, SourcePath = sourcePath };

    [Theory]
    [InlineData(LibrarySourceType.Git)]
    [InlineData(LibrarySourceType.SVN)]
    [InlineData(LibrarySourceType.File)]
    public void ALibraryLoadedFromOneFileCanBeSplit(LibrarySourceType type)
    {
        // Refused by B306 while the split left the library naming the file it deleted; since B429
        // the split re-registers it as the directory, as Format All does. What that does is held by
        // CodeReviewSplitSingleFileLibraryTests.
        var path = Path.Combine(Path.GetTempPath(), "mlqt-no-such-dir", "MyLib.mo");

        Assert.Null(CodeReview.WhyNotSplit(Library(type, path), "MyLib"));
    }

    // ── B429: when the split moves the library ────────────────────────────────────

    private static readonly string Parent = Path.Combine(Path.GetTempPath(), "mlqt-no-such-dir");
    private static readonly string SingleFile = Path.Combine(Parent, "MyLib.mo");
    private static readonly string Expanded = Path.Combine(Parent, "MyLib");

    private static ModelicaGraph.DataTypes.ModelNode Package(string id, string? parent = null) =>
        new(id, id.Split('.')[^1], $"package {id.Split('.')[^1]} end {id.Split('.')[^1]};")
        {
            ParentModelName = parent,
        };

    private static MLQT.Services.Helpers.PackageSplitter.SplitResult Expansion(params string[] removed) =>
        new([Path.Combine(Expanded, "package.mo"), Path.Combine(Expanded, "A.mo")], removed, Error: null);

    [Fact]
    public void ASplitThatDeletedTheLibrarysFile_MovesTheLibraryToTheNewDirectory()
    {
        Assert.Equal(Expanded, CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, SingleFile), Package("MyLib"), Expansion(SingleFile)));
    }

    [Fact]
    public void ALibraryThatIsADirectoryStaysWhereItIs()
    {
        // A package.mo split in place deletes nothing, and a directory library is not moved by one.
        Assert.Null(CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, Parent), Package("MyLib"), Expansion()));
    }

    [Fact]
    public void ASplitThatDidNotGoThroughMovesNothing()
    {
        var failed = new MLQT.Services.Helpers.PackageSplitter.SplitResult(
            [Path.Combine(Expanded, "package.mo")], [SingleFile], "could not delete");

        Assert.Null(CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, SingleFile), Package("MyLib"), failed));
    }

    [Fact]
    public void ASplitThatLeftTheLibrarysFileMovesNothing()
    {
        Assert.Null(CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, SingleFile), Package("MyLib"), Expansion()));
    }

    [Fact]
    public void ANestedPackageIsNotTheLibrary()
    {
        Assert.Null(CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, SingleFile), Package("MyLib.Sub", "MyLib"), Expansion(SingleFile)));
    }

    [Fact]
    public void NoPackageFileWrittenWhereTheLibraryWouldBe_MovesNothing()
    {
        // A root class whose name is not the directory the save wrote: never guess a directory.
        Assert.Null(CodeReview.LibraryExpandedBySplit(
            Library(LibrarySourceType.Git, SingleFile), Package("Other"), Expansion(SingleFile)));
    }

    [Theory]
    [InlineData(LibrarySourceType.Git)]
    [InlineData(LibrarySourceType.Directory)]
    public void ALibraryThatIsADirectoryCanBeSplit(LibrarySourceType type)
    {
        var path = Path.Combine(Path.GetTempPath(), "mlqt-no-such-dir", "MyLib");

        Assert.Null(CodeReview.WhyNotSplit(Library(type, path), "MyLib"));
    }

    [Fact]
    public void AnArchiveIsNeverWritten()
    {
        Assert.NotNull(CodeReview.WhyNotSplit(Library(LibrarySourceType.Zip, "lib.zip"), "MyLib"));
    }

    // ── B307: what the split tells the rest of the application ────────────────────

    [Fact]
    public void ASplitAnnouncesTheClassesItReloadedAndNothingElse()
    {
        // It announced the whole library, so the layout dropped every finding in it and re-checked
        // all of it. Only the classes the reload touched need that.
        var state = new AppState();
        var monitor = new Mock<IFileMonitoringService>();
        IReadOnlyList<string>? reanalysed = null;
        IReadOnlyCollection<string>? contentChanged = null;
        state.OnVcsModelsChanged += (_, models) => reanalysed = models;
        state.OnModelContentChanged += models => contentChanged = models;

        CodeReview.AnnounceReloadedModels(state, monitor.Object, "repo", ["Lib.P", "Lib.P.A"]);

        Assert.Equal(["Lib.P", "Lib.P.A"], reanalysed);
        Assert.Equal(["Lib.P", "Lib.P.A"], contentChanged);
    }

    [Fact]
    public void ASplitIsFileActivity()
    {
        // The monitor was paused across the write, so without this the library browser's VCS status
        // and the baseline never hear about the files the split created and deleted.
        var monitor = new Mock<IFileMonitoringService>();

        CodeReview.AnnounceReloadedModels(new AppState(), monitor.Object, "repo", ["Lib.P"]);

        monitor.Verify(m => m.NotifyFileActivity("repo"), Times.Once);
    }

    [Fact]
    public void NothingReloadedIsNothingToReanalyse()
    {
        var state = new AppState();
        var raised = false;
        state.OnVcsModelsChanged += (_, _) => raised = true;

        CodeReview.AnnounceReloadedModels(state, new Mock<IFileMonitoringService>().Object, "repo", []);

        Assert.False(raised);
    }

    [Fact]
    public void TheTwoRowActionsAreIndependent()
    {
        // Suppress and Split are offered on different sets, and a finding can have both: waiving the
        // rule and fixing it are different decisions, and the row should not force a choice.
        var finding = Finding(RuleIds.SingleFilePackage);

        Assert.True(CodeReview.CanSplitPackage(finding));
        Assert.True(CodeReview.CanSuppressRule(finding));
    }
}

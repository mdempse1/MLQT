using System.IO;
using MLQT.TestSupport;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using RevisionControl;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// <see cref="VcsChangeResolver"/> — the fallback chain that runs after every pull, switch, merge,
/// revert and update.
///
/// <para>These are the characterisation tests phase 7a-4 calls for before this code is disturbed:
/// they state what <c>MainLayout.OnVcsFilesChanged</c> has always done, quirks included, so a later
/// change to it has something to fail against. The behaviour they pin is not obvious from reading
/// the chain — most of it is about what the <em>second</em> set does not contain.</para>
/// </summary>
public class VcsChangeResolverTests
{
    private static FileChangeInfo Change(string path, FileChangeType type = FileChangeType.Modified) =>
        new() { FilePath = path, ChangeType = type };

    /// <summary>A graph in which every named file holds one class of the same name plus ".Class".</summary>
    private static Func<string, IEnumerable<string>> GraphHolding(params string[] knownFiles)
    {
        var known = knownFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return path => known.Contains(path) ? [path + ".Class"] : [];
    }

    private static readonly Func<IEnumerable<string>> NoVcsChanges = () => [];
    private static readonly Func<IEnumerable<string>> NoRepositoryModels = () => [];

    // ---- first source: the file monitor's pending changes --------------------------------------

    [Fact]
    public void PendingChanges_AreTheMostPreciseAnswer()
    {
        var result = VcsChangeResolver.Resolve(
            [Change("A.mo")], NoVcsChanges, () => ["Everything"], GraphHolding("A.mo"));

        Assert.Equal(VcsChangeSource.PendingChanges, result.Source);
        Assert.Equal(["A.mo.Class"], result.AffectedModelIds);
        Assert.Equal(["A.mo"], result.ChangedFilePaths);
    }

    [Fact]
    public void ADeletedFile_IsNotReAnalysedOrFormatted()
    {
        // Its classes are gone from the graph by the time this runs, so there is nothing to
        // re-analyse and nothing to write.
        var result = VcsChangeResolver.Resolve(
            [Change("A.mo", FileChangeType.Deleted)], NoVcsChanges, NoRepositoryModels, GraphHolding("A.mo"));

        Assert.Empty(result.AffectedModelIds);
        Assert.Empty(result.ChangedFilePaths);
    }

    [Fact]
    public void ANonModelicaChange_IsIgnored()
    {
        var readme = new FileChangeInfo { FilePath = "README.md", ChangeType = FileChangeType.Modified };
        Assert.False(readme.IsModelicaFile, "guard: the fixture has to be a non-Modelica file");

        var result = VcsChangeResolver.Resolve(
            [readme], NoVcsChanges, NoRepositoryModels, GraphHolding("README.md"));

        Assert.Empty(result.AffectedModelIds);
    }

    [Fact]
    public void AChangedFileOutsideTheLoadedLibrary_IsNotHandedToTheFormatter()
    {
        // The monitor watches the whole VCS root, which is wider than the library. A file outside it
        // is a real VCS change and not MLQT's to reformat.
        var result = VcsChangeResolver.Resolve(
            [Change("Inside.mo"), Change("Outside.mo")],
            NoVcsChanges, NoRepositoryModels, GraphHolding("Inside.mo"));

        Assert.Equal(["Inside.mo.Class"], result.AffectedModelIds);
        Assert.Equal(["Inside.mo"], result.ChangedFilePaths);
    }

    // ---- second source: the VCS's own status ---------------------------------------------------

    [Fact]
    public void WithNoPendingChanges_TheVcsStatusIsUsed()
    {
        // The monitor was paused before the operation, so it captured nothing.
        var result = VcsChangeResolver.Resolve(
            [], () => ["B.mo"], () => ["Everything"], GraphHolding("B.mo"));

        Assert.Equal(VcsChangeSource.VcsStatus, result.Source);
        Assert.Equal(["B.mo.Class"], result.AffectedModelIds);
        Assert.Equal(["B.mo"], result.ChangedFilePaths);
    }

    [Fact]
    public void PendingChangesThatResolveToNothing_FallThroughToTheVcs()
    {
        // A pending change list that is all deletions, or all outside the library, is not an answer.
        var result = VcsChangeResolver.Resolve(
            [Change("Gone.mo", FileChangeType.Deleted)],
            () => ["B.mo"], NoRepositoryModels, GraphHolding("B.mo", "Gone.mo"));

        Assert.Equal(VcsChangeSource.VcsStatus, result.Source);
        Assert.Equal(["B.mo.Class"], result.AffectedModelIds);
    }

    // ---- last resort: the whole repository -----------------------------------------------------

    [Fact]
    public void WhenNeitherSourceCanSay_EveryClassIsReAnalysed()
    {
        // A branch switch replaces every file, and neither the monitor nor the VCS reports a
        // meaningful diff.
        var result = VcsChangeResolver.Resolve(
            [], NoVcsChanges, () => ["Lib.A", "Lib.B"], GraphHolding());

        Assert.Equal(VcsChangeSource.WholeRepository, result.Source);
        Assert.Equal(["Lib.A", "Lib.B"], result.AffectedModelIds.OrderBy(x => x));
    }

    [Fact]
    public void TheWholeRepositoryFallback_HandsTheFormatterNothing()
    {
        // The load-bearing one. Re-analysing everything is cheap and correct; reformatting
        // everything on the strength of not knowing what changed would rewrite the working copy
        // after every branch switch. "Affected" and "changed" are not the same set, and this is why.
        var result = VcsChangeResolver.Resolve(
            [], NoVcsChanges, () => ["Lib.A", "Lib.B"], GraphHolding());

        Assert.NotEmpty(result.AffectedModelIds);
        Assert.Empty(result.ChangedFilePaths);
    }

    [Fact]
    public void VcsPathsThatResolveToNoClasses_StayOnTheFormattersList()
    {
        // Characterisation, not endorsement: MainLayout has always left these in place while
        // falling through to the whole-repository case. A file the VCS reports and the graph does
        // not know is one the formatter finds nothing in, so the two agree in practice — but a
        // refactor is the wrong place to discover otherwise, so the behaviour is preserved.
        var result = VcsChangeResolver.Resolve(
            [], () => ["Unknown.mo"], () => ["Lib.A"], GraphHolding());

        Assert.Equal(VcsChangeSource.WholeRepository, result.Source);
        Assert.Equal(["Lib.A"], result.AffectedModelIds);
        Assert.Equal(["Unknown.mo"], result.ChangedFilePaths);
    }

    [Fact]
    public void WhenThereIsGenuinelyNothing_TheSourceSaysSo()
    {
        var result = VcsChangeResolver.Resolve([], NoVcsChanges, NoRepositoryModels, GraphHolding());

        Assert.Equal(VcsChangeSource.Nothing, result.Source);
        Assert.Empty(result.AffectedModelIds);
    }

    // ---- ordering ------------------------------------------------------------------------------

    [Fact]
    public void TheSourcesAreTriedInOrderOfPrecision()
    {
        // All three could answer; the monitor's answer wins, and the other two are never consulted.
        var vcsAsked = false;
        var repositoryAsked = false;

        var result = VcsChangeResolver.Resolve(
            [Change("A.mo")],
            () => { vcsAsked = true; return ["B.mo"]; },
            () => { repositoryAsked = true; return ["Lib.Everything"]; },
            GraphHolding("A.mo", "B.mo"));

        Assert.Equal(["A.mo.Class"], result.AffectedModelIds);
        Assert.False(vcsAsked, "the VCS should not be consulted when the monitor answered");
        Assert.False(repositoryAsked, "the repository should not be walked when the monitor answered");
    }

    [Fact]
    public void FilePathsAreComparedWithoutRegardToCase()
    {
        // Windows hands back whatever case the operation used; the same file reported twice must
        // not be formatted twice.
        var result = VcsChangeResolver.Resolve(
            [Change("Lib.mo"), Change("LIB.MO")], NoVcsChanges, NoRepositoryModels, GraphHolding("Lib.mo", "LIB.MO"));

        Assert.Single(result.ChangedFilePaths);
    }
}

/// <summary>
/// <see cref="VcsChangeResolver.FormattableModelicaFiles"/> — which files a VCS status report names
/// that the formatter may actually rewrite.
///
/// <para>Four narrowings, each easy to drop without noticing and each with a consequence: the
/// formatter writes to a user's working copy.</para>
/// </summary>
public class FormattableModelicaFilesTests
{
    // Rooted for the running platform. A Windows literal is a relative path on Linux, so the
    // resolver combined it with the working directory and matched nothing - and the four tests here
    // that assert Empty passed anyway, for the wrong reason. See TestPaths.
    private static readonly string Root = TestPaths.Rooted("wc");
    private static readonly string Library = TestPaths.Rooted("wc", "Lib");

    private static VcsWorkingCopyFile Change(string path, VcsFileStatus status = VcsFileStatus.Modified) =>
        new() { Path = path, Status = status };

    private static HashSet<string> Resolve(params VcsWorkingCopyFile[] changes) =>
        VcsChangeResolver.FormattableModelicaFiles(Library, Root, changes, _ => true);

    [Fact]
    public void AModifiedModelicaFileInTheLibrary_IsFormattable()
    {
        Assert.Equal(
            [TestPaths.Rooted("wc", "Lib", "Thing.mo")],
            Resolve(Change(TestPaths.Relative("Lib", "Thing.mo"))));
    }

    [Fact]
    public void PathsAreResolvedAgainstTheVcsRoot_NotTheLibrary()
    {
        // The VCS reports paths relative to its own root, which can be a parent of the library.
        // Combining them with the library path instead gives wc/Lib/Lib/Thing.mo, which exists
        // nowhere and quietly formats nothing.
        var paths = Resolve(Change(TestPaths.Relative("Lib", "Thing.mo")));

        Assert.Equal([TestPaths.Rooted("wc", "Lib", "Thing.mo")], paths);
    }

    [Fact]
    public void AFileInASiblingLibrary_IsNotFormattable()
    {
        // One working copy can hold several libraries. A sibling's files are real VCS changes and
        // are not this repository's to rewrite.
        Assert.Empty(Resolve(Change(TestPaths.Relative("Other", "Thing.mo"))));
    }

    [Fact]
    public void ADeletedFile_IsNotFormattable()
    {
        Assert.Empty(Resolve(Change(TestPaths.Relative("Lib", "Gone.mo"), VcsFileStatus.Deleted)));
    }

    // Forward slashes, normalised in the body: an attribute argument has to be a compile-time
    // constant, so it cannot call TestPaths. The middle case used to read "Lib" + a backslash + "r",
    // which a heredoc turned into a real newline when this file was written - leaving a verbatim
    // string spanning two lines. It compiled, and the test asserted Empty on a filename containing a
    // newline, so it passed while checking nothing at all.
    [Theory]
    [InlineData("Lib/script.mos")]
    [InlineData("Lib/readme.md")]
    [InlineData("Lib/Resources/data.csv")]
    public void ANonModelicaFile_IsNotFormattable(string path)
    {
        // A working copy holds scripts, resources and documentation; the formatter would make
        // nonsense of any of them.
        Assert.Empty(Resolve(Change(path.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void AFileThatIsNoLongerThere_IsNotFormattable()
    {
        // A rename reports the old path too, and some clients report a moved-away file as changed.
        var paths = VcsChangeResolver.FormattableModelicaFiles(
            Library, Root, [Change(TestPaths.Relative("Lib", "Moved.mo"))], _ => false);

        Assert.Empty(paths);
    }

    [Fact]
    public void ARepositoryWithNoLocalPath_HasNothingToFormat()
    {
        var paths = VcsChangeResolver.FormattableModelicaFiles(
            "", Root, [Change(TestPaths.Relative("Lib", "Thing.mo"))], _ => true);

        Assert.Empty(paths);
    }

    [Fact]
    public void TheSameFileReportedTwice_IsFormattedOnce()
    {
        // Git reports a file staged and modified as two entries; formatting it twice is wasted work
        // and a second write the file monitor has to ignore.
        var paths = Resolve(
            Change(TestPaths.Relative("Lib", "Thing.mo")),
            Change(TestPaths.Relative("Lib", "Thing.mo"), VcsFileStatus.Added));

        Assert.Single(paths);
    }

    [Fact]
    public void TheExtensionIsMatchedRegardlessOfCase()
    {
        Assert.Single(Resolve(Change(TestPaths.Relative("Lib", "Thing.MO"))));
    }
}

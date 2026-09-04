using Moq;
using MLQT.Services.Checking;
using RevisionControl.Interfaces;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Which lines a change touched, for the review comments that have to land inside a pull request's
/// diff.
///
/// <para>The VCS layer's half of this has had tests since it landed, and the review formatter's half
/// is covered end to end — the resolver between them had none, so the branches nobody sees on a good
/// day were the untested ones: a working copy that is not Git, a diff that cannot be taken, and a
/// file that sits outside the repository and therefore has no path a forge could resolve.</para>
/// </summary>
public class ChangedLineResolverTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mlqt-lines-repo");
    private static readonly string Library = Path.Combine(Root, "Lib");

    /// <summary>A Git-shaped system: it owns the path and can answer a line-level diff.</summary>
    private static IRevisionControlSystem Git(
        IReadOnlyDictionary<string, IReadOnlySet<int>>? lines)
    {
        var vcs = new Mock<IRevisionControlSystem>(MockBehavior.Loose);
        vcs.As<ILineLevelDiff>()
            .Setup(v => v.GetChangedLinesSince(Root, It.IsAny<string>()))
            .Returns(lines);
        vcs.Setup(v => v.FindRepositoryRoot(It.IsAny<string>())).Returns(Root);
        vcs.Setup(v => v.IsValidRepository(It.IsAny<string>())).Returns(true);
        return vcs.Object;
    }

    /// <summary>An SVN-shaped system: it owns the path but cannot do a line-level diff.</summary>
    private static IRevisionControlSystem Svn()
    {
        var vcs = new Mock<IRevisionControlSystem>(MockBehavior.Loose);
        vcs.Setup(v => v.FindRepositoryRoot(It.IsAny<string>())).Returns(Root);
        vcs.Setup(v => v.IsValidRepository(It.IsAny<string>())).Returns(true);
        return vcs.Object;
    }

    /// <summary>A system that owns nothing.</summary>
    private static IRevisionControlSystem Nothing()
    {
        var vcs = new Mock<IRevisionControlSystem>(MockBehavior.Loose);
        vcs.Setup(v => v.FindRepositoryRoot(It.IsAny<string>())).Returns((string?)null);
        return vcs.Object;
    }

    private static Dictionary<string, IReadOnlySet<int>> Lines(string file, params int[] lines) =>
        new(StringComparer.OrdinalIgnoreCase) { [file] = new HashSet<int>(lines) };

    // ---- resolving ------------------------------------------------------------------------------

    [Fact]
    public void AGitWorkingCopyGivesTheChangedLinesAndTheRoot()
    {
        var file = Path.Combine(Library, "A.mo");

        var result = ChangedLineResolver.Resolve(Library, "main", Git(Lines(file, 3, 4)));

        Assert.True(result.Ok);
        Assert.Equal(Root, result.RepositoryRoot);
        Assert.True(result.Covers(file, 3));
        Assert.False(result.Covers(file, 5));
    }

    [Fact]
    public void ADiffThatCouldNotBeTakenIsNotACleanRun()
    {
        // Null is "could not be worked out". Reporting it as an empty diff produces a review that
        // comments on nothing and reads exactly like one with nothing to say.
        var result = ChangedLineResolver.Resolve(Library, "main", Git(null));

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Contains("fetch-depth", result.Error);   // the cause a CI checkout actually hits
    }

    [Fact]
    public void AnSvnWorkingCopyIsRefusedWithTheReason()
    {
        var result = ChangedLineResolver.Resolve(Library, "main", Svn());

        Assert.False(result.Ok);
        Assert.Contains("needs Git", result.Error);
    }

    [Fact]
    public void SomewhereThatIsNotAWorkingCopyAtAllSaysThat()
    {
        var result = ChangedLineResolver.Resolve(Library, "main", Nothing());

        Assert.False(result.Ok);
        Assert.Contains("not inside a Git working copy", result.Error);
    }

    [Fact]
    public void AFailedResolveCoversNothingRatherThanThrowing()
    {
        var result = ChangedLineResolver.Resolve(Library, "main", Nothing());

        Assert.False(result.Covers(Path.Combine(Library, "A.mo"), 1));
        Assert.Null(result.RepositoryRelativePath(Path.Combine(Library, "A.mo")));
    }

    // ---- the path a forge is given --------------------------------------------------------------

    [Fact]
    public void APathInsideTheRepositoryIsGivenRelativeWithForwardSlashes()
    {
        var file = Path.Combine(Library, "Sub", "A.mo");

        var result = ChangedLineResolver.Resolve(Library, "main", Git(Lines(file, 1)));

        Assert.Equal("Lib/Sub/A.mo", result.RepositoryRelativePath(file));
    }

    [Fact]
    public void APathOutsideTheRepositoryHasNoneAtAll()
    {
        // A dependency loaded from elsewhere. Null is what stops the review formatter offering the
        // comment - a path a forge cannot resolve fails the whole review with a 422, losing the
        // other forty comments with it.
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Other.mo");

        var result = ChangedLineResolver.Resolve(Library, "main", Git(Lines(outside, 1)));

        Assert.True(result.Ok);
        Assert.Null(result.RepositoryRelativePath(outside));
    }

    [Fact]
    public void CoveringIsAskedOfTheFileTheDiffNamed()
    {
        // The diff's keys come from the VCS layer, which builds them with the working directory and
        // an OS-appropriate comparer; a finding's path comes from ClassLocation, which normalises
        // through Path.GetFullPath. They have to meet.
        var file = Path.Combine(Library, "A.mo");

        var result = ChangedLineResolver.Resolve(Library, "main", Git(Lines(file, 7)));

        Assert.True(result.Covers(Path.GetFullPath(file), 7));
        Assert.False(result.Covers(Path.Combine(Library, "B.mo"), 7));
    }
}

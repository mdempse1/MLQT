using Moq;
using MLQT.Services.Checking;
using RevisionControl.Interfaces;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Which models a change touched, for the baseline ratchet's touched-debt escalation.
///
/// <para>The branch that matters most here is the one a real repository will not perform on demand: a
/// diff that <b>fails</b>. <c>GetChangedFilePathsSince</c> was changed to return null for that, rather
/// than an empty list, precisely because the two must not read alike — a broken diff in CI escalated
/// no touched debt, credited no fixed entry, and passed the build looking exactly like a run with
/// nothing to say. The VCS layer's half of that has had tests since it landed; this half had none,
/// because the resolver reached the systems through a static and could not be given a failing one.</para>
/// </summary>
public class ChangedModelResolverTests
{
    private const string Root = @"C:\repo";
    private const string Library = @"C:\repo\Lib";

    private static Mock<IRevisionControlSystem> Vcs(
        IReadOnlyList<string>? changedPaths, string? resolvedRevision = "abc123")
    {
        var vcs = new Mock<IRevisionControlSystem>(MockBehavior.Loose);
        vcs.Setup(v => v.FindRepositoryRoot(It.IsAny<string>())).Returns(Root);
        vcs.Setup(v => v.IsValidRepository(It.IsAny<string>())).Returns(true);
        vcs.Setup(v => v.ResolveRevision(Root, It.IsAny<string>())).Returns(resolvedRevision);
        vcs.Setup(v => v.GetChangedFilePathsSince(Root, It.IsAny<string>())).Returns(changedPaths);
        return vcs;
    }

    private static Dictionary<string, string> Map() => new(StringComparer.Ordinal)
    {
        ["Lib.A"] = Path.Combine(Library, "A.mo"),
        ["Lib.B"] = Path.Combine(Library, "B.mo"),
    };

    [Fact]
    public void AChangedFileEscalatesItsModels()
    {
        var result = ChangedModelResolver.Resolve(
            Library, "main", Map(), Vcs([Path.Combine(Library, "A.mo")]).Object);

        Assert.True(result.Ok);
        Assert.Equal(1, result.ChangedFileCount);
        Assert.Equal(["Lib.A"], result.ChangedModelIds);
    }

    [Fact]
    public void ADiffThatFails_StopsTheRun()
    {
        // Null is "could not take the diff". Reporting it as a clean run is what B14 was about.
        var result = ChangedModelResolver.Resolve(Library, "main", Map(), Vcs(null).Object);

        Assert.False(result.Ok);
        Assert.Empty(result.ChangedModelIds);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ADiffThatFails_NamesTheUsualCause()
    {
        // Almost always a shallow CI checkout, which is not something the message can be left to
        // imply: the ref resolves, the diff comes back empty-looking, and nothing else says why.
        var result = ChangedModelResolver.Resolve(Library, "origin/main", Map(), Vcs(null).Object);

        Assert.Contains("origin/main", result.Error!);
        Assert.Contains("fetch-depth: 0", result.Error!);
    }

    [Fact]
    public void AnEmptyDiff_IsACleanRun_NotAFailure()
    {
        // The other side of the same coin: nothing changed is a perfectly good answer, and has to be
        // distinguishable from the one above.
        var result = ChangedModelResolver.Resolve(Library, "main", Map(), Vcs([]).Object);

        Assert.True(result.Ok);
        Assert.Empty(result.ChangedModelIds);
        Assert.Equal(0, result.ChangedFileCount);
        Assert.Null(result.Error);
    }

    [Fact]
    public void AnUnresolvableRef_StopsTheRun_BeforeDiffing()
    {
        var vcs = Vcs([Path.Combine(Library, "A.mo")], resolvedRevision: null);

        var result = ChangedModelResolver.Resolve(Library, "no-such-branch", Map(), vcs.Object);

        Assert.False(result.Ok);
        Assert.Contains("no-such-branch", result.Error!);
        vcs.Verify(v => v.GetChangedFilePathsSince(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void APathOutsideAnyWorkingCopy_StopsTheRun()
    {
        var vcs = new Mock<IRevisionControlSystem>(MockBehavior.Loose);
        vcs.Setup(v => v.FindRepositoryRoot(It.IsAny<string>())).Returns((string?)null);

        var result = ChangedModelResolver.Resolve(Library, "main", Map(), vcs.Object);

        Assert.False(result.Ok);
        Assert.Contains("not inside a Git or SVN working copy", result.Error!);
    }

    [Fact]
    public void NonModelicaChangesAreIgnored()
    {
        // A README or a CI file changing does not touch any model's debt.
        var result = ChangedModelResolver.Resolve(
            Library, "main", Map(),
            Vcs([Path.Combine(Root, "README.md"), Path.Combine(Root, ".github", "ci.yml")]).Object);

        Assert.True(result.Ok);
        Assert.Empty(result.ChangedModelIds);
        Assert.Equal(0, result.ChangedFileCount);
    }

    [Fact]
    public void AChangedFileThatHoldsSeveralClassesEscalatesAllOfThem()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Lib"] = Path.Combine(Library, "package.mo"),
            ["Lib.A"] = Path.Combine(Library, "package.mo"),
        };

        var result = ChangedModelResolver.Resolve(
            Library, "main", map, Vcs([Path.Combine(Library, "package.mo")]).Object);

        Assert.Equal(["Lib", "Lib.A"], result.ChangedModelIds.OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>
    /// The VCS reports paths its own way and the graph stores them the way the library was loaded, so
    /// the two only meet after normalisation. Phase 3 listed this as a risk before either existed.
    /// </summary>
    [Fact]
    public void PathsAreMatchedAfterNormalisation()
    {
        var reported = Path.Combine(Root, "Lib", ".", "A.mo");   // same file, spelt differently

        var result = ChangedModelResolver.Resolve(Library, "main", Map(), Vcs([reported]).Object);

        Assert.Equal(["Lib.A"], result.ChangedModelIds);
    }

    [Fact]
    public void OnWindowsTheMatchIgnoresCase()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var result = ChangedModelResolver.Resolve(
            Library, "main", Map(), Vcs([Path.Combine(Library, "a.MO")]).Object);

        Assert.Equal(["Lib.A"], result.ChangedModelIds);
    }
}

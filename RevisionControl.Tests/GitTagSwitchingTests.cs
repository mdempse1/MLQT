using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// B193 — a Git tag is somewhere you can switch to, and the UI can say where you ended up.
/// </summary>
/// <remarks>
/// <para>Switching to a tagged version was not offered at all: <c>GetBranches</c> enumerated
/// branches, and Git keeps tags in a different ref namespace, so a released version was reachable
/// from TortoiseGit and not from MLQT. SVN never had the problem — its tags are directories and
/// arrive as ordinary <c>tags/*</c> entries.</para>
///
/// <para><b>The second half is the part that is easy to leave out.</b> Checking out a tag detaches
/// HEAD, so <c>GetCurrentBranch</c> correctly returns null and the browser showed a blank where the
/// branch name goes. Offering the switch without that is offering a state the application cannot
/// describe.</para>
/// </remarks>
public class GitTagSwitchingTests : IDisposable
{
    private readonly GitRevisionControlSystem _git = new();
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
            }
            catch { }
        }
    }

    /// <summary>A repository with two commits, the first of them tagged v1.0.0.</summary>
    private (Repository Repo, string Path, string TaggedSha) Tagged(bool annotated = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"GitTag_{Guid.NewGuid():N}");
        _tempPaths.Add(path);
        Repository.Init(path);

        var repo = new Repository(path);
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);

        File.WriteAllText(Path.Combine(path, "package.mo"), "package P \"v1\" end P;");
        Commands.Stage(repo, "package.mo");
        var first = repo.Commit("first", sig, sig);

        if (annotated)
            repo.Tags.Add("v1.0.0", first, sig, "the released version");
        else
            repo.Tags.Add("v1.0.0", first);

        File.WriteAllText(Path.Combine(path, "package.mo"), "package P \"v2\" end P;");
        Commands.Stage(repo, "package.mo");
        repo.Commit("second", sig, sig);

        return (repo, path, first.Sha);
    }

    [Fact]
    public void TagsAreListedBesideBranches()
    {
        var (repo, path, _) = Tagged();
        using (repo)
        {
            var refs = _git.GetBranches(path);

            var tag = Assert.Single(refs, b => b.IsTag);
            Assert.Equal("v1.0.0", tag.Name);
            Assert.Contains(refs, b => !b.IsTag);   // the branch is still there
        }
    }

    [Theory]
    [InlineData(false)]   // lightweight: the ref points straight at the commit
    [InlineData(true)]    // annotated: it points at a tag object that points at the commit
    public void BothKindsOfTagAreListed(bool annotated)
    {
        // The difference is invisible to a user and easy to get wrong in code: an annotated tag's
        // Target is a TagAnnotation, not a Commit, so anything reading Target directly loses them.
        var (repo, path, sha) = Tagged(annotated);
        using (repo)
        {
            var tag = Assert.Single(_git.GetBranches(path), b => b.IsTag);

            Assert.Equal("v1.0.0", tag.Name);
            Assert.Equal(sha, tag.LastCommit);
        }
    }

    [Fact]
    public void SwitchingToATagChecksOutThatRevision()
    {
        var (repo, path, sha) = Tagged();
        using (repo)
        {
            var result = _git.SwitchBranch(path, "v1.0.0");

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("package P \"v1\" end P;", File.ReadAllText(Path.Combine(path, "package.mo")));
            Assert.Equal(sha, _git.GetCurrentRevision(path));
        }
    }

    [Fact]
    public void SwitchingToATagLeavesNoBranch()
    {
        // Stated on its own because it is the half that has to reach the UI: there is no branch to
        // name afterwards, and something has to be shown in its place.
        var (repo, path, _) = Tagged();
        using (repo)
        {
            _git.SwitchBranch(path, "v1.0.0");

            Assert.Null(_git.GetCurrentBranch(path));
        }
    }

    [Fact]
    public void TheDetachedHeadIsDescribedByItsTag()
    {
        var (repo, path, _) = Tagged();
        using (repo)
        {
            _git.SwitchBranch(path, "v1.0.0");

            // The tag, not the commit id: it is what the user chose, and "a1b2c3d" answers a
            // question nobody asked.
            Assert.Equal("v1.0.0", _git.GetDetachedHeadLabel(path));
        }
    }

    [Fact]
    public void ADetachedHeadWithNoTagIsDescribedByItsCommit()
    {
        var (repo, path, sha) = Tagged();
        using (repo)
        {
            // Checked out by revision rather than by tag - the other way to detach, and the browser
            // has to say something then too.
            repo.Tags.Remove("v1.0.0");
            Commands.Checkout(repo, repo.Lookup<Commit>(sha));

            Assert.Equal(sha[..7], _git.GetDetachedHeadLabel(path));
        }
    }

    [Fact]
    public void OnABranchThereIsNothingToDescribe()
    {
        var (repo, path, _) = Tagged();
        using (repo)
        {
            Assert.Null(_git.GetDetachedHeadLabel(path));
        }
    }

    [Fact]
    public void TheTagYouAreSittingOnIsTheCurrentOne()
    {
        // So the selector can mark it, and so ExcludeCurrentBranch stops you switching to where you
        // already are.
        var (repo, path, _) = Tagged();
        using (repo)
        {
            _git.SwitchBranch(path, "v1.0.0");

            var tag = Assert.Single(_git.GetBranches(path), b => b.IsTag);
            Assert.True(tag.IsCurrent);
        }
    }

    [Fact]
    public void AMissingRefSaysItLookedForBoth()
    {
        var (repo, path, _) = Tagged();
        using (repo)
        {
            var result = _git.SwitchBranch(path, "v9.9.9");

            Assert.False(result.Success);
            Assert.Contains("tag", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        }
    }
}

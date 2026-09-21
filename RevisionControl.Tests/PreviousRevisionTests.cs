using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// B202 — which revision a commit is compared against to show what it changed.
/// </summary>
/// <remarks>
/// <para>The VCS History dialog used to diff a revision against the <b>working copy</b>, which
/// against an old commit is mostly other people's later work. Showing what the commit changed needs
/// the revision before it, and the two systems answer that differently enough that the caller must
/// not be doing it: <b>Git</b> has no ordering to count backwards along and <b>SVN</b> has no
/// parents recorded at all.</para>
///
/// <para>The SVN half of this runs anywhere, which is unusual for these suites — it is arithmetic
/// on a revision number and needs neither a working copy nor a server, so it is tested here rather
/// than left to the SVN tests that a CI runner cannot run.</para>
/// </remarks>
public class PreviousRevisionTests : IDisposable
{
    private readonly GitRevisionControlSystem _git = new();
    private readonly SvnRevisionControlSystem _svn = new();
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

    private string NewRepoPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"PrevRev_{Guid.NewGuid():N}");
        _tempPaths.Add(path);
        return path;
    }

    private (Repository Repo, string Path) NewRepo()
    {
        var path = NewRepoPath();
        Repository.Init(path);
        return (new Repository(path), path);
    }

    private static string Commit(Repository repo, string repoPath, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repoPath, file), content);
        Commands.Stage(repo, file);
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        return repo.Commit(message, sig, sig).Sha;
    }

    [Fact]
    public void Git_APreviousCommitIsTheOneBeforeIt()
    {
        var (repo, path) = NewRepo();
        using (repo)
        {
            var first = Commit(repo, path, "A.mo", "model A end A;", "first");
            var second = Commit(repo, path, "A.mo", "model A edited end A;", "second");

            Assert.Equal(first, _git.GetPreviousRevision(path, second));
        }
    }

    [Fact]
    public void Git_TheFirstCommitHasNothingBeforeIt()
    {
        // Not a failure: the dialog shows the whole file as added, which is what happened.
        var (repo, path) = NewRepo();
        using (repo)
        {
            var first = Commit(repo, path, "A.mo", "model A end A;", "first");

            Assert.Null(_git.GetPreviousRevision(path, first));
        }
    }

    [Fact]
    public void Git_AMergeIsComparedAgainstTheBranchItWasMadeOn()
    {
        // The first parent, not either parent. Diffing a merge against the branch that was merged in
        // reports the lines the merge brought over as though the merge commit had written them.
        var (repo, path) = NewRepo();
        using (repo)
        {
            var baseCommit = Commit(repo, path, "A.mo", "model A end A;", "base");

            var side = repo.CreateBranch("side");
            Commands.Checkout(repo, side);
            Commit(repo, path, "B.mo", "model B end B;", "on the side branch");

            Commands.Checkout(repo, repo.Branches["master"] ?? repo.Branches["main"]);
            var onMain = Commit(repo, path, "A.mo", "model A edited end A;", "on main");

            var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
            var merge = repo.Merge(repo.Branches["side"], sig);

            Assert.Equal(MergeStatus.NonFastForward, merge.Status);
            Assert.Equal(onMain, _git.GetPreviousRevision(path, merge.Commit!.Sha));
            Assert.NotEqual(baseCommit, _git.GetPreviousRevision(path, merge.Commit.Sha));
        }
    }

    [Fact]
    public void Git_ARevisionThatDoesNotExistHasNoPredecessor()
    {
        var (repo, path) = NewRepo();
        using (repo)
        {
            Commit(repo, path, "A.mo", "model A end A;", "first");

            Assert.Null(_git.GetPreviousRevision(path, "0123456789abcdef0123456789abcdef01234567"));
        }
    }

    [Fact]
    public void Git_APathThatIsNotARepositoryHasNoPredecessor()
    {
        Assert.Null(_git.GetPreviousRevision(NewRepoPath(), "HEAD"));
    }

    [Theory]
    [InlineData("4711", "4710")]
    [InlineData("2", "1")]
    public void Svn_TheRevisionBeforeIsTheNumberBefore(string revision, string expected)
    {
        // Revision numbers are global, so N-1 is the state of the repository before N whether or not
        // N-1 touched this file - which is what "before this commit" means.
        Assert.Equal(expected, _svn.GetPreviousRevision("any", revision));
    }

    [Fact]
    public void Svn_RevisionOneHasNothingBeforeIt()
    {
        Assert.Null(_svn.GetPreviousRevision("any", "1"));
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("BASE")]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void Svn_ARevisionItCannotCountBackFromHasNoPredecessor(string revision)
    {
        // Guessing here would produce a real revision number that is not the one asked about.
        Assert.Null(_svn.GetPreviousRevision("any", revision));
    }
}

using LibGit2Sharp;
using RevisionControl;

namespace RevisionControl.Tests;

/// <summary>
/// What "changed since a revision" means: the line-level diff a PR review needs
/// (<see cref="GitRevisionControlSystem.GetChangedLinesSince"/>) and the file-level one the ratchet
/// runs off. Both are measured from the merge base, and both have to be exact rather than
/// approximately right — a comment on a line outside the pull request's diff is rejected by the
/// forge, and the rejection fails the whole review; a file wrongly reported as changed escalates
/// somebody else's debt to this author.
/// </summary>
public class GitChangedLinesTests : IDisposable
{
    private readonly GitRevisionControlSystem _git = new();
    private readonly List<string> _paths = new();

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                Directory.Delete(path, recursive: true);
            }
            catch { }
        }
    }

    private string NewRepo()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitLines_" + Guid.NewGuid().ToString("N"));
        _paths.Add(path);
        Repository.Init(path);
        return path;
    }

    private static readonly Signature Who = new("Test", "test@example.com", DateTimeOffset.Now);

    private static void Commit(string repoPath, string relative, string content, string message)
    {
        var full = Path.Combine(repoPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        using var repo = new Repository(repoPath);
        Commands.Stage(repo, relative);
        repo.Commit(message, Who, Who);
    }

    private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public void TheLineNumbersAreTheOnesInTheWorkingCopy()
    {
        var repoPath = NewRepo();
        Commit(repoPath, "a.txt", Lines("one", "two", "three"), "base");
        using (var repo = new Repository(repoPath)) repo.CreateBranch("feature");
        Commit(repoPath, "a.txt", Lines("one", "inserted", "two", "three"), "insert");

        var changed = _git.GetChangedLinesSince(repoPath, "feature");

        Assert.NotNull(changed);
        var file = Path.GetFullPath(Path.Combine(repoPath, "a.txt"));
        // "inserted" is line 2 of the new file; nothing else moved in content.
        Assert.Equal(new[] { 2 }, changed![file].Order());
    }

    [Fact]
    public void AnUncommittedEditCounts()
    {
        // The check runs on the working copy, so the diff has to describe the working copy too.
        var repoPath = NewRepo();
        Commit(repoPath, "a.txt", Lines("one", "two"), "base");
        using (var repo = new Repository(repoPath)) repo.CreateBranch("feature");
        File.WriteAllText(Path.Combine(repoPath, "a.txt"), Lines("one", "two", "three"));

        var changed = _git.GetChangedLinesSince(repoPath, "feature");

        Assert.Equal(new[] { 3 }, changed![Path.GetFullPath(Path.Combine(repoPath, "a.txt"))].Order());
    }

    [Fact]
    public void AFileNobodyTouchedIsAbsent()
    {
        var repoPath = NewRepo();
        Commit(repoPath, "a.txt", Lines("one"), "base");
        Commit(repoPath, "b.txt", Lines("untouched"), "second");
        using (var repo = new Repository(repoPath)) repo.CreateBranch("feature");
        Commit(repoPath, "a.txt", Lines("one", "two"), "change a");

        var changed = _git.GetChangedLinesSince(repoPath, "feature");

        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(repoPath, "b.txt")), changed!.Keys);
    }

    [Fact]
    public void WhatTheBaseBranchDidAfterTheBranchPointIsNotOurs()
    {
        // The point of diffing from the merge base. Diffing the ref's tree directly would report
        // main's own later commits as changes of this branch, and a comment on one of those lines
        // is not in the pull request's diff - which GitHub rejects, failing the whole review.
        var repoPath = NewRepo();
        Commit(repoPath, "shared.txt", Lines("one", "two"), "base");

        using (var repo = new Repository(repoPath))
        {
            repo.CreateBranch("feature");
            Commands.Checkout(repo, "feature");
        }
        Commit(repoPath, "mine.txt", Lines("mine"), "my work");

        // main moves on, in a file this branch never touched
        string main;
        using (var repo = new Repository(repoPath))
        {
            main = repo.Branches["master"] is not null ? "master" : "main";
            Commands.Checkout(repo, repo.Branches[main]);
            File.WriteAllText(Path.Combine(repoPath, "shared.txt"), Lines("one", "two", "theirs"));
            Commands.Stage(repo, "shared.txt");
            repo.Commit("their work", Who, Who);
            Commands.Checkout(repo, repo.Branches["feature"]);
        }

        var changed = _git.GetChangedLinesSince(repoPath, main);

        Assert.Contains(Path.GetFullPath(Path.Combine(repoPath, "mine.txt")), changed!.Keys);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(repoPath, "shared.txt")), changed.Keys);
    }

    [Fact]
    public void TheFileLevelDiffIsMeasuredFromTheMergeBaseToo()
    {
        // The ratchet's touched-debt escalation runs off GetChangedFilePathsSince. Diffing the named
        // ref directly reported every file the base branch changed after the branch point, so the
        // boy-scout rule asked this author to clean up somebody else's models.
        var repoPath = NewRepo();
        Commit(repoPath, "shared.txt", Lines("one", "two"), "base");

        using (var repo = new Repository(repoPath))
        {
            repo.CreateBranch("feature");
            Commands.Checkout(repo, "feature");
        }
        Commit(repoPath, "mine.txt", Lines("mine"), "my work");

        string main;
        using (var repo = new Repository(repoPath))
        {
            main = repo.Branches["master"] is not null ? "master" : "main";
            Commands.Checkout(repo, repo.Branches[main]);
            File.WriteAllText(Path.Combine(repoPath, "shared.txt"), Lines("one", "two", "theirs"));
            Commands.Stage(repo, "shared.txt");
            repo.Commit("their work", Who, Who);
            Commands.Checkout(repo, repo.Branches["feature"]);
        }

        var changed = _git.GetChangedFilePathsSince(repoPath, main);

        Assert.NotNull(changed);
        Assert.Contains(Path.GetFullPath(Path.Combine(repoPath, "mine.txt")), changed!);
        Assert.DoesNotContain(Path.GetFullPath(Path.Combine(repoPath, "shared.txt")), changed);
    }

    [Fact]
    public void NothingChangedIsAnEmptyAnswer_NotAFailure()
    {
        var repoPath = NewRepo();
        Commit(repoPath, "a.txt", Lines("one"), "base");

        var changed = _git.GetChangedLinesSince(repoPath, "HEAD");

        Assert.NotNull(changed);
        Assert.Empty(changed!);
    }

    [Fact]
    public void AnUnresolvableRefIsAFailure_NotAnEmptyAnswer()
    {
        var repoPath = NewRepo();
        Commit(repoPath, "a.txt", Lines("one"), "base");

        Assert.Null(_git.GetChangedLinesSince(repoPath, "no-such-ref"));
    }

    [Fact]
    public void SoIsAPathThatIsNotARepository()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitLines_none_" + Guid.NewGuid().ToString("N"));
        _paths.Add(path);
        Directory.CreateDirectory(path);

        Assert.Null(_git.GetChangedLinesSince(path, "HEAD"));
    }
}

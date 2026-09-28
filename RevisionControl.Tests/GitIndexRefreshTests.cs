using LibGit2Sharp;

namespace RevisionControl.Tests;

/// <summary>
/// B298 — a slow working-copy status refreshes git's index, so the next one is fast.
///
/// <para>Format All and a revert rewrite every file with the same content and a new timestamp. Git's
/// index then matches nothing, every status has to read and hash every file to find it unchanged, and
/// LibGit2Sharp's status never writes back what it learned: on MSL that was 7 seconds a query, every
/// query, where a fresh index took a quarter of a second. <c>git update-index --refresh</c> is the
/// write-back.</para>
///
/// <para>Whether the refresh happened is read from the index file itself, because what it changes
/// is the timestamps recorded there - which is also why the threshold is forced here rather than
/// waiting for a status slow enough to trip it.</para>
/// </summary>
public class GitIndexRefreshTests : IDisposable
{
    private const int FileCount = 200;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "GitIndexRefresh_" + Guid.NewGuid().ToString("N"));

    public GitIndexRefreshTests()
    {
        Repository.Init(_root);
        using var repo = new Repository(_root);
        for (var i = 0; i < FileCount; i++)
            File.WriteAllText(Path.Combine(_root, $"Model{i}.mo"), $"model Model{i}\nend Model{i};\n");
        Commands.Stage(repo, "*");
        var who = new Signature("MLQT", "mlqt@localhost", DateTimeOffset.Now);
        repo.Commit("initial", who, who);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What Format All leaves behind: every file's content as it was, its timestamp not.</summary>
    private void TouchEveryFile()
    {
        var later = DateTime.Now.AddHours(1);
        foreach (var file in Directory.EnumerateFiles(_root, "*.mo"))
            File.SetLastWriteTime(file, later);
    }

    private byte[] Index() => File.ReadAllBytes(Path.Combine(_root, ".git", "index"));

    [Fact]
    public void ASlowStatus_WritesTheNewTimestampsBackToTheIndex()
    {
        TouchEveryFile();
        var before = Index();

        var changes = new GitRevisionControlSystem { SlowStatusThreshold = TimeSpan.Zero }.GetWorkingCopyChanges(_root);

        Assert.Empty(changes);
        Assert.NotEqual(before, Index());
    }

    [Fact]
    public void AnOrdinaryStatus_LeavesTheIndexAlone()
    {
        // The control: without it, "the index changed" could be the status writing it on every call.
        TouchEveryFile();
        var before = Index();

        new GitRevisionControlSystem { SlowStatusThreshold = TimeSpan.MaxValue }.GetWorkingCopyChanges(_root);

        Assert.Equal(before, Index());
    }

    [Fact]
    public void AFileThatReallyChanged_IsStillReported_AfterTheRefresh()
    {
        // The refresh records timestamps and stages nothing. Were it to stage, a modified file would
        // vanish from the list of changes - and from the commit dialog with it.
        TouchEveryFile();
        File.WriteAllText(Path.Combine(_root, "Model7.mo"), "model Model7 \"edited\"\nend Model7;\n");
        var git = new GitRevisionControlSystem { SlowStatusThreshold = TimeSpan.Zero };

        var first = git.GetWorkingCopyChanges(_root);
        var second = git.GetWorkingCopyChanges(_root);

        Assert.Equal(["Model7.mo"], first.Select(c => c.Path));
        Assert.Equal(["Model7.mo"], second.Select(c => c.Path));
        Assert.All(second, c => Assert.False(c.IsStaged));
    }
}

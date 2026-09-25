namespace RevisionControl.Tests;

/// <summary>
/// B295 — a git command MLQT runs must end, whatever git does.
///
/// <para>Every one of these used to be able to hang an operation, and the dialog waiting on it, for
/// good: fetch, push, force push, rebase and its continue and abort all go through
/// <see cref="GitRevisionControlSystem.RunGitCommand"/>. The behaviours are driven through a shell
/// alias that does the one thing in question, so they need no remote, no credentials and no conflict
/// — only git and the shell it runs aliases with, which Git for Windows ships.</para>
///
/// <para>A test that fails by hanging never reports, so each is run under a limit of its own and
/// fails by that instead.</para>
/// </summary>
public class GitCommandRunnerTests : IDisposable
{
    private static readonly TimeSpan NeverThisLong = TimeSpan.FromSeconds(60);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GitRunner_" + Guid.NewGuid().ToString("N"));

    public GitCommandRunnerTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAlias(string alias, TimeSpan? limit = null)
    {
        var run = Task.Run(() => GitRevisionControlSystem.RunGitCommand(
            _directory, $"-c \"alias.probe=!{alias}\" probe", limit));

        var finished = await Task.WhenAny(run, Task.Delay(NeverThisLong, TestContext.Current.CancellationToken));
        Assert.True(finished == run, $"git probe ('{alias}') was still running after {NeverThisLong.TotalSeconds}s");
        return await run;
    }

    [Fact]
    public async Task MoreOnStderrThanAPipeHolds_DoesNotDeadlock()
    {
        // 200 KB: far past any pipe buffer, which is the whole point. Read stdout first and git blocks
        // writing this while we wait for stdout to end, which it never does.
        var (exitCode, _, stderr) = await RunAlias("yes stderr-line | head -c 200000 >&2");

        Assert.Equal(0, exitCode);
        Assert.True(stderr.Length >= 200_000, $"only {stderr.Length} characters of stderr came back");
    }

    [Fact]
    public async Task AGitThatRunsTooLong_IsStopped_AndSaysSo()
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var (exitCode, _, stderr) = await RunAlias("sleep 120", limit: TimeSpan.FromSeconds(2));
        elapsed.Stop();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("did not finish", stderr);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30),
            $"stopping it took {elapsed.Elapsed.TotalSeconds:0}s — the kill did not reach what git started");
    }

    [Fact]
    public async Task SomethingThatReadsStdin_SeesItsEnd()
    {
        // With stdin inherited, this waits on whatever MLQT's own stdin is - forever, in a window app.
        var (exitCode, _, _) = await RunAlias("cat", limit: TimeSpan.FromSeconds(20));

        Assert.Equal(0, exitCode);
    }

    // The two below read the start info rather than asking git what it was given. Asked through git,
    // they passed with the settings deleted: the session running them already had GIT_EDITOR=true in
    // its environment, and git inherited it. A user's MLQT has whatever its launcher had.

    [Fact]
    public void GitIsTold_NotToPromptOnATerminal()
    {
        var startInfo = GitRevisionControlSystem.GitStartInfo(_directory, "fetch origin");

        Assert.Equal("0", startInfo.Environment["GIT_TERMINAL_PROMPT"]);
    }

    [Fact]
    public void GitIsGivenAnEditorThatReturnsAtOnce()
    {
        // What `rebase --continue` opens for the commit message once a conflict is resolved. With no
        // console to show it in, anything but an editor that returns at once is a wait forever.
        var startInfo = GitRevisionControlSystem.GitStartInfo(_directory, "rebase --continue");

        Assert.Equal("true", startInfo.Environment["GIT_EDITOR"]);
    }

    [Fact]
    public void EveryStreamIsRedirected()
    {
        var startInfo = GitRevisionControlSystem.GitStartInfo(_directory, "fetch origin");

        Assert.True(startInfo.RedirectStandardInput && startInfo.RedirectStandardOutput && startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
    }
}

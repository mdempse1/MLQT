namespace RevisionControl.Tests;

/// <summary>
/// B297 — an svn command MLQT runs must end, whatever the server does.
///
/// <para>Every svn operation goes through <see cref="SvnCli.Execute"/>. It already read both streams
/// at once and passed <c>--non-interactive</c>, so it could not deadlock on a pipe or wait on a
/// prompt; but a server that accepted the connection and then stopped answering blocked update,
/// commit, switch or merge - and the dialog waiting on it - for good, and stdin was inherited
/// whenever nothing was piped in.</para>
///
/// <para>No svn server is needed to show any of that. <see cref="SvnCli.Execute"/> takes the
/// executable, so these drive it with git running a shell alias that does the one thing in question,
/// as <see cref="GitCommandRunnerTests"/> does for git's own runner. A test that fails by hanging
/// never reports, so each runs under a limit of its own.</para>
/// </summary>
public class SvnCommandRunnerTests
{
    private static readonly TimeSpan NeverThisLong = TimeSpan.FromSeconds(60);

    private static async Task<SvnCli.RawResult> RunAlias(string alias, TimeSpan idleLimit, string? stdin = null)
    {
        var run = Task.Run(() => SvnCli.Execute("git", ["-c", $"alias.probe=!{alias}", "probe"], stdin, idleLimit));

        var finished = await Task.WhenAny(run, Task.Delay(NeverThisLong, TestContext.Current.CancellationToken));
        Assert.True(finished == run, $"the probe ('{alias}') was still running after {NeverThisLong.TotalSeconds}s");
        return await run;
    }

    private static string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes);

    [Fact]
    public async Task ACommandThatFallsSilent_IsStopped_AndSaysSo()
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var result = await RunAlias("sleep 120", idleLimit: TimeSpan.FromSeconds(2));
        elapsed.Stop();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("no output", result.StdErr);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30),
            $"stopping it took {elapsed.Elapsed.TotalSeconds:0}s — the kill did not reach what it started");
    }

    [Fact]
    public async Task ACommandStillWriting_IsNotStopped_HoweverLongItRuns()
    {
        // The limit is on silence, not on running time: a checkout of a large repository runs for as
        // long as it runs, and reports each file as it goes. This one runs three times the limit.
        var result = await RunAlias(
            "for i in 1 2 3 4 5 6; do echo tick; sleep 1; done", idleLimit: TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(6, Text(result.StdOut).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task SomethingThatReadsStdin_SeesItsEnd()
    {
        // With stdin inherited, this waits on whatever MLQT's own stdin is - forever, in a window app.
        var result = await RunAlias("cat", idleLimit: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StdOut);
    }

    [Fact]
    public async Task TextPipedToStdin_StillArrives()
    {
        var result = await RunAlias("cat", idleLimit: TimeSpan.FromSeconds(20), stdin: "a commit message");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("a commit message", Text(result.StdOut));
    }

    [Fact]
    public async Task MoreOutputThanAPipeHolds_OnBothStreams_DoesNotDeadlock()
    {
        // What svn cat of a large file, or svn log over thousands of revisions, looks like.
        var result = await RunAlias(
            "yes out | head -c 300000; yes err | head -c 200000 >&2", idleLimit: TimeSpan.FromSeconds(20));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(300_000, result.StdOut.Length);
        Assert.Equal(200_000, result.StdErr.Length);
    }

    /// <summary>
    /// No svn command MLQT runs is made quiet (B383).
    /// </summary>
    /// <remarks>
    /// The limit above is on silence, and it can only tell a command still working from a stalled
    /// one because svn reports each file as it goes. <c>svn update --quiet</c> of a large working
    /// copy prints nothing until it is done, so an update that took longer than the limit was stopped
    /// as a stall - and, since B330, cleaned up after - while it was working. Read from the source
    /// rather than asserted on one call, because the next command made quiet "to keep the output
    /// down" would have the same defect and no test of its own.
    /// </remarks>
    [Fact]
    public void NoSvnCommandIsMadeQuiet()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var sources = Directory.GetFiles(Path.Combine(dir.FullName, "RevisionControl"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.Contains(sources, f => Path.GetFileName(f) == "SvnRevisionControlSystem.cs");

        var quiet = sources
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => l.Text.Contains("\"--quiet\"") || l.Text.Contains("\"-q\""))
            .Select(l => $"{l.File}:{l.Line}: {l.Text.Trim()}")
            .ToList();

        Assert.True(quiet.Count == 0, "an svn command is made quiet, so the idle limit cannot see it working:\n" + string.Join("\n", quiet));
    }
}

namespace RevisionControl.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public class ThreadPoolStarvationCollection
{
    public const string Name = "Thread pool starvation";
}

/// <summary>
/// The svn idle limit is judged by when the command last wrote, however busy the process is.
/// </summary>
/// <remarks>
/// <para><see cref="SvnCli.Execute"/> once noted each sign of life in a <c>ReadAsync</c>
/// continuation on the thread pool. With the pool busy, the output sat unread in the pipe and the
/// clock was never reset, so a command writing every quarter of a second was stopped as silent. On
/// a 4-core CI runner, the command's last line, drained after the kill, had been written 0.28s
/// earlier.</para>
///
/// <para>This test makes the pool busy on purpose by queueing more blocking work than it has
/// threads. It then runs a command that writes steadily for longer than the limit. It runs alone
/// (<see cref="ThreadPoolStarvationCollection"/>) because it holds every pool thread for as long as
/// the command runs.</para>
/// </remarks>
[Collection(ThreadPoolStarvationCollection.Name)]
public class SvnCommandRunnerStarvationTests
{
    // Blocking is the point: an await would need a pool thread while the pool is full on purpose.
#pragma warning disable xUnit1031
    [Fact]
    public void ACommandStillWriting_IsNotStopped_WhileThePoolIsBusy()
    {
        var limit = TimeSpan.FromSeconds(1.5);

        // The pool adds threads when it is starved, and a count of blockers alone starved it for
        // too short a time to matter. Capped at its minimum and filled, it cannot catch up. The test
        // blocks rather than awaits for the same reason: nothing here may need a pool thread until
        // the pool is released.
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);
        Assert.True(ThreadPool.SetMaxThreads(minWorkers, maxIo), "the thread pool refused its cap");

        using var release = new ManualResetEventSlim();
        var blockers = new List<Task>();
        Task<bool>? control = null;
        SvnCli.RawResult? result = null;
        var ranWhileStarved = false;
        try
        {
            for (var i = 0; i < minWorkers * 2; i++)
                blockers.Add(Task.Run(() => release.Wait(TimeSpan.FromSeconds(60))));

            // The positive control. It is queued behind the blockers and must not run until they are
            // released. If it has run, the pool was not starved and this run tested nothing.
            control = Task.Run(() => release.IsSet);

            // The command runs on a thread of its own, because the pool is what this test takes away.
            var runner = new Thread(() => result = SvnCli.Execute("git",
                ["-c", "alias.probe=!for i in $(seq 20); do echo tick; sleep 0.2; done", "probe"],
                stdinText: null, limit));
            runner.Start();
            Assert.True(runner.Join(TimeSpan.FromSeconds(60)), "the probe was still running after 60s");

            ranWhileStarved = control.IsCompleted;
        }
        finally
        {
            release.Set();
            Task.WaitAll([.. blockers], TimeSpan.FromSeconds(60));
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
        }

        Assert.False(ranWhileStarved, "queued work ran while the thread pool should have been full, so nothing was tested");
        Assert.True(control.Wait(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken) && control.Result, "the control ran before the pool was released");

        Assert.NotNull(result);
        var linesRead = System.Text.Encoding.UTF8.GetString(result.StdOut).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.False(result.Stopped,
            $"stopped as silent while it was still writing, with {linesRead} of 20 lines read. The idle " +
            "clock is being kept waiting behind the thread pool");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(20, linesRead);
    }
#pragma warning restore xUnit1031
}

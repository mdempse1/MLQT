namespace MLQT.Services.Tests;

/// <summary>
/// B396 — two requests to Dymola are never given the same id.
/// </summary>
/// <remarks>
/// A probe (<c>GetSessionStateAsync</c>) runs outside the command lock, so it can take an id while a
/// command is taking one. Both did <c>_rpcId++</c> and then read <c>_rpcId</c> again, so the two
/// could send the same id - the id the interface relies on to tell a command's own reply from the
/// late answer to one it gave up on. Here rather than in DymolaInterface.Tests, which no CI job runs.
/// </remarks>
public sealed class DymolaRequestIdTests
{
    /// <summary>A port nothing is expected to be listening on, so construction takes no id.</summary>
    private const int DeadPort = 9999;

    [Fact]
    public void IdsTakenConcurrently_AreAllDifferent_AndNoneIsSkipped()
    {
        using var dymola = new DymolaInterface.DymolaInterface("", DeadPort, "127.0.0.1", TimeSpan.Zero);
        const int threads = 8;
        const int perThread = 50_000;
        var taken = new int[threads][];

        using var start = new Barrier(threads);
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            var mine = new int[perThread];
            start.SignalAndWait();
            for (var i = 0; i < perThread; i++)
                mine[i] = dymola.NextRequestId();
            taken[t] = mine;
        });

        var all = taken.SelectMany(ids => ids).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(threads * perThread, all.Max());
    }

    [Fact]
    public void IdsCountUpFromOne()
    {
        using var dymola = new DymolaInterface.DymolaInterface("", DeadPort, "127.0.0.1", TimeSpan.Zero);

        Assert.Equal(1, dymola.NextRequestId());
        Assert.Equal(2, dymola.NextRequestId());
    }
}

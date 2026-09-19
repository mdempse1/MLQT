using System.Diagnostics;
using DymolaInterface.Tests.Fakes;

namespace DymolaInterface.Tests;

/// <summary>
/// Tests for <see cref="DymolaInterface.CommandTimeout"/>: the per-command limit that lets a
/// caller give one long simulation hours while ordinary commands keep the five-minute default.
/// </summary>
public class CommandTimeoutTests
{
    [Fact]
    public void CommandTimeout_ByDefault_IsFiveMinutes()
    {
        using var h = new DymolaTestHarness();

        Assert.Equal(TimeSpan.FromMinutes(5), h.Dymola.CommandTimeout);
        Assert.Equal(DymolaInterface.DefaultCommandTimeout, h.Dymola.CommandTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CommandTimeout_NotPositive_IsRejected(int seconds)
    {
        using var h = new DymolaTestHarness();

        Assert.Throws<ArgumentOutOfRangeException>(() => h.Dymola.CommandTimeout = TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void CommandTimeout_Infinite_IsAccepted()
    {
        using var h = new DymolaTestHarness();

        h.Dymola.CommandTimeout = Timeout.InfiniteTimeSpan;

        Assert.Equal(Timeout.InfiniteTimeSpan, h.Dymola.CommandTimeout);
    }

    [Fact]
    public async Task Command_SlowerThanTheCommandTimeout_GivesUpAtTheTimeout()
    {
        using var h = new DymolaTestHarness();
        h.Handler.ResponseDelay = TimeSpan.FromSeconds(20);
        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(200);

        var elapsed = Stopwatch.StartNew();
        var ok = await h.Dymola.ExecuteCommandAsync("slowCommand()");
        elapsed.Stop();

        Assert.False(ok);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task Command_FasterThanTheCommandTimeout_Completes()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromMilliseconds(100);
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(10);

        Assert.True(await h.Dymola.ExecuteCommandAsync("quickCommand()"));
    }

    [Fact]
    public async Task CommandTimeout_ChangedBetweenCommands_AppliesToTheNextCommand()
    {
        using var h = new DymolaTestHarness();
        h.SetResultBool(true);
        h.Handler.ResponseDelay = TimeSpan.FromMilliseconds(500);

        h.Dymola.CommandTimeout = TimeSpan.FromMilliseconds(100);
        var first = await h.Dymola.ExecuteCommandAsync("first()");
        h.Dymola.CommandTimeout = TimeSpan.FromSeconds(10);
        var second = await h.Dymola.ExecuteCommandAsync("second()");

        Assert.False(first);
        Assert.True(second);
    }
}

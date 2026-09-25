using MLQT.Shared.Helpers;
using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// B326 — the analysis pipeline a VCS operation starts counts as VCS work until it has finished, so
/// no other operation can start under it.
/// </summary>
/// <remarks>
/// The Library Browser's busy flag cleared as soon as its operation returned, and the operation
/// returned as soon as it had fired the pipeline, which runs detached. A Switch Branch could then
/// start during the previous Update's "Applying code formatting…", reload every library under the
/// running analysis, and have the formatter write old-branch text over the checkout.
/// </remarks>
public class VcsPipelineQueueTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly AppState _state = new();

    [Fact]
    public async Task APipelineIsVcsWork_FromTheMomentItIsQueued_UntilItHasFinished()
    {
        var queue = new VcsPipelineQueue(_state);
        var release = new TaskCompletionSource();

        var run = queue.Enqueue(() => release.Task);

        // Before the pipeline has even started: the caller's operation may end on the next line.
        Assert.True(_state.IsVcsWorkInProgress);

        release.SetResult();
        await run.WaitAsync(Patience);
        Assert.False(_state.IsVcsWorkInProgress);
    }

    [Fact]
    public async Task PipelinesRunOneAtATime_AndTheWorkLastsUntilTheLastHasRun()
    {
        // One at a time, not first come first served: which of two queued together goes first was
        // never promised, and asserting it made this fail on a loaded machine.
        var queue = new VcsPipelineQueue(_state);
        var running = 0;
        var mostAtOnce = 0;
        var workSeenWhileRunning = true;

        async Task Pipeline()
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref mostAtOnce, now);
            await Task.Delay(100);
            workSeenWhileRunning &= _state.IsVcsWorkInProgress;
            Interlocked.Decrement(ref running);
        }

        var runs = Enumerable.Range(0, 3).Select(_ => queue.Enqueue(Pipeline)).ToList();
        await Task.WhenAll(runs).WaitAsync(Patience);

        Assert.Equal(1, mostAtOnce);
        Assert.True(workSeenWhileRunning, "the work ended while a queued pipeline was still to run");
        Assert.False(_state.IsVcsWorkInProgress);

        static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while ((seen = Volatile.Read(ref target)) < value
                   && Interlocked.CompareExchange(ref target, value, seen) != seen)
            {
            }
        }
    }

    [Fact]
    public async Task APipelineThatThrows_StillEndsItsWork_AndDoesNotHoldUpTheNext()
    {
        var queue = new VcsPipelineQueue(_state);

        var failed = queue.Enqueue(() => throw new InvalidOperationException("the analysis failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.WaitAsync(Patience));

        var ran = false;
        await queue.Enqueue(() => { ran = true; return Task.CompletedTask; }).WaitAsync(Patience);

        Assert.True(ran);
        Assert.False(_state.IsVcsWorkInProgress);
    }

    [Fact]
    public void VcsWork_EndedTwice_IsCountedOffOnce()
    {
        var changes = 0;
        _state.OnVcsWorkChanged += () => changes++;

        var outer = _state.BeginVcsWork();
        var inner = _state.BeginVcsWork();
        inner.Dispose();
        inner.Dispose();

        Assert.True(_state.IsVcsWorkInProgress, "a second end of the inner work ended the outer one too");

        outer.Dispose();
        Assert.False(_state.IsVcsWorkInProgress);
        Assert.Equal(4, changes);
    }
}

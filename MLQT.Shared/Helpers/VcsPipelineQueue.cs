namespace MLQT.Shared.Helpers;

/// <summary>
/// The analysis pipelines that VCS operations start: run one at a time, and counted as VCS work
/// from the moment they are queued until they finish (B301, B326).
/// </summary>
/// <remarks>
/// <para><b>One at a time</b> (B301). An operation on a working copy that holds several repositories
/// starts a pipeline for each, and two running together format files and re-analyse dependencies
/// over the same graph at once. Queued rather than refused: each still has to run.</para>
///
/// <para><b>Counted from the queueing, not from the start</b> (B326). The work is taken on the
/// caller's thread, inside the event the VCS operation raised, so the operation cannot end and let
/// the next one start in the gap before the pipeline gets going - or while it waits behind another.
/// A revert's analysis and a Refresh go through here as well: they rewrite and re-analyse the same
/// graph.</para>
/// </remarks>
public sealed class VcsPipelineQueue
{
    private readonly AppState _state;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VcsPipelineQueue(AppState state) => _state = state;

    /// <summary>
    /// Queues a pipeline behind any already queued. Returns the task that completes when it has
    /// run; callers that fire and forget may discard it.
    /// </summary>
    public Task Enqueue(Func<Task> pipeline)
    {
        var work = _state.BeginVcsWork();
        return Task.Run(async () =>
        {
            try
            {
                await _gate.WaitAsync();
                try
                {
                    await pipeline();
                }
                finally
                {
                    _gate.Release();
                }
            }
            finally
            {
                work.Dispose();
            }
        });
    }
}

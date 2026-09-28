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
    /// <summary>
    /// Queues <paramref name="work"/> only when no VCS work is in progress, and returns
    /// <see langword="null"/> without running it otherwise - for work that must not start under a VCS
    /// operation rather than merely wait behind its pipeline.
    /// </summary>
    /// <remarks>
    /// Format All Files is the case (B385). It rewrites every file in the repository, so a VCS
    /// operation must not start under it - which queueing gives it - and it must not start under
    /// one either: an Update holds no place in this queue while it runs, only its pipeline does
    /// once it has finished, so queued work would take the gate and format files the Update is still
    /// writing.
    /// </remarks>
    public Task? TryEnqueue(Func<Task> work) =>
        _state.IsVcsWorkInProgress ? null : Enqueue(work);

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

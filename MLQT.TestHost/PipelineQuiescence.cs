using MLQT.Services.Interfaces;

namespace MLQT.TestHost;

/// <summary>
/// Answers "has MLQT finished thinking?" for a journey that needs to click the next thing.
/// </summary>
/// <remarks>
/// <para>The alternative is sleeping, and a suite built on sleeps is slow when it passes and flaky
/// when it does not — the sleep is always either longer than the work or shorter than it, and which
/// one depends on the machine. Playwright's auto-waiting handles the DOM settling; it cannot know
/// that a background style check over 7,000 classes is still running.</para>
///
/// <para>Test-host only. Nothing here is reachable from the desktop app, and the shipped hosts do
/// not reference it.</para>
/// </remarks>
public sealed class PipelineQuiescence(
    IStyleCheckingService styleChecking,
    IFileMonitoringService monitoring,
    ILibraryDataService libraries)
{
    /// <summary>
    /// Waits until dependency analysis has finished, the style-check queue is empty and the file
    /// monitor has nothing pending.
    /// </summary>
    /// <returns>False if the wait was abandoned rather than reaching quiet.</returns>
    public async Task<bool> WaitForIdleAsync(CancellationToken token)
    {
        try
        {
            // Whatever dependency analysis is in flight, plus anything queued behind it. Idempotent,
            // and shared by concurrent callers, so asking is cheap even when nothing is running.
            await libraries.EnsureDependenciesAnalyzedAsync();

            // Style checking queues its work and returns; this is the part a caller has to wait on.
            await styleChecking.WaitForCompletionAsync();

            // A pending change means a re-analysis is about to be asked for, so the run that just
            // finished is not the last word.
            while (monitoring.PendingChanges.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(50, token);
                await styleChecking.WaitForCompletionAsync();
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

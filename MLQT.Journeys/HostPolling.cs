namespace MLQT.Journeys;

/// <summary>
/// Waiting on the host's own state, for what the page does not announce: findings arriving in the
/// Code Review service, a startup step finishing.
/// </summary>
internal static class HostPolling
{
    /// <summary>Polls <paramref name="condition"/> until it holds, failing after the timeout.</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 60_000, string? what = null)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(what is null
                    ? "the host did not reach the expected state in time"
                    : $"the host did not reach the expected state in time: {what}");
            await Task.Delay(100);
        }
    }
}

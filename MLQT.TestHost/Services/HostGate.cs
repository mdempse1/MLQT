namespace MLQT.TestHost.Services;

/// <summary>
/// Holds one call of a service at the door until a journey lets it through.
/// </summary>
/// <remarks>
/// <para>For the hand checks that are about what the window shows <i>while</i> something runs - a
/// reload during startup (B407), buttons during Format All (B385). On the fixture library every step
/// finishes in milliseconds, so without a way to hold one there is no "while" to look at, and a
/// journey that tried to catch it with timing would pass or fail by the speed of the machine.</para>
///
/// <para><b>One call, once.</b> The first call to arrive after the gate is armed waits on it; every
/// other call, before or after, goes straight through. Disposing releases it, so a journey that fails
/// part-way does not leave the shared host with a step that never ends.</para>
/// </remarks>
public sealed class HostGate : IDisposable
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the held call has reached the gate.</summary>
    public Task Arrived => _arrived.Task;

    /// <summary>Waits for the held call to arrive, failing rather than hanging when it never does.</summary>
    public async Task WaitForArrivalAsync(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(60);
        if (await Task.WhenAny(Arrived, Task.Delay(limit)) != Arrived)
            throw new TimeoutException($"nothing reached the gate within {limit.TotalSeconds:0}s");
    }

    /// <summary>Lets the held call go on.</summary>
    public void Release() => _released.TrySetResult();

    /// <summary>Called by the gated service: records the arrival and waits to be let through.</summary>
    internal Task PassAsync()
    {
        _arrived.TrySetResult();
        return _released.Task;
    }

    public void Dispose() => Release();
}

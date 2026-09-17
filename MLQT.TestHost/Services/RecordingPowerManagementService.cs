using MLQT.Services.Interfaces;

namespace MLQT.TestHost.Services;

/// <summary>
/// Keeps count instead of touching the operating system.
/// </summary>
/// <remarks>
/// Counting rather than doing nothing, so "a long operation prevented sleep" stays assertable — and
/// so does the half that actually goes wrong: <see cref="PreventSleep"/> without a matching
/// <see cref="AllowSleep"/> leaves a user's machine awake after MLQT has finished with it, and on
/// Windows that persists until the process exits.
/// </remarks>
public sealed class RecordingPowerManagementService : IPowerManagementService
{
    private int _prevented;
    private int _allowed;

    public int PreventedCount => Volatile.Read(ref _prevented);
    public int AllowedCount => Volatile.Read(ref _allowed);

    /// <summary>True when every <see cref="PreventSleep"/> has had its <see cref="AllowSleep"/>.</summary>
    public bool IsBalanced => PreventedCount == AllowedCount;

    public void PreventSleep() => Interlocked.Increment(ref _prevented);

    public void AllowSleep() => Interlocked.Increment(ref _allowed);
}

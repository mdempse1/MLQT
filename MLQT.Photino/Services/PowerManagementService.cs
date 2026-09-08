using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MLQT.Services;
using MLQT.Services.Interfaces;

namespace MLQT.Photino.Services;

/// <summary>
/// Stops the machine sleeping during a long analysis or check.
/// </summary>
/// <remarks>
/// <para>The Windows half is the MAUI implementation unchanged, because there was never anything
/// MAUI about it: <c>SetThreadExecutionState</c> is plain Win32 through <c>DllImport</c>. The plan
/// called this a file copy and it was.</para>
///
/// <para><b>Linux holds an inhibit lock through <c>systemd-inhibit</c></b> (7b-3), rather than
/// speaking D-Bus directly. A D-Bus call would avoid a child process, but it needs a session-bus
/// connection and a dependency to make one, and it fails in exactly the environments this has to
/// degrade gracefully in anyway — a container, a remote session, a desktop that is not systemd's.
/// <c>systemd-inhibit</c> is present wherever the lock would have been honoured and absent where it
/// would not, so its absence is the same answer by a cheaper route.</para>
///
/// <para><b>Where there is nothing to hold the lock with, this does nothing and says so once.</b>
/// Probe 10 asserts only that the two calls return, so it cannot tell a real inhibit from a no-op —
/// which is why the log line matters more than the probe here. A machine that sleeps in the middle of
/// a forty-minute check is a bug report that arrives with no evidence at all.</para>
/// </remarks>
internal sealed class PowerManagementService : IPowerManagementService
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001,
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    private readonly Lock _gate = new();
    private Process? _inhibitor;
    private bool _warned;

    public void PreventSleep()
    {
        if (OperatingSystem.IsWindows())
        {
            SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
            return;
        }

        if (!OperatingSystem.IsLinux())
            return;

        lock (_gate)
        {
            if (_inhibitor is { HasExited: false })
                return;

            try
            {
                // The lock lives as long as the child does, so it holds `sleep infinity` and is killed
                // to release. --what=idle:sleep covers both the idle timer and an explicit suspend.
                _inhibitor = Process.Start(new ProcessStartInfo("systemd-inhibit")
                {
                    ArgumentList =
                    {
                        "--what=idle:sleep",
                        "--who=MLQT",
                        "--why=Analysing a Modelica library",
                        "--mode=block",
                        "sleep", "infinity",
                    },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
            }
            catch (Exception ex)
            {
                // No systemd-inhibit, or no session to inhibit. Nothing to do, and worth saying once
                // rather than on every long-running operation.
                if (!_warned)
                {
                    _warned = true;
                    LoggingService.Info(nameof(PowerManagementService),
                        $"Cannot prevent sleep on this system ({ex.GetType().Name}: {ex.Message}); "
                        + "long operations may be interrupted by the machine suspending.");
                }
            }
        }
    }

    public void AllowSleep()
    {
        if (OperatingSystem.IsWindows())
        {
            SetThreadExecutionState(ExecutionState.Continuous);
            return;
        }

        lock (_gate)
        {
            if (_inhibitor is null)
                return;

            try
            {
                if (!_inhibitor.HasExited)
                    _inhibitor.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                LoggingService.Error(nameof(PowerManagementService), "Could not release the sleep inhibitor", ex);
            }
            finally
            {
                _inhibitor.Dispose();
                _inhibitor = null;
            }
        }
    }
}

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
/// <para><b>Linux is not implemented yet</b> and does nothing rather than pretending. The intended
/// mechanism is an inhibit lock over D-Bus (<c>org.freedesktop.ScreenSaver</c>) or shelling out to
/// <c>systemd-inhibit</c>; 7b-3 owns it. Probe 10 asserts only that both calls return, which they do
/// — so the probe passes on Linux today while the machine can still sleep mid-run. That is a
/// limitation of what a probe can see, not a passing grade, and it is written down here so it is not
/// read as one.</para>
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

    public void PreventSleep()
    {
        if (OperatingSystem.IsWindows())
            SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
    }

    public void AllowSleep()
    {
        if (OperatingSystem.IsWindows())
            SetThreadExecutionState(ExecutionState.Continuous);
    }
}

using System.Runtime.InteropServices;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Windows implementation of IPowerManagementService using SetThreadExecutionState.
/// Prevents the system from sleeping during long-running operations like
/// dependency analysis and style checking.
/// </summary>
public class PowerManagementService : IPowerManagementService
{
    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_CONTINUOUS = 0x80000000,
        ES_SYSTEM_REQUIRED = 0x00000001,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    public void PreventSleep()
    {
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
    }

    public void AllowSleep()
    {
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
    }
}

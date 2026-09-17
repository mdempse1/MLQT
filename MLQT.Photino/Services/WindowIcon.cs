using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MLQT.Services;

namespace MLQT.Photino.Services;

/// <summary>
/// Puts the application icon on the window itself, after Windows has made the taskbar button.
/// </summary>
/// <remarks>
/// <para>Photino attaches the icon from the <c>.ico</c> while the window is being created, and it
/// attaches the <b>100% size</b>: 32x32 for <c>ICON_BIG</c> and 16x16 for <c>ICON_SMALL</c>. On a
/// scaled display Windows then stretches them — at 125% the shell wants 40 and 20 — so this re-attaches
/// them at the size the display actually asks for, which the multi-size <c>.ico</c> can come <i>down</i>
/// to from its 48 rather than up from its 32.</para>
///
/// <para><b>This is not what fixed the taskbar button</b>, and the file says so because it was written
/// while trying to. That turned out to be a stale Start Menu shortcut pointing at a build from before
/// the icon existed — see B132 and <c>Branding/README.md</c>. This stays because a crisp icon at high
/// DPI is worth having on its own, not because it fixes anything.</para>
///
/// <para>Windows only — GTK takes a PNG through <c>SetIconFile</c> and needs none of this — and every
/// failure is logged and stepped over. An application that will not start because it could not decorate
/// its own title bar would be a much worse defect than a soft icon.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowIcon
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    /// <summary>Attaches <paramref name="iconFile"/> to this process's main window.</summary>
    public static void Apply(string iconFile)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(iconFile) || !File.Exists(iconFile))
            return;

        try
        {
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var window = process.MainWindowHandle;

            if (window == IntPtr.Zero)
            {
                LoggingService.Warn(nameof(WindowIcon), "No main window to put the icon on");
                return;
            }

            var dpi = (int)GetDpiForWindow(window);

            Set(window, iconFile, ICON_BIG, WindowGeometry.IconSize(dpi, 32));
            Set(window, iconFile, ICON_SMALL, WindowGeometry.IconSize(dpi, 16));

            LoggingService.Info(nameof(WindowIcon), $"Window icon set from {iconFile} at {dpi} dpi");
        }
        catch (Exception ex)
        {
            LoggingService.Warn(nameof(WindowIcon), $"Could not set the window icon: {ex.Message}");
        }
    }

    private static void Set(IntPtr window, string iconFile, int which, int size)
    {
        // The handle is deliberately not freed. It belongs to the window for as long as the window
        // exists, and the process ends with it; destroying it here is how a window ends up with no
        // icon and a title bar that draws a blank square.
        var icon = LoadImageW(IntPtr.Zero, iconFile, IMAGE_ICON, size, size, LR_LOADFROMFILE);

        if (icon == IntPtr.Zero)
        {
            LoggingService.Warn(nameof(WindowIcon), $"Could not load a {size}x{size} icon from {iconFile}");
            return;
        }

        SendMessageW(window, WM_SETICON, which, icon);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}

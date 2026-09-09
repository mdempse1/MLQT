using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MLQT.Services;

namespace MLQT.Photino.Services;

/// <summary>
/// Puts the application icon on the window itself, after Windows has made the taskbar button.
/// </summary>
/// <remarks>
/// <para><b>Three places show an application's icon and each reads from somewhere different</b>, which
/// is how two of them can be right while the third is not. Explorer and Alt-Tab read the resource
/// compiled into the executable; the title bar reads the window's <c>ICON_SMALL</c>; the taskbar reads
/// <c>ICON_BIG</c>, under the identity the shell has for the process.</para>
///
/// <para>Photino's <c>SetIconFile</c> attaches both from the <c>.ico</c> — measured at 16x16 and
/// 32x32, both correct — and the taskbar still showed the generic executable icon. Two things about
/// that are worth fixing whatever the cause, and this does both:</para>
///
/// <list type="number">
/// <item><b>Timing.</b> Photino attaches the icon while the window is being created. A taskbar button
/// takes its icon when it appears, and a button that appeared first keeps what it had. Setting the
/// icon again once the window exists is the ordinary way to make the shell pick it up.</item>
/// <item><b>Size.</b> The 32x32 is the icon at 100%. At 125% the taskbar wants 40 and stretches it;
/// the <c>.ico</c> has a 48 to come down from instead, which is the direction that looks right.
/// <see cref="WindowGeometry.IconSize"/> works out which to ask for.</item>
/// </list>
///
/// <para>Windows only — GTK takes a PNG through <c>SetIconFile</c> and needs none of this — and every
/// failure is logged and stepped over. An application that will not start because it could not decorate
/// its taskbar button would be a much worse defect than the one this fixes.</para>
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

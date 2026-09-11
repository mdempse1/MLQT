using MLQT.Services;
using MLQT.Services.Interfaces;
using Photino.NET;

namespace MLQT.Photino.Services;

/// <summary>
/// Where the window was last time.
/// </summary>
/// <remarks>
/// <para>MAUI restored this for us; Photino does not, so it is the host's job. Stored through
/// <see cref="ISettingsService"/> like everything else, which means it lands in the same JSON file.</para>
///
/// <para>A saved placement is only used if it still lands somewhere usable. A window restored to a
/// monitor that is no longer attached is invisible and unrecoverable without editing the settings
/// file, which is the failure worth guarding: the numbers are checked for sanity rather than trusted,
/// and anything odd falls back to the default.</para>
/// </remarks>
internal sealed record WindowPlacement(int Left, int Top, int Width, int Height)
{
    private const string Key = "WindowPlacement";

    /// <summary>
    /// The first-run window, in device-independent units — the size MAUI opened at.
    /// </summary>
    /// <remarks>
    /// <c>MLQT/App.xaml.cs</c> asks for <c>min(1200, screen)</c> by <c>min(900, screen)</c> and centres
    /// it, and MAUI works in device-independent units throughout. Same numbers here, so the two hosts
    /// open the same size on the same machine.
    /// </remarks>
    private const int PreferredWidth = 1200;
    private const int PreferredHeight = 900;

    /// <summary>The last-resort window, for a machine whose monitors cannot be read.</summary>
    public static WindowPlacement Fallback { get; } = new(Left: 80, Top: 60, Width: 1400, Height: 950);

    /// <summary>
    /// Where to open: where the user left it, or a first-run window the size MAUI used.
    /// </summary>
    /// <remarks>
    /// <para><b>Photino sizes windows in physical pixels; MAUI sized them in device-independent
    /// units.</b> That is the whole of why the two looked different, and it is not visible in the
    /// numbers: a flat <c>SetSize(1400, 950)</c> is 1400 physical pixels, which on the 125% display it
    /// was reported from is 1120 × 760 units against MAUI's 1200 × 900 — a window a fifth shorter,
    /// from code that appears to ask for a larger one.</para>
    ///
    /// <para>Measured rather than assumed: with <c>SetSize(1400, 950)</c> the self-test's
    /// <c>interop.dimensions</c> probe reported a 1106-pixel-wide viewport, which is 1400 divided by
    /// 1.25 less the window chrome. Had Photino been sizing in units it would have reported ~1390.</para>
    ///
    /// <para>So the preferred size is scaled by the monitor, clamped to its work area rather than its
    /// full bounds — a window as tall as the screen sits under the taskbar — and centred, as MAUI
    /// centred.</para>
    /// </remarks>
    public static void Apply(ISettingsService settings, PhotinoWindow window)
    {
        var saved = settings.GetAsync<WindowPlacement?>(Key, null).GetAwaiter().GetResult();

        if (saved is not null && saved.IsUsable())
        {
            Set(window, saved);

            // ...and checked again once there is a window to ask about the monitors, because the
            // layout changes while the application is closed. IsUsable only rejects the shapes that
            // are wrong on any machine; whether *this* machine still has the screen the window was
            // left on cannot be known until now (B149).
            window.RegisterWindowCreatedHandler((sender, _) => KeepOnScreen((PhotinoWindow)sender!, saved));
            return;
        }

        // First run, and the monitor cannot be read yet: PhotinoWindow.MainMonitor and ScreenDpi
        // throw "the Photino window hasn't been initialized yet" until the native window exists,
        // which is after Run(). So the size that depends on the display is applied from the
        // WindowCreated handler, and a fallback covers the moment before it.
        //
        // Found by measurement, and it would not have been found otherwise: reading the monitor here
        // throws into the catch below, which logs and returns the fallback — so the host would have
        // opened at the wrong size on every first run with nothing but a line in a log to say so.
        Set(window, Fallback);
        window.RegisterWindowCreatedHandler((sender, _) => Set((PhotinoWindow)sender!, FirstRun((PhotinoWindow)sender!)));
    }

    /// <summary>
    /// Moves a remembered window back onto a screen when the one it was left on has gone.
    /// </summary>
    /// <remarks>
    /// <para>Backlog B149, and the case is the ordinary one: dock a laptop, open MLQT on the second
    /// screen, close it, undock. The remembered position is then off the side of the only display
    /// there is — and a window nobody can see is also a window nobody can drag back.</para>
    ///
    /// <para>The user's size is kept and only shrunk to fit. Losing a deliberately large window is a
    /// poor answer to a problem whose cause was unplugging a cable.</para>
    ///
    /// <para>Says so in the log. A window that quietly appears somewhere other than where it was left
    /// is the kind of thing people doubt their own memory about.</para>
    /// </remarks>
    private static void KeepOnScreen(PhotinoWindow window, WindowPlacement saved)
    {
        try
        {
            var workAreas = window.Monitors
                .Select(m => new WindowBounds(
                    m.WorkArea.Left, m.WorkArea.Top, m.WorkArea.Width, m.WorkArea.Height))
                .ToList();

            if (workAreas.Count == 0)
                return;

            var remembered = new WindowBounds(saved.Left, saved.Top, saved.Width, saved.Height);
            if (WindowGeometry.IsOnScreen(remembered, workAreas))
                return;

            var moved = WindowGeometry.MovedOnto(remembered, workAreas[0]);

            MLQT.Services.LoggingService.Info(nameof(WindowPlacement),
                $"The remembered window at {saved.Left},{saved.Top} is not on any current display " +
                $"({workAreas.Count} found); moving it to {moved.Left},{moved.Top}");

            Set(window, new WindowPlacement(moved.Left, moved.Top, moved.Width, moved.Height));
        }
        catch (Exception ex)
        {
            // Reading the monitor layout needs the native layer and a display. Leaving the window
            // where it was remembered is a worse answer than moving it, and a better one than not
            // opening at all.
            MLQT.Services.LoggingService.Error(nameof(WindowPlacement),
                "Could not check the remembered window against the monitor layout", ex);
        }
    }

    private static void Set(PhotinoWindow window, WindowPlacement placement) =>
        window.SetSize(placement.Width, placement.Height)
              .SetLeft(placement.Left)
              .SetTop(placement.Top);

    /// <summary>The centred, monitor-scaled first-run window.</summary>
    /// <remarks>
    /// Only the plumbing lives here: reading the monitor is Photino's business and the arithmetic is
    /// <see cref="WindowGeometry"/>'s, where it can be tested without a display.
    /// </remarks>
    internal static WindowPlacement FirstRun(PhotinoWindow window)
    {
        try
        {
            var monitor = window.MainMonitor;
            var work = new WindowBounds(
                monitor.WorkArea.Left, monitor.WorkArea.Top,
                monitor.WorkArea.Width, monitor.WorkArea.Height);

            var bounds = WindowGeometry.Centred(work, monitor.Scale);

            MLQT.Services.LoggingService.Info(nameof(WindowPlacement),
                $"First run: opening {bounds.Width}x{bounds.Height} at {bounds.Left},{bounds.Top} " +
                $"(work area {work.Width}x{work.Height}, scale {monitor.Scale})");

            return new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        catch (Exception ex)
        {
            // Reading the monitors goes through the native layer and needs a display. It is not worth
            // refusing to open a window over.
            MLQT.Services.LoggingService.Error(nameof(WindowPlacement),
                "Could not read the monitor layout; opening at the fallback size", ex);
            return Fallback;
        }
    }

    public static void Save(ISettingsService settings, PhotinoWindow window)
    {
        try
        {
            var placement = new WindowPlacement(window.Left, window.Top, window.Width, window.Height);

            if (placement.IsUsable())
                settings.SetAsync(Key, placement).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Failing to remember the window is not a reason to fail closing it.
            MLQT.Services.LoggingService.Error(nameof(WindowPlacement), "Could not save the window placement", ex);
        }
    }

    /// <summary>
    /// Whether this placement would put a usable window on screen.
    /// </summary>
    /// <remarks>
    /// Deliberately loose. It rejects the shapes that cannot be recovered from - a window with no
    /// size, or one positioned far enough off-screen that its title bar cannot be grabbed - and does
    /// not try to check it against the current monitor layout, which changes while the application is
    /// closed and would need Photino to enumerate screens.
    /// </remarks>
    private bool IsUsable() =>
        Width >= 640 && Height >= 480 &&
        Width <= 20_000 && Height <= 20_000 &&
        Left > -10_000 && Top > -10_000 &&
        Left < 20_000 && Top < 20_000;
}

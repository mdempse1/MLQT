namespace MLQT.Services;

/// <summary>Where a window sits, in the units the operating system uses.</summary>
public readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

/// <summary>
/// The size and position a desktop host opens at when the user has no saved one.
/// </summary>
/// <remarks>
/// <para>Here rather than in the host because it is arithmetic, and because getting it wrong is
/// invisible in the numbers. <b>MAUI sized windows in device-independent units; Photino sizes them in
/// physical pixels.</b> A host asking Photino for <c>1400 x 950</c> therefore opened <i>smaller</i>
/// than a MAUI window asking for <c>1200 x 900</c> — on the 125% display it was reported from, 1400
/// physical pixels is 1120 units, and 950 is 760 against MAUI's 900. A window a fifth shorter, from
/// code that appears to ask for a larger one.</para>
///
/// <para>Which unit Photino uses was measured, not assumed: with <c>SetSize(1400, 950)</c> the
/// self-test's <c>interop.dimensions</c> probe reported a 1106-pixel viewport, which is 1400 ÷ 1.25
/// less the window chrome. Sizing in units would have reported about 1390.</para>
/// </remarks>
public static class WindowGeometry
{
    /// <summary>The window MLQT opens at, in device-independent units.</summary>
    /// <remarks>
    /// The numbers the retired MAUI host opened at, kept so the application did not change size
    /// under the user when the host was replaced. Big enough for the tree, the code and the findings
    /// list side by side.
    /// </remarks>
    public const int PreferredWidth = 1200;
    public const int PreferredHeight = 900;

    /// <summary>The size an icon should be loaded at for a display of this DPI.</summary>
    /// <param name="dpi">Dots per inch: 96 at 100%, 120 at 125%, 144 at 150%.</param>
    /// <param name="baseSize">The size at 100% — 32 for a taskbar icon, 16 for a title bar one.</param>
    /// <remarks>
    /// <para>An icon handed to a window is a fixed number of pixels, and Windows scales it to
    /// whatever the taskbar wants. Handing it the 100% size on a scaled display means it is stretched:
    /// at 125% the taskbar wants 40 and gets 32, which is the difference between a crisp icon and a
    /// soft one. A multi-size <c>.ico</c> has a 48 to scale <i>down</i> from instead, which is the
    /// direction that looks right.</para>
    ///
    /// <para>Integer arithmetic on purpose: the standard scalings all divide exactly (120 → 40,
    /// 144 → 48, 192 → 64), so there is no rounding to argue about.</para>
    /// </remarks>
    public static int IconSize(int dpi, int baseSize) =>
        dpi <= 0 ? baseSize : baseSize * dpi / 96;

    /// <summary>
    /// The preferred window, scaled to the display and centred in the space available to it.
    /// </summary>
    /// <param name="work">The monitor's <b>work area</b> — its bounds less the taskbar and any other
    /// reserved edges — in physical pixels.</param>
    /// <param name="scale">Physical pixels per device-independent unit: 1.0 at 100%, 1.5 at 150%.</param>
    /// <remarks>
    /// <para>Clamped to the work area rather than the monitor bounds, so a window on a short screen
    /// does not open underneath the taskbar with its own status bar out of reach.</para>
    ///
    /// <para>Centred, because MAUI centred. A window that opens somewhere else on a machine the user
    /// has been running MLQT on for a year reads as a different application.</para>
    /// </remarks>
    public static WindowBounds Centred(WindowBounds work, double scale)
    {
        // A display that reports no scale is a display at 100%, not one of zero size. The upper guard
        // is for a value that is plainly not a scale factor - the whole window would be off-screen and
        // unrecoverable without editing the settings file.
        if (scale is <= 0 or > 10 || double.IsNaN(scale))
            scale = 1.0;

        var width = (int)Math.Min(PreferredWidth * scale, work.Width);
        var height = (int)Math.Min(PreferredHeight * scale, work.Height);

        return new WindowBounds(
            Left: work.Left + (work.Width - width) / 2,
            Top: work.Top + (work.Height - height) / 2,
            Width: width,
            Height: height);
    }
}

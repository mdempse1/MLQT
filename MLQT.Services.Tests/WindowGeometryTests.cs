using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The window a desktop host opens at on first run.
/// </summary>
/// <remarks>
/// <para>Phase 7b-5, from the only two differences a user could find between the MAUI build and the
/// Photino one. This was the first: the Photino window was visibly smaller, from code asking for
/// <c>1400 x 950</c> against MAUI's <c>1200 x 900</c>. <b>MAUI sizes in device-independent units and
/// Photino in physical pixels</b>, so on a 125% display the larger-looking numbers produced a window
/// a fifth shorter.</para>
///
/// <para>Tested here rather than in the host because none of it needs a display, and because a defect
/// that cost a user nothing but a raised eyebrow this time would have been a window opening
/// off-screen on the next monitor layout.</para>
/// </remarks>
public class WindowGeometryTests
{
    /// <summary>A 1920x1080 monitor with a taskbar along the bottom.</summary>
    private static readonly WindowBounds Laptop = new(0, 0, 1920, 1040);

    [Fact]
    public void AtOneHundredPercentItIsThePreferredSize()
    {
        var bounds = WindowGeometry.Centred(Laptop, 1.0);

        Assert.Equal(1200, bounds.Width);
        Assert.Equal(900, bounds.Height);
    }

    [Theory]
    [InlineData(1.25, 1500, 1125)]
    [InlineData(1.5, 1800, 1350)]
    [InlineData(2.0, 2400, 1800)]
    public void AScaledDisplayGetsAProportionallyLargerWindow(double scale, int width, int height)
    {
        // The point of the whole class. The same window in units means more physical pixels on a
        // scaled display, and asking for a fixed pixel count is how the two hosts came to look
        // different. A 4K work area, so nothing here is clamped and the scaling is what is measured.
        var bounds = WindowGeometry.Centred(new WindowBounds(0, 0, 3840, 2100), scale);

        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
    }

    [Fact]
    public void ItIsCentredInTheWorkArea()
    {
        var bounds = WindowGeometry.Centred(Laptop, 1.0);

        Assert.Equal((1920 - 1200) / 2, bounds.Left);
        Assert.Equal((1040 - 900) / 2, bounds.Top);
    }

    [Fact]
    public void ItIsCentredOnTheMonitorItIsGiven_NotOnTheFirstOne()
    {
        // A second monitor's work area starts where the first one ends, and the offset has to come
        // through or the window opens on the wrong screen - which on a two-monitor desk is the
        // difference between "it opened" and "it did not".
        var second = new WindowBounds(1920, 0, 1920, 1040);

        var bounds = WindowGeometry.Centred(second, 1.0);

        Assert.Equal(1920 + (1920 - 1200) / 2, bounds.Left);
    }

    [Fact]
    public void ANegativeOffsetMonitorIsHandled()
    {
        // A monitor placed to the left of the primary one has negative coordinates. Windows allows it
        // and users do it.
        var left = new WindowBounds(-1920, 0, 1920, 1040);

        var bounds = WindowGeometry.Centred(left, 1.0);

        Assert.Equal(-1920 + (1920 - 1200) / 2, bounds.Left);
    }

    // ---- not bigger than the screen ------------------------------------------------------------

    [Fact]
    public void ASmallScreenGetsAWindowThatFitsIt()
    {
        // MAUI took min(preferred, screen) and so does this. A window larger than the display puts
        // its own controls out of reach.
        var small = new WindowBounds(0, 0, 1024, 720);

        var bounds = WindowGeometry.Centred(small, 1.0);

        Assert.Equal(1024, bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void AWindowThatFillsTheWorkAreaSitsAtItsOrigin()
    {
        var small = new WindowBounds(37, 11, 1024, 720);

        var bounds = WindowGeometry.Centred(small, 1.0);

        Assert.Equal(37, bounds.Left);
        Assert.Equal(11, bounds.Top);
    }

    [Fact]
    public void TheWorkAreaIsWhatIsFilled_NotTheMonitor()
    {
        // Given the work area, the window never overlaps the taskbar. Stated because passing the
        // monitor bounds instead is a one-word mistake with no visible symptom until the status bar
        // is behind the taskbar.
        var bounds = WindowGeometry.Centred(Laptop, 2.0);

        Assert.Equal(1040, bounds.Height);
        Assert.Equal(0, bounds.Top);
    }

    // ---- icon sizes ----------------------------------------------------------------------------

    [Theory]
    [InlineData(96, 32, 32)]     // 100%
    [InlineData(120, 32, 40)]    // 125% - the taskbar wants 40, and was being handed 32 to stretch
    [InlineData(144, 32, 48)]    // 150%
    [InlineData(192, 32, 64)]    // 200%
    [InlineData(120, 16, 20)]    // the title bar's icon scales the same way
    public void AnIconIsLoadedAtTheSizeTheDisplayWants(int dpi, int baseSize, int expected)
    {
        Assert.Equal(expected, WindowGeometry.IconSize(dpi, baseSize));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    public void AnUnreadableDpiFallsBackToTheUnscaledSize(int dpi)
    {
        // Zero is what an uninitialised window reports. Multiplying by it asks for a zero-pixel icon,
        // which loads nothing at all and leaves the window with no icon - worse than an unscaled one.
        Assert.Equal(32, WindowGeometry.IconSize(dpi, 32));
    }

    // ---- a display that reports nonsense -------------------------------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(100.0)]
    public void AnImpossibleScaleIsTreatedAsOneHundredPercent(double scale)
    {
        // Zero is what an unread monitor reports, and the rest are what a wrong unit would look like.
        // Falling back to 100% gives a usable window; multiplying by them gives one nobody can reach.
        var bounds = WindowGeometry.Centred(Laptop, scale);

        Assert.Equal(1200, bounds.Width);
        Assert.Equal(900, bounds.Height);
    }

    // ---- B149: is the remembered window still on a screen? --------------------------------------

    /// <summary>The second screen the laptop was docked to, arranged to the right of it.</summary>
    private static readonly WindowBounds SecondScreen = new(1920, 0, 2560, 1400);

    [Fact]
    public void AWindowOnTheOnlyDisplayIsOnScreen()
    {
        Assert.True(WindowGeometry.IsOnScreen(new WindowBounds(100, 100, 1200, 900), [Laptop]));
    }

    [Fact]
    public void AWindowLeftOnASecondScreenThatHasGoneIsNot()
    {
        // The case this exists for. Dock, open MLQT on the second screen, close it, undock: the
        // window reopens at x=2200 on a 1920-wide display, invisible and impossible to drag back.
        var remembered = new WindowBounds(2200, 300, 1400, 1000);

        Assert.True(WindowGeometry.IsOnScreen(remembered, [Laptop, SecondScreen]));   // while docked
        Assert.False(WindowGeometry.IsOnScreen(remembered, [Laptop]));                // after undocking
    }

    [Fact]
    public void AScreenAboveOrLeftOfThePrimaryIsStillAScreen()
    {
        // A monitor arranged left of or above the primary has negative coordinates, and a window on
        // it is perfectly valid. A rule that simply rejected negatives would move it for no reason.
        var toTheLeft = new WindowBounds(-1920, 0, 1920, 1040);
        var window = new WindowBounds(-1800, 100, 1200, 900);

        Assert.True(WindowGeometry.IsOnScreen(window, [Laptop, toTheLeft]));
        Assert.False(WindowGeometry.IsOnScreen(window, [Laptop]));
    }

    [Theory]
    // Just enough title bar to grab counts...
    [InlineData(1920 - WindowGeometry.MinimumVisibleWidth, 1040 - WindowGeometry.MinimumVisibleHeight, true)]
    // ...and one pixel less of it, in either direction, does not.
    [InlineData(1920 - WindowGeometry.MinimumVisibleWidth + 1, 1040 - WindowGeometry.MinimumVisibleHeight, false)]
    [InlineData(1920 - WindowGeometry.MinimumVisibleWidth, 1040 - WindowGeometry.MinimumVisibleHeight + 1, false)]
    public void ASliverOffTheEdgeIsNotEnoughToGrab(int left, int top, bool onScreen)
    {
        Assert.Equal(onScreen, WindowGeometry.IsOnScreen(new WindowBounds(left, top, 1200, 900), [Laptop]));
    }

    [Fact]
    public void NoMonitorsMeansNothingToJudgeAgainst()
    {
        // A headless machine, or a display not ready yet, reports none. Answering "not on screen"
        // would move every window on every launch, which is worse than leaving it where it was.
        Assert.False(WindowGeometry.IsOnScreen(new WindowBounds(0, 0, 1200, 900), []));
    }

    [Fact]
    public void MovingAWindowBackKeepsTheSizeTheUserChose()
    {
        // Their size is the half of the placement they actually chose. Falling back to the default
        // window would take a deliberately large one away over an unplugged cable.
        var moved = WindowGeometry.MovedOnto(new WindowBounds(2200, 300, 1400, 1000), Laptop);

        Assert.Equal(1400, moved.Width);
        Assert.Equal(1000, moved.Height);
        Assert.True(WindowGeometry.IsOnScreen(moved, [Laptop]));
    }

    [Fact]
    public void AWindowTooBigForTheScreenItMovesToIsShrunkToFit()
    {
        // The 2560x1400 second screen is gone and the window had nearly filled it.
        var moved = WindowGeometry.MovedOnto(new WindowBounds(2000, 0, 2400, 1300), Laptop);

        Assert.Equal(1920, moved.Width);
        Assert.Equal(1040, moved.Height);
        Assert.Equal(0, moved.Left);
        Assert.Equal(0, moved.Top);
    }

    [Fact]
    public void AMovedWindowIsCentredOnTheScreenItLandsOn()
    {
        var moved = WindowGeometry.MovedOnto(new WindowBounds(5000, 5000, 800, 600), Laptop);

        Assert.Equal((1920 - 800) / 2, moved.Left);
        Assert.Equal((1040 - 600) / 2, moved.Top);
    }

}

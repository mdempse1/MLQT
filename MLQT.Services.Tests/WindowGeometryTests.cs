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
}

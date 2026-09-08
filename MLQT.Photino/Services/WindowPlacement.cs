using MLQT.Services.Interfaces;
using Photino.NET;

namespace MLQT.Photino.Services;

/// <summary>
/// Where the window was last time.
/// </summary>
/// <remarks>
/// <para>MAUI restored this for us; Photino does not, so it is the host's job. Stored through
/// <see cref="ISettingsService"/> like everything else, which means it lands in the same JSON file
/// and will need the same migration.</para>
///
/// <para>A saved placement is only used if it still lands somewhere usable. A window restored to a
/// monitor that is no longer attached is invisible and unrecoverable without editing the settings
/// file, which is the failure worth guarding: the numbers are checked for sanity rather than trusted,
/// and anything odd falls back to the default.</para>
/// </remarks>
internal sealed record WindowPlacement(int Left, int Top, int Width, int Height)
{
    private const string Key = "WindowPlacement";

    /// <summary>A first-run window: large enough for the tree, the editor and the findings list.</summary>
    public static WindowPlacement Default { get; } = new(Left: 80, Top: 60, Width: 1400, Height: 950);

    public static WindowPlacement Restore(ISettingsService settings)
    {
        var saved = settings.GetAsync<WindowPlacement?>(Key, null).GetAwaiter().GetResult();

        return saved is not null && saved.IsUsable() ? saved : Default;
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

using Microsoft.Playwright;

namespace MLQT.Journeys;

/// <summary>
/// Waits until nothing is covering the shell, so the next click reaches what it is aimed at.
/// </summary>
/// <remarks>
/// <para>One helper rather than a copy per journey. Two journeys carried their own
/// <c>DismissTooltipsAsync</c>, both were right about tooltips, and when a second kind of overlay
/// turned up (B154) neither knew about it — which is the shape this repository keeps meeting.</para>
///
/// <para><b>Two things can be in front of the tab strip, and they are different problems.</b></para>
///
/// <para>A <b>tooltip</b>: the top-level tabs carry <c>ToolTip=</c>, so clicking one opens a popover
/// positioned over the strip, and it then intercepts the *next* click. Moving the pointer away
/// closes it.</para>
///
/// <para>A <b>modal dialog's scrim</b>: MLQT raises a non-closable progress dialog while it is
/// loading repositories or running a deferred style check, and the Metrics tab starts one of those
/// by being opened. Nothing a test does closes it — it closes when the work finishes — so the only
/// correct thing to do is wait, which is what a user does too. Journeys share one host, so a library
/// another journey loaded is enough to make the Metrics tab start a check here.</para>
///
/// <para>Waiting rather than forcing the click through: a forced click would also sail through a
/// real overlay covering the control, and that is a defect worth failing on.</para>
/// </remarks>
public static class ShellReadiness
{
    /// <summary>How long a progress dialog may stay up before it counts as stuck rather than busy.</summary>
    /// <remarks>
    /// Generous, because what is behind it is real analysis over whatever a previous journey loaded,
    /// and mean enough that a dialog nothing will ever close fails the test instead of hanging it.
    /// </remarks>
    private const int ScrimTimeoutMs = 60_000;

    public static async Task WaitUntilClickableAsync(IPage page)
    {
        // Off the tab strip, so any tooltip the last click opened closes itself.
        await page.Mouse.MoveAsync(0, 0);

        await page.Locator(".mud-popover-open").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        // Nothing matches when no dialog is up, and Playwright treats "hidden" as already satisfied
        // then - so this costs nothing on the ordinary path.
        await page.Locator(".mud-overlay-scrim").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = ScrimTimeoutMs });
    }
}

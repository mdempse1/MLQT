using System.Diagnostics;

namespace MLQT.Services;

/// <summary>
/// Opens a URL in the user's own browser, rather than inside MLQT's webview.
/// </summary>
/// <remarks>
/// <para>MLQT had two ways of doing this and only one of them worked everywhere. The About dialog
/// called <c>window.open</c> through JS interop, which WebView2 turns into a browser launch and
/// <b>WebKitGTK does nothing with at all</b> — the buttons were dead on Linux, silently, because a
/// popup the host declines to create raises nothing. <c>CreatePullRequestDialog</c> meanwhile shelled
/// out, which works on both. Phase 7a predicted this exact split ("<c>window.open</c> behaves
/// differently under WebKitGTK and may need routing through a native shell-open"); B138 is it
/// happening.</para>
///
/// <para>So: one way, and it is the one that does not depend on the engine. <see cref="Process"/> with
/// <c>UseShellExecute</c> hands the URL to the operating system — <c>ShellExecute</c> on Windows,
/// <c>xdg-open</c> on Linux — which is also the behaviour a user expects, since it honours their
/// default browser rather than the webview's idea of one.</para>
///
/// <para>Here rather than in a host because it needs nothing a host provides, and a platform service
/// would mean two implementations of a call that is already platform-independent.</para>
/// </remarks>
public static class ExternalBrowser
{
    /// <summary>
    /// Whether this is a URL we are willing to hand to the shell.
    /// </summary>
    /// <remarks>
    /// <para>Shell-execute will open whatever it is given — a document, an executable, a
    /// <c>file://</c> path — so what reaches it is worth being deliberate about even when every
    /// current caller passes a literal. <c>http</c> and <c>https</c> only.</para>
    ///
    /// <para>Separated from <see cref="Open"/> so the decision can be tested: the launch itself
    /// cannot be, because asserting on it means opening a browser on whatever machine runs the
    /// suite.</para>
    /// </remarks>
    public static bool IsOpenable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Opens <paramref name="url"/> in the user's browser. Returns whether it was handed over.
    /// </summary>
    /// <remarks>
    /// Never throws. A machine with no registered handler — a bare Linux install with no
    /// <c>xdg-open</c> — is a reason to log and carry on, not to take down the dialog the user
    /// clicked in.
    /// </remarks>
    public static bool Open(string? url)
    {
        if (!IsOpenable(url))
        {
            LoggingService.Warn(nameof(ExternalBrowser), $"Refused to open '{url}': not an http(s) URL");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.Error(nameof(ExternalBrowser), $"Could not open '{url}' in a browser", ex);
            return false;
        }
    }
}

using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// <see cref="ExternalBrowser.IsOpenable"/> — what MLQT is willing to hand to the shell.
/// </summary>
/// <remarks>
/// <para>Only the decision is tested. <see cref="ExternalBrowser.Open"/> hands the URL to
/// <c>ShellExecute</c> or <c>xdg-open</c>, so a test that asserted on it would open a browser window
/// on whatever machine ran the suite — which is why the decision is a separate method at all.</para>
///
/// <para>The reason it exists: B138. The About dialog opened links with <c>window.open</c> through JS
/// interop, which WebView2 turns into a browser launch and WebKitGTK ignores entirely, so both
/// buttons were dead on Linux and silent about it.</para>
/// </remarks>
public class ExternalBrowserTests
{
    [Theory]
    [InlineData("https://github.com/mdempse1/MLQT")]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/a/b?c=d#e")]
    public void AnHttpUrl_IsOpenable(string url) =>
        Assert.True(ExternalBrowser.IsOpenable(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("github.com/mdempse1/MLQT")]      // no scheme, so not absolute
    [InlineData("/etc/passwd")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    public void AnythingThatIsNotAnAbsoluteHttpUrl_IsNot(string? url) =>
        Assert.False(ExternalBrowser.IsOpenable(url));

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    [InlineData("javascript:alert(1)")]
    public void OtherSchemes_AreRefused(string url)
    {
        // Shell-execute opens whatever it is given - a document, an executable, a local path - so the
        // scheme is worth being deliberate about even while every caller passes a literal. It costs
        // nothing now and is the check nobody adds after a caller starts passing something else.
        Assert.False(ExternalBrowser.IsOpenable(url));
    }

    [Fact]
    public void RefusingToOpen_ReturnsFalseRatherThanThrowing()
    {
        // The caller is a button in a dialog. A URL it cannot open is a message to show, not an
        // exception that takes the dialog down - and Open never reaches the shell here, because
        // IsOpenable has already said no.
        Assert.False(ExternalBrowser.Open("not a url"));
    }
}

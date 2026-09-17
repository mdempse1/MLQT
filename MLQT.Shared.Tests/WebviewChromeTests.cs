using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The desktop host turns off the webview's own chrome: its right-click menu and its developer tools.
/// </summary>
/// <remarks>
/// <para>Both are on by default in Photino, and both leak the fact that MLQT is a webview. Right-
/// clicking anywhere on the page in the shipped 7b builds produced WebView2's menu on Windows and
/// WebKitGTK's on Linux — offering to reload, to go back, and an <i>Inspect</i> entry that opened the
/// developer tools on MLQT's markup. None of those are things MLQT does, and the second is not
/// something a released application should offer at all.</para>
///
/// <para>Asserted as source text because there is nothing else to assert against: these are
/// properties of a native window that only exists once <c>Run</c> has been called, so there is no
/// in-process way to ask a built <c>PhotinoWindow</c> what it was configured with. What the test is
/// actually holding is that the calls are still <i>there</i> — the way this regresses is somebody
/// rewriting <c>Main</c> and not carrying them over, and the symptom is a menu nobody looks for until
/// a user reports it against a release.</para>
///
/// <para><b>Not</b> the DOM <c>contextmenu</c> event, which still fires on both platforms and is what
/// MLQT's own menus are built on — <c>spellCheck.js</c> opens the spelling correction menu from it.
/// If that ever stops working, this is the pair of lines to look at, but disabling the default menu is
/// not what would have broken it.</para>
/// </remarks>
public class WebviewChromeTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    private static string HostProgram() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "MLQT.Photino", "Program.cs"));

    [Fact]
    public void TheDefaultContextMenuIsDisabled()
    {
        Assert.Matches(
            new Regex(@"SetContextMenuEnabled\s*\(\s*false\s*\)"),
            HostProgram());
    }

    [Fact]
    public void TheDeveloperToolsAreOffUnlessAskedFor()
    {
        var source = HostProgram();

        // Not simply "false": the developer tools are the one thing here worth keeping a way back to
        // while debugging the host, so the call takes an environment variable in the shape the rest of
        // Program.cs uses. What matters is that an ordinary run — no variable set — gets none.
        Assert.Matches(new Regex(@"SetDevToolsEnabled\s*\("), source);
        Assert.Contains("MLQT_DEVTOOLS", source, StringComparison.Ordinal);
    }
}

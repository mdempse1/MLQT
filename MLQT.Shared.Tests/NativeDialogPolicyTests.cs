using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// A native dialog is never opened from inside WebView2's callback (B283).
/// </summary>
/// <remarks>
/// <para>A click reaches a component through WebView2's <c>WebMessageReceived</c> callback, and
/// Photino.Blazor runs the component's handler inline on that callback's stack. A dialog opened there
/// runs its nested message loop inside WebView2's event handler; anything Blazor renders meanwhile then
/// reaches WebView2 re-entrantly, and the runtime stops the process with <c>0x80000003</c> — which is
/// how MLQT crashed while a user browsed for a repository folder, with nothing in the log.</para>
///
/// <para><c>PhotinoFilePickerService.OnTheMessageLoopAsync</c> is the one safe way to open one. No test
/// can click through a native dialog, so this holds the rule at the only level it can be held: every
/// call that opens one, in either Photino application, is made inside it. The obvious way to write the
/// next picker is the crashing one.</para>
/// </remarks>
public class NativeDialogPolicyTests
{
    private static readonly Regex OpensADialog = new(@"\.Show(Open|Save)(File|Folder)\w*\s*\(|\.ShowMessage\s*\(",
        RegexOptions.Compiled);

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

    private static IEnumerable<(string File, int Line, string Text)> DialogCalls()
    {
        var root = RepositoryRoot();
        foreach (var project in new[] { "MLQT.Photino", "MLQT.McpTester" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
                        continue;
                    if (OpensADialog.IsMatch(lines[i]))
                        yield return (Path.GetRelativePath(root, file), i + 1, lines[i]);
                }
            }
        }
    }

    [Fact]
    public void EveryNativeDialogIsOpenedFromTheMessageLoop()
    {
        var root = RepositoryRoot();
        var offenders = DialogCalls()
            .Where(call => !File.ReadAllLines(Path.Combine(root, call.File))[call.Line - 1].Contains("OnTheMessageLoopAsync", StringComparison.Ordinal))
            .Select(call => $"{call.File}:{call.Line}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These open a native dialog directly. Called from a click, that runs inside WebView2's callback and "
            + "crashes the process when anything renders while the dialog is open (B283). Open it through "
            + "PhotinoFilePickerService.OnTheMessageLoopAsync: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheScanFindsTheDialogsItIsHolding()
    {
        // Guards the scan: a pattern that found nothing would pass the rule over an empty set.
        var calls = DialogCalls().ToList();

        Assert.Contains(calls, c => c.Text.Contains("ShowOpenFolder", StringComparison.Ordinal));
        Assert.Contains(calls, c => c.Text.Contains("ShowOpenFile", StringComparison.Ordinal));
    }
}

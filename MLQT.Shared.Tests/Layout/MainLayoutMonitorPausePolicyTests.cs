using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests.Layout;

/// <summary>
/// MainLayout's own writes hold off the file monitor for the whole working copy, through a
/// <c>MonitorPause</c> (B296, B325, B416).
/// </summary>
/// <remarks>
/// <para>Format All Files stopped and started the monitor for its own repository with a bare
/// <c>StopMonitoring</c>/<c>StartMonitoring</c> pair after B325 had moved the VCS pipeline and Refresh
/// onto a pause over every repository sharing the working copy: the watcher is shared, so a sibling
/// left watching recorded the formatter's writes as its own pending changes (B416). The pre-commit
/// format had the same pair.</para>
///
/// <para>Read as text, like <see cref="ReferenceLibrariesLoadPolicyTests"/>, because MainLayout cannot
/// be rendered without every service in the application behind it. The rule: a direct
/// <c>StopMonitoring</c> call in MainLayout is in the ledger below with its reason, and each method
/// that formats files pauses through <c>MonitorPause.Begin</c> over
/// <c>GetRepositoriesSharingWorkingCopy</c>.</para>
/// </remarks>
public class MainLayoutMonitorPausePolicyTests
{
    private static readonly Regex MethodStart =
        new(@"^    (private|internal|public|protected)\b.*\b(\w+)\s*\(", RegexOptions.Compiled);

    /// <summary>The methods allowed a direct StopMonitoring call, and why.</summary>
    private static readonly Dictionary<string, string> Ledger = new()
    {
        ["ProcessVcsFilesChangedAsync"] =
            "Stops its own repository, which the operation that queued it may already have stopped and handed over, "
            + "and holds its siblings with a MonitorPause (B325)",
    };

    /// <summary>The methods that write files and must pause the whole working copy.</summary>
    public static TheoryData<string> FormattingMethods => new()
    {
        "ProcessRepositorySettingsAsync",
        "FormatChangedFilesForCommitAsync",
    };

    private static string[] MainLayoutLines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared", "Layout", "MainLayout.razor.cs");
            if (File.Exists(candidate))
                return File.ReadAllLines(candidate);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("MainLayout.razor.cs not found from " + AppContext.BaseDirectory);
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    private static string? EnclosingMethod(string[] lines, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            var match = MethodStart.Match(lines[i]);
            if (match.Success)
                return match.Groups[2].Value;
        }
        return null;
    }

    private static IEnumerable<string> BodyOf(string[] lines, string method)
    {
        var start = Array.FindIndex(lines, l => MethodStart.Match(l) is { Success: true } m && m.Groups[2].Value == method);
        Assert.True(start >= 0, $"{method} not found in MainLayout - if it was renamed, point this guard at its successor");
        for (var i = start + 1; i < lines.Length && !MethodStart.IsMatch(lines[i]); i++)
        {
            if (!IsComment(lines[i]))
                yield return lines[i];
        }
    }

    [Fact]
    public void EveryDirectStopMonitoring_IsInTheLedger()
    {
        var lines = MainLayoutLines();
        var stops = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("FileMonitoringService.StopMonitoring(", StringComparison.Ordinal) && !IsComment(lines[i]))
            .Select(i => (Line: i + 1, Method: EnclosingMethod(lines, i)))
            .ToList();

        var offenders = stops.Where(s => s.Method is null || !Ledger.ContainsKey(s.Method))
            .Select(s => $"line {s.Line} in {s.Method}").ToList();

        Assert.True(offenders.Count == 0,
            "These stop the file monitor for one repository directly. Hold it off with a MonitorPause over "
            + "RepositoryService.GetRepositoriesSharingWorkingCopy, so a sibling in the same working copy does not "
            + "record the writes as its own and an exception cannot leave it unwatched (B296, B325, B416): "
            + string.Join(", ", offenders));
    }

    [Theory]
    [MemberData(nameof(FormattingMethods))]
    public void AFormattingMethod_PausesTheWholeWorkingCopy(string method)
    {
        var body = BodyOf(MainLayoutLines(), method).ToList();

        var begin = body.FindIndex(l => l.Contains("MonitorPause.Begin(", StringComparison.Ordinal));
        Assert.True(begin >= 0, $"{method} does not hold the monitor off with a MonitorPause (B416)");
        Assert.Contains("GetRepositoriesSharingWorkingCopy(", string.Join("\n", body.Skip(begin).Take(2)));
        Assert.DoesNotContain(body, l => l.Contains("FileMonitoringService.StartMonitoring(", StringComparison.Ordinal));
    }
}

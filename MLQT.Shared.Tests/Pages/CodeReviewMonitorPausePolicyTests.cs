namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// The Code Review page's own writes hold off the file monitor for the whole working copy, through a
/// <c>MonitorPause</c> (B296, B301, B376).
/// </summary>
/// <remarks>
/// <para>Split into files, applying an annotation and a spelling correction each stopped and started
/// the monitor for their own repository with a bare <c>StopMonitoring</c>/<c>StartMonitoring</c>
/// pair. The watcher is shared, so a sibling library in the same working copy heard the write as its
/// own change; and in the annotation and correction paths an exception from the reload skipped the
/// restart, leaving the repository unwatched for the rest of the session.</para>
///
/// <para>Read as text, like <c>MainLayoutMonitorPausePolicyTests</c> (B416). Split is also held by
/// behaviour in <see cref="CodeReviewSplitMonitorTests"/>; the other two write the user's file on
/// disk, which the page harness never creates. The page has no reason to stop the monitor any other
/// way, so the ledger is empty: a direct call is a failure.</para>
/// </remarks>
public class CodeReviewMonitorPausePolicyTests
{
    private static string[] CodeReviewLines()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared", "Pages", "CodeReview.razor.cs");
            if (File.Exists(candidate))
                return File.ReadAllLines(candidate);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("CodeReview.razor.cs not found from " + AppContext.BaseDirectory);
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal)
        || line.TrimStart().StartsWith("///", StringComparison.Ordinal);

    [Theory]
    [InlineData("FileMonitoringService.StopMonitoring(")]
    [InlineData("FileMonitoringService.StartMonitoring(")]
    public void ThePageNeverStopsOrStartsTheMonitorByHand(string call)
    {
        var lines = CodeReviewLines();
        var offenders = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains(call, StringComparison.Ordinal) && !IsComment(lines[i]))
            .Select(i => $"line {i + 1}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"CodeReview.razor.cs calls {call.TrimEnd('(')} directly. Hold the monitor off with "
            + "MonitorPause.Begin(FileMonitoringService, WorkingCopyOf(repository)), so a sibling in the same "
            + "working copy does not hear the write and an exception cannot leave it unwatched (B296, B301, B376): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryPauseCoversTheWholeWorkingCopy()
    {
        // The other half of the rule: a pause over one repository is the B301 defect with a using
        // around it.
        // A call is often wrapped after its first argument, so each is read with the line after it.
        var lines = CodeReviewLines();
        var pauses = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("MonitorPause.Begin(", StringComparison.Ordinal) && !IsComment(lines[i]))
            .Select(i => lines[i] + (i + 1 < lines.Length ? lines[i + 1] : ""))
            .ToList();

        Assert.NotEmpty(pauses);
        Assert.All(pauses, l => Assert.True(
            l.Contains("WorkingCopyOf(", StringComparison.Ordinal)
            || l.Contains("GetRepositoriesSharingWorkingCopy", StringComparison.Ordinal),
            $"This pause is not over the working copy: {l.Trim()}"));
    }
}

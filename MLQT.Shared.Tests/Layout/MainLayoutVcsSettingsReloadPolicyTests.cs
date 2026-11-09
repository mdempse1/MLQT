using Xunit;

namespace MLQT.Shared.Tests.Layout;

/// <summary>
/// The analysis pipeline after a VCS operation re-reads the repository's committed settings before it
/// formats or checks anything (B525).
/// </summary>
/// <remarks>
/// <para>The desktop app read <c>.mlqt/settings.json</c> when it loaded the project and never again, so
/// a pull that brought the team's new rules left it formatting and checking with the old ones until
/// the project was reloaded. What a re-read decides is <c>VcsSettingsReloader</c>'s, tested in
/// MLQT.Services.Tests; this holds MainLayout to asking it, and early enough.</para>
///
/// <para>Read as text, like <see cref="MainLayoutMonitorPausePolicyTests"/>, because MainLayout cannot be
/// rendered without every service in the application behind it.</para>
/// </remarks>
public class MainLayoutVcsSettingsReloadPolicyTests
{
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

    private static List<string> PipelineBody()
    {
        var lines = MainLayoutLines();
        var start = Array.FindIndex(lines, l => l.Contains("private async Task ProcessVcsFilesChangedAsync(", StringComparison.Ordinal));
        Assert.True(start >= 0, "ProcessVcsFilesChangedAsync not found in MainLayout - if it was renamed, point this guard at its successor");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("    private ", StringComparison.Ordinal));
        return [.. lines[start..end].Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))];
    }

    private static int First(List<string> body, string text) =>
        body.FindIndex(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void ThePipeline_RereadsTheSettings_BeforeItFormatsOrChecks()
    {
        var body = PipelineBody();

        var reload = First(body, "VcsSettingsReloader.Reload(");
        Assert.True(reload >= 0, "The VCS pipeline does not re-read the repository's settings (B525)");
        Assert.True(reload < First(body, "repository.StyleSettings"), "The settings are re-read after the formatter's are taken");
        Assert.True(reload < First(body, "SaveChangedFilesWithFormattingAsync("), "The settings are re-read after the files are formatted");
        Assert.True(reload < First(body, "StyleCheckingService.CheckModelsAsync("), "The settings are re-read after the classes are checked");
    }

    [Fact]
    public void NewRules_ReachEveryClassInTheRepository()
    {
        var body = string.Join("\n", PipelineBody());

        Assert.Contains("if (settingsReload.ChecksChanged)", body);
    }

    [Fact]
    public void ARevert_RereadsTheSettings_Too()
    {
        // A revert takes the analysis-only pipeline, and can put back the committed settings file.
        var lines = MainLayoutLines();
        var start = Array.FindIndex(lines, l => l.Contains("private void OnVcsModelsChanged(", StringComparison.Ordinal));
        Assert.True(start >= 0, "OnVcsModelsChanged not found in MainLayout - if it was renamed, point this guard at its successor");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("    private ", StringComparison.Ordinal));
        var body = lines[start..end].Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToList();

        var reload = First(body, "VcsSettingsReloader.Reload(");
        Assert.True(reload >= 0, "A revert does not re-read the repository's settings (B525)");
        Assert.True(reload < First(body, "StyleCheckingService.CheckModelsAsync("), "The settings are re-read after the classes are checked");
    }
}

using MLQT.Services.DataTypes;
using MLQT.Shared.Layout;
using ModelicaGraph;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Shared.Tests.Layout;

/// <summary>
/// In deferred mode the resource watchers start when <c>MLQT.Resource.Missing</c> needs them, not
/// only when the External Resources tab is asked for.
/// </summary>
/// <remarks>
/// The rule runs with style checking, the watchers ran with the tab: with the tab never opened, a
/// restored file raised no event and its finding stayed until the project was reloaded. The decision
/// is tested directly; the call is checked by reading MainLayout, which cannot be rendered without
/// every service behind it (as <see cref="MainLayoutMonitorPausePolicyTests"/> does).
/// </remarks>
public class DeferredResourceWatchingTests
{
    private static Repository With(StyleCheckingSettings? settings) =>
        new() { Id = Guid.NewGuid().ToString("N"), Name = "Lib", LocalPath = "", StyleSettings = settings };

    [Fact]
    public void ARepositoryWithTheRuleOn_NeedsTheWatchers()
    {
        var on = new StyleCheckingSettings();
        on.SetRuleEnabled(RuleIds.MissingResource, true);

        Assert.True(MainLayout.AnyRepositoryReportsMissingResources([With(null), With(on)]));
    }

    [Fact]
    public void WithTheRuleOffEverywhere_TheTabStillDecides()
    {
        var other = new StyleCheckingSettings();
        other.SetRuleEnabled(RuleIds.AbsoluteResourcePath, true);

        Assert.False(MainLayout.AnyRepositoryReportsMissingResources([With(null), With(new StyleCheckingSettings()), With(other)]));
    }

    [Fact]
    public void TheDeferredDependencyPass_StartsTheResourceAnalysis_WhenTheRuleIsOn()
    {
        // Every deferred style check that runs the rule passes through the dependency pass first,
        // because the rule needs the edges.
        var source = MainLayoutSource();
        var start = source.IndexOf("private async Task RunDeferredDependenciesAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "RunDeferredDependenciesAsync not found - point this at its successor");

        var body = source[start..end];
        Assert.Contains("AnyRepositoryReportsMissingResources(RepositoryService.Repositories)", body);
        Assert.Contains("await RunDeferredExternalResourcesAsync();", body);
    }

    private static string MainLayoutSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MLQT.Shared", "Layout", "MainLayout.razor.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }
        throw new InvalidOperationException("MainLayout.razor.cs not found from " + AppContext.BaseDirectory);
    }
}

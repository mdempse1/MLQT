using MLQT.Services;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The baseline classification has to be right once a repository has finished loading, without
/// anything asking it to catch up.
///
/// <para>It was not. A library is attached to its repository <i>after</i> it finishes loading, so
/// every refresh triggered by the library-loaded event looked at a repository that owned no models
/// yet, found no baseline for it, and concluded there was none. Nothing fired again once the last
/// library was attached, so the answer stayed "no baseline" until something called
/// <see cref="IBaselineStatusService.Refresh"/> directly — which is what opening the Code Review tab
/// does. The count was therefore only ever right on a freshly-opened tab, and leaving it and coming
/// back looked like the fix.</para>
/// </summary>
public class BaselineStatusStartupTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-baseline-startup", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    /// <summary>A repository with one library and a committed baseline, as the CLI writes it.</summary>
    private void WriteRepository()
    {
        var lib = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(Path.Combine(_root, ".mlqt"));
        Directory.CreateDirectory(lib);

        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "package MyLib\n  model Widget\n  end Widget;\nend MyLib;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "Widget\n");
        File.WriteAllText(Path.Combine(_root, ".mlqt", "baseline.json"), """
            {
              "entries": [
                { "Fingerprint": "abc123", "RuleId": "MLQT.Doc.ClassDescription",
                  "Model": "MyLib.Widget", "Element": null, "Message": "missing description" }
              ]
            }
            """);
    }

    private sealed record Fixture(
        RepositoryService Repositories, BaselineStatusService Baseline, Func<int> Events);

    private Fixture Build()
    {
        var libraries = new LibraryDataService();
        var monitor = new FileMonitoringService();
        var repositories = new RepositoryService(libraries, new InMemorySettingsService(), monitor);
        var baseline = new BaselineStatusService(libraries, repositories, monitor);

        var events = 0;
        baseline.OnChanged += () => Interlocked.Increment(ref events);

        return new Fixture(repositories, baseline, () => Volatile.Read(ref events));
    }

    [Fact]
    public async Task AfterARepositoryLoads_TheBaselineIsFoundWithoutBeingAskedAgain()
    {
        WriteRepository();
        var fixture = Build();

        var added = await fixture.Repositories.AddRepositoryAsync(_root, checkoutPath: null, startMonitoring: false);
        Assert.True(added.Success, added.ErrorMessage);
        await fixture.Repositories.LoadLibrariesAsync(added.Repository!.Id);

        // Deliberately no Refresh() here: that is the call a freshly-opened tab makes, and relying on
        // it is what confined the correct answer to a freshly-opened tab.
        Assert.True(fixture.Baseline.HasBaseline);
    }

    [Fact]
    public async Task AfterARepositoryLoads_TheChangeIsAnnounced()
    {
        // Being correct is not enough — a view reads the snapshot when it renders, so it also has to
        // be told that rendering again is worthwhile.
        WriteRepository();
        var fixture = Build();

        var added = await fixture.Repositories.AddRepositoryAsync(_root, checkoutPath: null, startMonitoring: false);
        var before = fixture.Events();
        await fixture.Repositories.LoadLibrariesAsync(added.Repository!.Id);

        Assert.True(fixture.Events() > before,
            "loading a repository changed the classification without telling anyone");
    }

    [Fact]
    public async Task RefreshingAgainChangesNothing()
    {
        // The state after loading must already be the settled one, not a stage on the way to it.
        WriteRepository();
        var fixture = Build();

        var added = await fixture.Repositories.AddRepositoryAsync(_root, checkoutPath: null, startMonitoring: false);
        await fixture.Repositories.LoadLibrariesAsync(added.Repository!.Id);

        var settled = fixture.Events();
        fixture.Baseline.Refresh();

        Assert.Equal(settled, fixture.Events());
        Assert.True(fixture.Baseline.HasBaseline);
    }
}

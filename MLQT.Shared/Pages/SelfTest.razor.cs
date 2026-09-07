using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MLQT.Shared.Theming;
using RevisionControl;

namespace MLQT.Shared.Pages;

/// <summary>How a probe came out.</summary>
public enum ProbeStatus
{
    /// <summary>The probe ran and the host behaved.</summary>
    Pass,

    /// <summary>The probe ran and the host did not.</summary>
    Fail,

    /// <summary>The probe could not run here, and says why. Not a pass and not a failure.</summary>
    Skipped,
}

/// <summary>One probe's answer, as the baseline records it.</summary>
/// <param name="Id">Stable across hosts and runs; the key the baseline diff is taken on.</param>
/// <param name="Name">What it checks, for a person reading the table.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Detail">What it saw. Diagnostic only — deliberately not part of the comparison.</param>
public sealed record ProbeResult(
    string Id,
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ProbeStatus Status,
    string Detail);

public partial class SelfTest
{
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private ISettingsService Settings { get; set; } = null!;
    [Inject] private IPowerManagementService Power { get; set; } = null!;
    [Inject] private IFilePickerService FilePicker { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<ProbeResult> _results = [];
    private bool _running = true;
    private string _json = "";

    /// <summary>The whole run, as the file a host writes and the baseline is diffed against.</summary>
    private sealed record SelfTestReport(string Host, string RuntimeVersion, IReadOnlyList<ProbeResult> Probes);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _results = await RunProbesAsync();

        var report = new SelfTestReport(
            Host: HostName(),
            RuntimeVersion: Environment.Version.ToString(),
            Probes: _results);

        _json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

        _running = false;
        await InvokeAsync(StateHasChanged);

        WriteReportAndExitIfAsked();
    }

    /// <summary>Set to a path to have the report written there, after which the process exits.</summary>
    public const string OutputPathVariable = "MLQT_SELFTEST_OUT";

    /// <summary>
    /// Writes the report and stops the process, when a host was started to capture a baseline.
    /// </summary>
    /// <remarks>
    /// <para>Here rather than in each host, so every host captures its baseline the same way and a
    /// difference between two reports is a difference between two hosts.</para>
    ///
    /// <para>Exiting from a page is not something to do lightly, and it is deliberate: the
    /// alternative is a UI automation framework driving the real window, and the only one that can
    /// drive WebView2 speaks a protocol WebKitGTK does not — so that baseline would have to be
    /// thrown away at the moment it was needed. Nothing here runs unless a caller set the variable.</para>
    ///
    /// <para>The exit code is the answer: 0 when every probe passed, 1 when any failed. A capture
    /// script does not have to parse the file to know whether the host is sound.</para>
    /// </remarks>
    private void WriteReportAndExitIfAsked()
    {
        var path = Environment.GetEnvironmentVariable(OutputPathVariable);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, _json);
            LoggingService.Info(nameof(SelfTest), $"Self-test report written to {path}");
        }
        catch (Exception ex)
        {
            LoggingService.Error(nameof(SelfTest), $"Could not write the self-test report to {path}", ex);
            Environment.Exit(2);
        }

        Environment.Exit(_results.Any(r => r.Status == ProbeStatus.Fail) ? 1 : 0);
    }

    /// <summary>The environment variable a host uses to name itself in the report.</summary>
    public const string HostNameVariable = "MLQT_SELFTEST_HOST";

    /// <summary>
    /// Which host is running this — the field the whole comparison is keyed on.
    /// </summary>
    /// <remarks>
    /// Declared by the host rather than inferred from the entry assembly. Inferring it is right in
    /// the two cases that matter and wrong in the one that runs most often: a journey starts the
    /// test host in-process, where the entry assembly is the test runner, so a report captured that
    /// way would be labelled with the name of whatever launched it. A baseline whose identity
    /// depends on how it was produced is not a baseline.
    /// </remarks>
    private static string HostName() =>
        Environment.GetEnvironmentVariable(HostNameVariable)
        ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name
        ?? "unknown";

    private async Task<List<ProbeResult>> RunProbesAsync()
    {
        var results = new List<ProbeResult>();

        async Task Probe(string id, string name, Func<Task<(bool Ok, string Detail)>> run)
        {
            try
            {
                var (ok, detail) = await run();
                results.Add(new ProbeResult(id, name, ok ? ProbeStatus.Pass : ProbeStatus.Fail, detail));
            }
            catch (Exception ex)
            {
                // A probe that throws is a failure, never an aborted run: one broken probe must not
                // cost the report the other thirteen answers.
                results.Add(new ProbeResult(id, name, ProbeStatus.Fail, $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        // ---- interop ---------------------------------------------------------------------------

        await Probe("interop.dimensions", "getDimensions returns a real window size", async () =>
        {
            var d = await JS.InvokeAsync<Services.BrowserDimension>("getDimensions");
            return (d.Width > 0 && d.Height > 0, $"{d.Width}x{d.Height}");
        });

        await Probe("assets.rcl", "Razor class library static assets are served", async () =>
        {
            var status = await JS.InvokeAsync<int>("eval",
                "fetch('_content/MLQT.Shared/app.css').then(r => r.status).catch(() => 0)");
            return (status == 200, $"HTTP {status}");
        });

        await Probe("scripts.globals", "Every library script defined its global", async () =>
        {
            // klayjs is absent on purpose. It publishes klayregister/klaycallback rather than a
            // `klay` global and is only ever reached through cytoscape-klay, so the meaningful
            // question about it is whether that layout runs - which is the next probe's job.
            var missing = await JS.InvokeAsync<string[]>("eval", """
                ['cytoscape','dagre','cytoscapeGraph','diffViewer','spellCheck','getDimensions']
                    .filter(g => typeof window[g] === 'undefined')
                """);
            return (missing.Length == 0, missing.Length == 0 ? "all defined" : "missing: " + string.Join(", ", missing));
        });

        await Probe("cytoscape.layouts", "Every layout extension registered with Cytoscape", async () =>
        {
            // Each layout is a separate script that registers itself against the global Cytoscape
            // defines. One that failed to load does not throw at page load - it throws when a user
            // picks that layout, which on a new engine is the shape a report like this exists to
            // find early.
            var missing = await JS.InvokeAsync<string[]>("eval", """
                (function () {
                  var need = ['dagre','klay','fcose','spread'];
                  var have = (cytoscape && cytoscape().options) ? null : null;
                  return need.filter(function (n) {
                    try { return typeof cytoscape('layout', n) !== 'function'; }
                    catch (e) { return true; }
                  });
                })()
                """);
            return (missing.Length == 0, missing.Length == 0 ? "dagre, klay, fcose, spread" : "missing: " + string.Join(", ", missing));
        });

        await Probe("cytoscape.init", "Cytoscape draws a two-node graph", async () =>
        {
            var nodes = await JS.InvokeAsync<int>("eval", """
                (function () {
                  var id = 'selftest-cy';
                  var d = document.getElementById(id);
                  if (!d) { d = document.createElement('div'); d.id = id;
                            d.style.width = '200px'; d.style.height = '200px';
                            d.style.position = 'absolute'; d.style.left = '-9999px';
                            document.body.appendChild(d); }
                  window.cytoscapeGraph.init(id, [
                    { group: 'nodes', data: { id: 'A', label: 'A', color: '#6a70b1', borderColor: '#333' } },
                    { group: 'nodes', data: { id: 'B', label: 'B', color: '#6a70b1', borderColor: '#333' } },
                    { group: 'edges', data: { id: 'e', source: 'A', target: 'B' } }
                  ], null, 'dagre-tb');
                  var n = window._cytoscapeInstances[id].cy.nodes().length;
                  window.cytoscapeGraph.destroy(id);
                  return n;
                })()
                """);
            return (nodes == 2, $"{nodes} node(s)");
        });

        await Probe("interop.modules", "The diff and spell-check interop respond", async () =>
        {
            // initSyncScroll takes the elements themselves, not their ids - which the first version
            // of this probe got wrong and reported as a host failure.
            var ok = await JS.InvokeAsync<bool>("eval", """
                (function () {
                  try {
                    var a = document.createElement('div'), b = document.createElement('div');
                    document.body.appendChild(a); document.body.appendChild(b);
                    window.diffViewer.initSyncScroll(a, b);
                    window.diffViewer.dispose();
                    a.remove(); b.remove();
                    return typeof window.spellCheck.init === 'function';
                  } catch (e) { return false; }
                })()
                """);
            return (ok, ok ? "both responded" : "one of them threw");
        });

        await Probe("mudblazor.overlays", "A dialog, a snackbar and the popover layer reach the DOM", async () =>
        {
            // The probe the design note asked for and the first pass left out. MudBlazor positions
            // its overlays from JavaScript, and that is the part most likely to behave differently
            // under WebKitGTK - a dialog that renders off-screen or a popover that never appears is
            // the failure mode, and it is invisible to every test that does not involve a browser.
            //
            // What this proves is that the overlays reach the DOM. It does not prove they are in the
            // right place: comparing pixel geometry across two engines produces differences on every
            // glyph, which the note rules out on purpose. Where they land stays a human's judgement,
            // once, per platform.
            var reference = await Dialogs.ShowAsync<SelfTestProbeDialog>("self-test");
            Snackbar.Add("self-test", Severity.Normal);

            // Both are rendered by providers that re-render in response to this call, so the DOM is
            // a frame behind until the renderer catches up.
            await InvokeAsync(StateHasChanged);
            await Task.Delay(250);

            var found = await JS.InvokeAsync<string[]>("eval", """
                ['.mud-dialog', '.mud-snackbar', '.mud-popover-provider']
                    .filter(sel => document.querySelector(sel) === null)
                """);

            reference.Close();
            Snackbar.Clear();

            return (found.Length == 0,
                    found.Length == 0 ? "dialog, snackbar and popover layer all present"
                                      : "missing from the DOM: " + string.Join(", ", found));
        });

        await Probe("fonts.roboto", "Roboto is available to the page", async () =>
        {
            var available = await JS.InvokeAsync<bool>("eval",
                "(document.fonts && document.fonts.check) ? document.fonts.check('12px Roboto') : false");
            // Not a failure when absent: the font comes from fonts.googleapis.com, so an offline
            // machine or a locked-down network legitimately has no Roboto. Recorded either way so
            // the baseline diff shows a host where it stopped arriving.
            return (true, available ? "available" : "not available (network font)");
        });

        // ---- platform services -----------------------------------------------------------------

        await Probe("settings.roundtrip", "Settings survive a write, read and delete", async () =>
        {
            const string key = "__mlqt_selftest";
            var written = new UISettings { Theme = Theme.Dark, CustomPrimary = "#123456" };

            await Settings.SetAsync(key, written);
            var read = await Settings.GetAsync<UISettings?>(key, null);
            await Settings.RemoveAsync(key);
            var afterRemove = await Settings.GetAsync<UISettings?>(key, null);

            var ok = read is not null
                     && read.Theme == Theme.Dark
                     && read.CustomPrimary == "#123456"
                     && afterRemove is null;
            return (ok, ok ? "round-tripped a complex type" : "the value did not survive");
        });

        await Probe("settings.location", "Settings have a real backing store", () =>
        {
            // The round-trip above cannot tell persistence from the appearance of it: an
            // implementation holding values in a dictionary passes it and loses everything on
            // restart. This does not prove persistence either - it makes the store visible, so the
            // baseline records where settings lived under MAUI and a new host's answer is a diff
            // rather than a guess. It is the note's probe 9, which the first pass folded into the
            // round-trip and thereby lost.
            var store = Settings.BackingStore;

            if (string.IsNullOrWhiteSpace(store))
                return Task.FromResult((false, "the settings service does not say where it stores anything"));

            // When the store is a path, the directory holding it has to exist - that is the
            // XDG-versus-LocalAppData failure, and it is the one case this can check properly.
            if (Path.IsPathRooted(store))
            {
                var directory = Path.GetDirectoryName(store);
                var exists = !string.IsNullOrEmpty(directory) && Directory.Exists(directory);
                return Task.FromResult((exists, exists ? store : $"{store} (its directory does not exist)"));
            }

            return Task.FromResult((true, store));
        });

        await Probe("power.sleep", "Sleep prevention can be turned on and off", () =>
        {
            Power.PreventSleep();
            Power.AllowSleep();
            return Task.FromResult((true, "both calls returned"));
        });

        await Probe("filepicker.wired", "A file picker is wired up", () =>
            // Deliberately does not open one: a modal native dialog would hang the run. That a
            // picker resolves is what this can honestly say.
            Task.FromResult((FilePicker is not null, FilePicker?.GetType().Name ?? "none")));

        // ---- the environment the app assumes ---------------------------------------------------

        await Probe("culture.invariant", "Numbers parse and format invariantly", () =>
        {
            var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            var parsed = double.TryParse("1.5", NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
                         && Math.Abs(value - 1.5) < 1e-9;
            return Task.FromResult((separator == "." && parsed, $"separator '{separator}', 1.5 parsed: {parsed}"));
        });

        await Probe("logging.writes", "This process wrote a line to the log file", () =>
        {
            // Asks whether *this run* logged, not whether the folder has a log file in it. The first
            // version asked the latter and passed on any machine where the app had ever run - which
            // is every developer machine and no clean one. It reported Pass on a host that had not
            // initialised logging at all, and a Linux CI runner was the first thing to disagree.
            var token = "selftest-" + Guid.NewGuid().ToString("N");
            LoggingService.Info(nameof(SelfTest), token);
            LoggingService.Flush();

            var directory = LoggingService.LogDirectory;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return Task.FromResult((false, $"logging was never initialised (directory: {directory ?? "none"})"));

            var newest = new DirectoryInfo(directory)
                .EnumerateFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest is null)
                return Task.FromResult((false, $"no log file under {directory}"));

            // Shared read: NLog has the file open when KeepFileOpen is on, and a probe must not be
            // the reason a host cannot log.
            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var wrote = reader.ReadToEnd().Contains(token, StringComparison.Ordinal);

            return Task.FromResult((wrote, wrote ? directory : $"{newest.FullName} did not receive this run's line"));
        });

        await Probe("svn.client", "An svn client can be found", () =>
        {
            var path = SvnToolLocator.SvnExecutablePath;
            // Not a failure when absent - SVN is optional, and the bundled payload is Windows-only.
            // Recorded so the Linux host's answer can be compared with this one deliberately.
            return Task.FromResult((true, path ?? "none on PATH and none bundled"));
        });

        await Probe("theme.builds", "The application theme builds", () =>
        {
            var theme = MlqtTheme.BuildTheme(MlqtTheme.GetDefaultPaletteLight());
            var ok = theme.PaletteDark is not null && theme.Typography?.Body1?.FontSize == "0.75rem";
            return Task.FromResult((ok, ok ? "palette and typography present" : "theme is not as configured"));
        });

        return results;
    }

    private static string StatusClass(ProbeStatus status) => status switch
    {
        ProbeStatus.Pass => "mud-success-text",
        ProbeStatus.Fail => "mud-error-text",
        _ => "mud-secondary-text",
    };
}

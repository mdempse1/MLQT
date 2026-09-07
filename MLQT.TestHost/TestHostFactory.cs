using MLQT.Services.Interfaces;
using MLQT.Shared;
using MLQT.TestHost.Components;
using MLQT.TestHost.Services;

namespace MLQT.TestHost;

/// <summary>
/// Builds the test host, for <c>Program</c> to run or for a journey fixture to start in-process.
/// </summary>
/// <remarks>
/// One definition rather than two: a journey that started a differently-configured host would be
/// testing something the host does not do.
/// </remarks>
public static class TestHostFactory
{
    public static WebApplication Build(string[]? args = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],

            // Named explicitly because a journey starts this host *in-process*, where the entry
            // assembly is MLQT.Journeys. Static web assets are found through a manifest named after
            // the application - MLQT.TestHost.staticwebassets.runtime.json - so without this the
            // lookup asks for MLQT.Journeys.staticwebassets.runtime.json, finds nothing, and every
            // _content/... path 404s. It works when run with `dotnet run` and fails under the
            // journeys, which is the worst shape a configuration bug can have.
            ApplicationName = typeof(TestHostFactory).Assembly.GetName().Name,
        });

        // Serve the Razor class library's wwwroot - every _content/MLQT.Shared/... path the page asks
        // for. CreateBuilder only wires this up automatically in the Development environment, and the
        // journeys run wherever CI puts them: without it the page loads, every script 404s, and the
        // failure surfaces as an empty Cytoscape box rather than as a missing file.
        //
        // This is probe 2 of the /selftest route in 7a-7, which the design note calls the single
        // likeliest Photino failure. It was the first thing to go wrong here too.
        builder.WebHost.UseStaticWebAssets();

        // The same list the desktop app registers, from the same place. That is the point of the
        // host: a journey that passes here exercised the implementation the app runs, not a copy.
        builder.Services.AddMlqtCore();

        // This host's three platform services - the same three MAUI supplies, and Photino will.
        // Registered concretely as well as behind the interface, so a journey can prime the picker
        // and read the sleep counts without casting.
        builder.Services.AddSingleton<ScriptedFilePickerService>();
        builder.Services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<ScriptedFilePickerService>());
        builder.Services.AddSingleton<InMemorySettingsService>();
        builder.Services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<InMemorySettingsService>());
        builder.Services.AddSingleton<RecordingPowerManagementService>();
        builder.Services.AddSingleton<IPowerManagementService>(sp => sp.GetRequiredService<RecordingPowerManagementService>());

        builder.Services.AddSingleton<PipelineQuiescence>();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            // MLQT's pages live in MLQT.Shared, not here. Without this the endpoint router finds
            // nothing routable and every request is a 404 - the Router inside Routes.razor never
            // gets a chance.
            .AddAdditionalAssemblies(typeof(Routes).Assembly);

        // Test-only, and unreachable from a shipped host: the journeys need to know when the
        // analysis pipeline has gone quiet, and waiting on a timer makes a suite that is slow when
        // it passes and flaky when it does not.
        app.MapGet("/testapi/idle", async (PipelineQuiescence quiescence, CancellationToken token) =>
        {
            var quiet = await quiescence.WaitForIdleAsync(token);
            return quiet ? Results.Ok("idle") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}

using MLQT.Services.Interfaces;
using MLQT.Shared;
using MLQT.TestHost;
using MLQT.TestHost.Components;
using MLQT.TestHost.Services;

var builder = WebApplication.CreateBuilder(args);

// Serve the Razor class library's wwwroot - every _content/MLQT.Shared/... path the page asks for.
// CreateBuilder only wires this up automatically in the Development environment, and the journeys
// run wherever CI puts them: without it the page loads, every script 404s, and the failure surfaces
// as an empty Cytoscape box rather than as a missing file.
//
// This is probe 2 of the /selftest route in 7a-7, and the design note calls it the single likeliest
// Photino failure. It was the first thing to go wrong here too.
builder.WebHost.UseStaticWebAssets();

// The same list the desktop app registers, from the same place. That is the point of the host: a
// journey that passes here exercised the implementation the app runs, not a copy of it.
builder.Services.AddMlqtCore();

// This host's three platform services - the same three MAUI supplies, the same three Photino will.
builder.Services.AddSingleton<ScriptedFilePickerService>();
builder.Services.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<ScriptedFilePickerService>());
builder.Services.AddSingleton<InMemorySettingsService>();
builder.Services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<InMemorySettingsService>());
builder.Services.AddSingleton<RecordingPowerManagementService>();
builder.Services.AddSingleton<IPowerManagementService>(sp => sp.GetRequiredService<RecordingPowerManagementService>());

// Registered concretely as well as behind the interface so a test can prime the picker and read the
// sleep counts without resolving through a cast.

builder.Services.AddSingleton<PipelineQuiescence>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // MLQT's pages live in MLQT.Shared, not here. Without this the endpoint router finds nothing
    // routable and every request is a 404 - the Router inside Routes.razor never gets a chance.
    .AddAdditionalAssemblies(typeof(MLQT.Shared.Routes).Assembly);

// Test-only, and never reachable from a shipped host: the journeys need to know when the analysis
// pipeline has gone quiet, and waiting on a timer makes a suite that is slow when it passes and
// flaky when it does not.
app.MapGet("/testapi/idle", async (PipelineQuiescence quiescence, CancellationToken token) =>
{
    var quiet = await quiescence.WaitForIdleAsync(token);
    return quiet ? Results.Ok("idle") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();

/// <summary>Exposed so the journey fixture can start this host in-process.</summary>
public partial class Program;

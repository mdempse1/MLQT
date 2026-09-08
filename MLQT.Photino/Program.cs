using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MLQT.Photino.Services;
using MLQT.Services;
using MLQT.Services.Interfaces;
using MLQT.Shared;
using MLQT.Shared.Components;
using MLQT.Shared.Pages;
using Photino.Blazor;

namespace MLQT.Photino;

/// <summary>
/// MLQT's desktop host, on Photino.
/// </summary>
/// <remarks>
/// <para>Phase 7b-2. Compare it with <c>MLQT/MauiProgram.cs</c>: both call <see
/// cref="MlqtServiceCollectionExtensions.AddMlqtCore"/>, add the same three platform services and a
/// renderer, and do nothing else. That is what 7a-6 was for — the composition root is shared, so a
/// host is the renderer plus the three implementations that reach the operating system.</para>
///
/// <para><b>Two things here are load-bearing and both fail silently</b>, which is why they carry
/// comments rather than being left to read as boilerplate. 7b-0 found each of them by running into
/// it, and on this host a page that fails to load is indistinguishable from a page that loads and
/// never starts: the window opens, logging initialises, and nothing else happens.</para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // (1) The file provider must be rooted at wwwroot explicitly. PhotinoBlazorAppConfiguration's
        // HostPage is "index.html" with no directory part, so the provider is expected to be
        // wwwroot-rooted already, and the parameterless CreateDefault does not do that.
        var wwwroot = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"));

        var builder = PhotinoBlazorAppBuilder.CreateDefault(wwwroot, args);

        // Everything that is not this host's own business: the services, MudBlazor, the invariant
        // culture and logging. Identical to the line in MauiProgram.
        builder.Services.AddMlqtCore();

        // The three that reach the operating system, and the whole of what a host contributes.
        builder.Services.AddSingleton<PhotinoWindowAccessor>();
        builder.Services.AddSingleton<IFilePickerService, PhotinoFilePickerService>();
        builder.Services.AddSingleton<ISettingsService, JsonSettingsService>();
        builder.Services.AddSingleton<IPowerManagementService, PowerManagementService>();

        // Self-test mode roots directly on the probes rather than on the router. Photino's
        // PhotinoBlazorApp loads "/" itself and ignores PhotinoWindow.StartUrl, so there is no
        // equivalent of MAUI's BlazorWebView.StartPath to navigate with - and the probes must not run
        // with MainLayout starting the application underneath them in any case.
        if (SelfTest.IsEnabled)
            builder.RootComponents.Add<SelfTestHost>("#app");
        else
            builder.RootComponents.Add<Routes>("#app");

        var app = builder.Build();

        // The picker needs the window to parent its dialogs, and the window only exists after Build.
        app.Services.GetRequiredService<PhotinoWindowAccessor>().Window = app.MainWindow;

        var placement = WindowPlacement.Restore(app.Services.GetRequiredService<ISettingsService>());
        // Photino logs every message it exchanges with the webview to stdout, and a Blazor render
        // batch is one of those messages - base64-encoded, and tens of kilobytes for an ordinary UI
        // update. Every console write is synchronous, so the cost lands on the thread producing the
        // update, and it scales with how often the UI changes: an idle window is fine and one with
        // the analysis pipeline running behind it is not. That is the shape of the slowness reported
        // against the first build of this host.
        //
        // 0 = silent. Set MLQT_PHOTINO_LOG to raise it when debugging the host itself; it is not
        // something an ordinary run should pay for.
        app.MainWindow.LogVerbosity =
            int.TryParse(Environment.GetEnvironmentVariable("MLQT_PHOTINO_LOG"), out var verbosity) ? verbosity : 0;

        app.MainWindow
           .SetTitle("MLQT")
           .SetSize(placement.Width, placement.Height)
           .SetLeft(placement.Left)
           .SetTop(placement.Top);

        app.MainWindow.WindowClosing += (_, _) =>
        {
            WindowPlacement.Save(app.Services.GetRequiredService<ISettingsService>(), app.MainWindow);
            return false;   // false = allow the close to proceed
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MLQT.Services.LoggingService.Error(nameof(Program), $"Unhandled: {e.ExceptionObject}");

        app.Run();
    }
}

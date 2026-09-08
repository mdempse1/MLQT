using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MLQT.McpTester.Components;
using MLQT.McpTester.Services;
using MudBlazor.Services;
using Photino.Blazor;

namespace MLQT.McpTester;

/// <summary>
/// The MCP tester's entry point, on Photino rather than MAUI (phase 7b-1).
/// </summary>
/// <remarks>
/// <para>This replaces <c>MauiProgram</c>, <c>App.xaml</c> and <c>MainPage.xaml</c> — three files and
/// a XAML tree — with one. That is the shape phase 7b promises for MLQT itself, and this app is the
/// rehearsal for it: same host model, same bootstrap, MudBlazor under the same engine, and nothing
/// that matters if it breaks.</para>
///
/// <para><b>Two things here are not optional, and both fail silently if you get them wrong.</b> 7b-0
/// found each of them the hard way and they are the reason this file has comments at all.</para>
/// </remarks>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The file provider has to be rooted at wwwroot explicitly. Photino's HostPage is
        // "index.html" with no directory part, so the provider is expected to be wwwroot-rooted
        // already - and the parameterless CreateDefault does not do that. Get it wrong and the
        // window opens showing the loading div, with no error anywhere.
        var wwwroot = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"));

        var builder = PhotinoBlazorAppBuilder.CreateDefault(wwwroot, args);

        builder.Services.AddMudServices();
        builder.Services.AddSingleton<McpClientService>();
        builder.Services.AddLogging();

        builder.RootComponents.Add<Routes>("#app");

        var app = builder.Build();

        // Photino logs every message it exchanges with the webview, including base64-encoded Blazor
        // render batches - about a megabyte of synchronous console writes per fifteen seconds of an
        // ordinary session. Silent by default; MLQT_PHOTINO_LOG raises it for debugging the host.
        app.MainWindow.LogVerbosity =
            int.TryParse(Environment.GetEnvironmentVariable("MLQT_PHOTINO_LOG"), out var verbosity) ? verbosity : 0;

        app.MainWindow
           .SetTitle("MLQT MCP Tester")
           .SetSize(1400, 900);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"Unhandled: {e.ExceptionObject}");

        app.Run();
    }
}

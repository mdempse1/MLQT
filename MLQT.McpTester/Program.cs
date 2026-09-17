using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MLQT.McpTester.Components;
using MLQT.McpTester.Services;
// StaticWebAssetManifest's own namespace. The type is compiled into this assembly from source rather
// than referenced — see the Compile Include in the project file for why.
using MLQT.Services;
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
        var builder = PhotinoBlazorAppBuilder.CreateDefault(WebAssets(), args);

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

    /// <summary>
    /// Where the web assets are, whether this build was published or just built.
    /// </summary>
    /// <remarks>
    /// <para><c>dotnet publish</c> writes a real <c>wwwroot</c>; <c>dotnet build</c> writes a manifest
    /// pointing at the originals — the project's own <c>wwwroot</c>, MudBlazor's
    /// <c>staticwebassets</c> folder in the NuGet cache, and so on. Only the first was handled here,
    /// so a Debug build did not start at all: <c>PhysicalFileProvider</c> throws
    /// <c>DirectoryNotFoundException</c> on a root that is not there, before the window is ever
    /// created.</para>
    ///
    /// <para>This is B133, which was found and fixed in <c>MLQT.Photino</c> and not here — even though
    /// this app was the rehearsal the port was done on first, and its <c>Main</c> carries the comment
    /// warning about the other half of the same trap. The two hosts now answer it the same way, from
    /// one shared <see cref="StaticWebAssetManifest"/>.</para>
    ///
    /// <para>The published folder is preferred when it exists: it is what ships, and a shipped
    /// application should not depend on a manifest full of absolute paths to this machine's NuGet
    /// cache.</para>
    /// </remarks>
    private static IFileProvider WebAssets()
    {
        var published = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(published))
            return new PhysicalFileProvider(published);

        var manifest = StaticWebAssetManifest.Load(
            StaticWebAssetManifest.PathFor(AppContext.BaseDirectory, typeof(Program).Assembly.GetName().Name!),
            (message, ex) => Console.Error.WriteLine($"{message}: {ex}"));

        if (manifest is not null)
            return new StaticWebAssetsFileProvider(manifest);

        // Neither. Say so rather than throwing out of PhysicalFileProvider with a bare directory name,
        // or opening a window that loads nothing - the two failures this method exists to tell apart.
        Console.Error.WriteLine(
            $"No web assets: neither {published} nor a static web assets manifest is beside the " +
            "executable, so the window would be blank. Run dotnet publish, or build the project so " +
            "the manifest is written.");

        return new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}

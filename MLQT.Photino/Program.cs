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
/// <para>Phase 7b-2, and the only host since the cutover in 7b-8. It was written to be the same
/// shape as the MAUI <c>MauiProgram</c> it replaced — both call <see
/// cref="MlqtServiceCollectionExtensions.AddMlqtCore"/>, add the same three platform services and a
/// renderer, and do nothing else — and that shape is what made a swap of hosts a small change.
/// That is what 7a-6 was for: the composition root is shared, so a host is the renderer plus the
/// three implementations that reach the operating system.</para>
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
        // Before anything that might have something to say. AddMlqtCore initialises logging too and
        // the call is idempotent, but the file provider is chosen before that line runs — and the one
        // message that matters there is "there are no web assets", which is the difference between a
        // blank window that explains itself and one that does not. Note that it explains itself *in
        // the log file*: logging is file-only unless MLQT_LOG_CONSOLE is set, so a blank window says
        // nothing in the terminal it was started from.
        LoggingService.Initialize();

        // (1) The file provider must be rooted at wwwroot explicitly. PhotinoBlazorAppConfiguration's
        // HostPage is "index.html" with no directory part, so the provider is expected to be
        // wwwroot-rooted already, and the parameterless CreateDefault does not do that.
        var builder = PhotinoBlazorAppBuilder.CreateDefault(WebAssets(), args);

        // Everything that is not this host's own business: the services, MudBlazor, the invariant
        // culture and logging. The MAUI host called the same line, which is what made it replaceable.
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

        MigrateMauiSettings(app.Services.GetRequiredService<ISettingsService>());


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

        var icon = ApplicationIcon();

        app.MainWindow
           .SetTitle("MLQT")
           .SetIconFile(icon);

        // Again, once the window exists. Photino attaches the icon during creation, which leaves the
        // taskbar button showing whatever it had when it appeared, and attaches the 100% size, which
        // a scaled display then stretches. See WindowIcon.
        app.MainWindow.RegisterWindowCreatedHandler((_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowIcon.Apply(icon);
        });

        WindowPlacement.Apply(app.Services.GetRequiredService<ISettingsService>(), app.MainWindow);

        app.MainWindow.WindowClosing += (_, _) =>
        {
            WindowPlacement.Save(app.Services.GetRequiredService<ISettingsService>(), app.MainWindow);
            return false;   // false = allow the close to proceed
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MLQT.Services.LoggingService.Error(nameof(Program), $"Unhandled: {e.ExceptionObject}");

        app.Run();
    }

    /// <summary>
    /// Where the web assets are, whether this build was published or just built.
    /// </summary>
    /// <remarks>
    /// <para><c>dotnet publish</c> writes a real <c>wwwroot</c>; <c>dotnet build</c> writes a manifest
    /// pointing at the originals. Only the first was handled, so <b>the host ran only from a publish</b>
    /// — F5 and <c>dotnet run</c> opened a window that loaded nothing, with no error, because a webview
    /// showing nothing is indistinguishable from one that is still starting.</para>
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
            StaticWebAssetManifest.PathFor(AppContext.BaseDirectory, nameof(MLQT) + ".Photino"));

        if (manifest is not null)
        {
            LoggingService.Info(nameof(Program),
                "No published wwwroot; serving web assets through the static web assets manifest");
            return new StaticWebAssetsFileProvider(manifest);
        }

        // Neither. Say so loudly rather than opening an empty window, which is what this looked like
        // for the whole of 7b until someone tried to run a Debug build.
        LoggingService.Error(nameof(Program),
            $"No web assets: neither {published} nor a static web assets manifest is beside the " +
            "executable, so the window will be blank. Run dotnet publish, or build the project so the " +
            "manifest is written.", new FileNotFoundException(published));

        return new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    /// <summary>
    /// The window and taskbar icon, beside the executable.
    /// </summary>
    /// <remarks>
    /// <para>The <c>ApplicationIcon</c> in the project file puts the icon on the <c>.exe</c>, which is
    /// what Explorer shows and what Windows falls back to — but Photino creates its own native window,
    /// and without an icon set on it the taskbar entry showed the generic default. That is what was
    /// reported: the application looked unbranded next to the MAUI build.</para>
    ///
    /// <para>Per platform because the formats are not interchangeable: Windows wants the multi-size
    /// <c>.ico</c>, GTK wants a PNG. Both are copied beside the executable by the project file.</para>
    ///
    /// <para><b>On a Wayland session the GTK half does nothing, and that is not a defect here.</b>
    /// Photino calls <c>gtk_window_set_icon_from_file</c>, an X11-era call; Wayland has no protocol
    /// for a client to give its own window an icon, and a full <c>WAYLAND_DEBUG=1</c> trace shows the
    /// toplevel receiving no icon request of any kind. GNOME takes the icon from the desktop entry it
    /// matches by <c>app_id</c> instead — for the dock, Alt-Tab and the window list alike — so
    /// <b>the Linux icon is installed by packaging (7b-7, B134), not set here</b>. This call is kept
    /// because an X11 session does honour it.</para>
    ///
    /// <para>A missing file is not a reason to fail startup — Photino would rather have no icon than
    /// no window — so the path is checked and an empty string returned, which Photino ignores.</para>
    /// </remarks>
    private static string ApplicationIcon()
    {
        var file = OperatingSystem.IsWindows() ? "mlqt.ico" : "mlqt-256.png";
        var path = Path.Combine(AppContext.BaseDirectory, file);

        if (File.Exists(path))
            return path;

        LoggingService.Warn(nameof(Program), $"The application icon is missing from {path}");
        return string.Empty;
    }

    /// <summary>
    /// Brings a MAUI user's settings across on first run.
    /// </summary>
    /// <remarks>
    /// <para>Here rather than in the MAUI app, and that is the whole point: shipping this would
    /// otherwise require a MAUI release that users had to run <i>before</i> the Photino one, which is
    /// not a sequence anybody can rely on. The MAUI build is read and never modified, so it is not
    /// part of the upgrade path at all — a user can go straight from any MLQT release to this one.</para>
    ///
    /// <para>Before the window opens, because <c>MainLayout</c> reads the repository list during its
    /// startup sequence and a migration that landed afterwards would be a migration the user has to
    /// restart to see.</para>
    /// </remarks>
    private static void MigrateMauiSettings(ISettingsService settings)
    {
        if (settings is not JsonSettingsService store)
            return;

        var path = MauiPreferencesFile.Locate();
        if (path is null)
            return;

        var copied = store.MigrateFrom(MauiPreferencesFile.Read(path));

        if (copied.Count > 0)
            LoggingService.Info(nameof(Program),
                $"Migrated {copied.Count} setting(s) from the MAUI build at {path}: {string.Join(", ", copied)}");
    }
}

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
        ClaimTaskbarIdentity();

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

        app.MainWindow
           .SetTitle("MLQT")
           .SetIconFile(ApplicationIcon());

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
    /// The identity the Windows taskbar groups this application under.
    /// </summary>
    /// <remarks>
    /// <para>A taskbar button is keyed on an <b>Application User Model ID</b>, and an application
    /// that does not declare one is given whatever the shell derives from the <i>process</i> — which
    /// for a .NET application started through <c>dotnet run</c> is <c>dotnet.exe</c>, and for a path
    /// the shell has cached against is whatever it cached. Either way the button stops following the
    /// window, which is why the icon could be right in the title bar and in Explorer and wrong on the
    /// taskbar: those three come from three different places.</para>
    ///
    /// <para>Declaring one is the documented fix and it is also what makes pinning survive a move or
    /// an upgrade — the pin follows the id, not the folder the executable happened to be in. It has to
    /// be the first thing the process does: the id is read when the first window is created.</para>
    ///
    /// <para>Windows only, and a failure is ignored: the taskbar grouping is not worth refusing to
    /// start over, and every other platform groups by process.</para>
    /// </remarks>
    private static void ClaimTaskbarIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // "Company.Product" — the shape Windows expects, and stable across versions and paths.
            SetCurrentProcessExplicitAppUserModelID("MLQTProject.MLQT");
        }
        catch (Exception ex)
        {
            LoggingService.Warn(nameof(Program), $"Could not set the taskbar application id: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

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

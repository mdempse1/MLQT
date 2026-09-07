using Microsoft.Extensions.Logging;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using MLQT.Shared;
using MudBlazor.Services;
using DymolaInterface;
using OpenModelicaInterface;

namespace MLQT;

/// <summary>
/// Entry point for the MAUI application that configures services and dependency injection.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Everything that is not this host's own business, including the invariant-culture setup.
        builder.Services.AddMlqtCore();

        // The three services that reach the operating system, and the renderer. This is the whole of
        // what a host contributes - the Photino host and the test host add the same three, with
        // their own implementations.
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IPowerManagementService, PowerManagementService>();

        builder.Services.AddMauiBlazorWebView();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

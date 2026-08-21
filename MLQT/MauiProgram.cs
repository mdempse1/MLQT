using System.Globalization;
using Microsoft.Extensions.Logging;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using MLQT.Shared.Services;
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
        // Modelica source and the simulation-tool command protocols are culture-invariant:
        // the decimal separator is always '.', and ',' is never a thousands separator.
        // Default every thread to the invariant culture so number parsing/formatting is
        // never corrupted by the host machine's locale (belt-and-braces; the parser and
        // tool interfaces also specify InvariantCulture explicitly at each call site).
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Add device-specific services used by the MLQT.Shared project
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<AppState>();
        builder.Services.AddSingleton<ILibraryDataService, LibraryDataService>();
        builder.Services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
        builder.Services.AddSingleton<IRepositoryService, RepositoryService>();
        builder.Services.AddSingleton<ICodeReviewService, CodeReviewService>();
        builder.Services.AddSingleton<IBaselineStatusService, BaselineStatusService>();
        builder.Services.AddSingleton<IStyleCheckingService, StyleCheckingService>();
        builder.Services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
        builder.Services.AddSingleton<IDictionaryManagerService, DictionaryManagerService>();
        builder.Services.AddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
        builder.Services.AddSingleton<DymolaInterface.Interfaces.IDymolaInterfaceFactory, DymolaInterfaceFactory>();
        builder.Services.AddSingleton<OpenModelicaInterface.Interfaces.IOpenModelicaInterfaceFactory, OpenModelicaInterfaceFactory>();
        builder.Services.AddSingleton<IExternalResourceService, ExternalResourceService>();
        builder.Services.AddSingleton<IPowerManagementService, PowerManagementService>();
        builder.Services.AddSingleton<DymolaCheckingService>();
        builder.Services.AddSingleton<OpenModelicaCheckingService>();
        builder.Services.AddScoped<BrowserService>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

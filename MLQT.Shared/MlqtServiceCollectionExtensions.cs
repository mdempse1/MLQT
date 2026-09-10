using DymolaInterface;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using MLQT.Shared.Services;
using MudBlazor.Services;
using OpenModelicaInterface;
using System.Globalization;

namespace MLQT.Shared;

/// <summary>
/// The service registrations every host of MLQT's UI needs, whatever that host is.
/// </summary>
/// <remarks>
/// <para>Phase 7a-6. The MAUI <c>MauiProgram</c> registered twenty services inline, which meant the
/// Photino host and the test host would each have had their own copy of that list — and a service
/// quietly missing from one of them is a whole class of migration bug that is invisible until the
/// feature that needs it is used. Extracting it first is what let 7b-8 delete a host without
/// touching a registration.</para>
///
/// <para><b>One list, two hosts.</b> Each adds only what is genuinely its own: the three platform
/// services, and its renderer. <c>MLQT.Photino</c> adds the shipping implementations; the test host
/// adds fakes.</para>
///
/// <para>The plan for this step put the extension in <c>MLQT.Services</c>. It is here instead
/// because two of the registrations — <see cref="AppState"/> and <see cref="BrowserService"/> — are
/// types in <c>MLQT.Shared</c>, which <c>MLQT.Services</c> does not reference and must not. Every
/// host that renders MLQT's UI already references this project, so nothing is lost.</para>
/// </remarks>
public static class MlqtServiceCollectionExtensions
{
    /// <summary>
    /// Registers every MLQT service that is independent of the desktop host, and sets the invariant
    /// culture the whole application assumes.
    /// </summary>
    /// <remarks>
    /// A host still has to add its own <see cref="IFilePickerService"/>,
    /// <see cref="ISettingsService"/> and <see cref="IPowerManagementService"/>: those are the only
    /// three that reach the operating system, and they are the migration's whole non-portable
    /// surface.
    /// </remarks>
    public static IServiceCollection AddMlqtCore(this IServiceCollection services)
    {
        // Modelica source and the simulation-tool command protocols are culture-invariant: the
        // decimal separator is always '.', and ',' is never a thousands separator. Set here rather
        // than in each host so no host can forget it — a host that did would parse a Modelica
        // literal against the machine's locale, and on a German machine "1.5" is not 1.5.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // Logging, for the same reason and after the same defect: this used to be MainLayout's first
        // line, so a host had logging only once that particular component rendered. The /selftest
        // route runs under EmptyLayout by design, so it had none - and the probe that was supposed to
        // notice passed anyway, because the developer's machine already had log files in the folder
        // from ordinary use of the app. A clean Linux runner said otherwise. Initialising here means
        // every host and every route has logging, including one that never renders MainLayout.
        LoggingService.Initialize();

        services.AddSingleton<AppState>();
        services.AddSingleton<ILibraryDataService, LibraryDataService>();
        services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
        services.AddSingleton<IRepositoryService, RepositoryService>();
        services.AddSingleton<IFormattingPipeline, FormattingPipeline>();
        services.AddSingleton<ICodeReviewService, CodeReviewService>();
        services.AddSingleton<IBaselineStatusService, BaselineStatusService>();
        services.AddSingleton<IStyleCheckingService, StyleCheckingService>();
        services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
        services.AddSingleton<IDictionaryManagerService, DictionaryManagerService>();
        services.AddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
        services.AddSingleton<IExternalResourceService, ExternalResourceService>();
        services.AddSingleton<DymolaInterface.Interfaces.IDymolaInterfaceFactory, DymolaInterfaceFactory>();
        services.AddSingleton<OpenModelicaInterface.Interfaces.IOpenModelicaInterfaceFactory, OpenModelicaInterfaceFactory>();
        services.AddSingleton<DymolaCheckingService>();
        services.AddSingleton<OpenModelicaCheckingService>();
        services.AddScoped<BrowserService>();

        services.AddMudServices();

        return services;
    }
}

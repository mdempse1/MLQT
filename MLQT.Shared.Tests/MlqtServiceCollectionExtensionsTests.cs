using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MLQT.Services;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using MLQT.Shared.Services;
using Moq;
using MudBlazor;

namespace MLQT.Shared.Tests;

/// <summary>
/// <see cref="MlqtServiceCollectionExtensions.AddMlqtCore"/> is the one list of services every host
/// registers, so what it promises a host is asserted here rather than discovered by a journey.
/// </summary>
/// <remarks>
/// <para>Before this, only <c>MLQT.TestHost</c> called it, and nothing that host runs is measured: a
/// registration could be dropped, or a constructor could gain a dependency nobody registers, and the
/// first sign would be a feature failing to open in the desktop app.</para>
///
/// <para>The provider is built with <c>ValidateOnBuild</c> and <c>ValidateScopes</c>, which is what
/// makes it a test of the list and not of the lines: every registered service's constructor
/// dependencies must be satisfiable, and no singleton may capture a scoped service.</para>
/// </remarks>
public class MlqtServiceCollectionExtensionsTests
{
    /// <summary>
    /// The services a host gets from <c>AddMlqtCore</c>, and what each resolves to. The list
    /// CLAUDE.md documents as registered there.
    /// </summary>
    public static TheoryData<Type, Type> CoreSingletons => new()
    {
        { typeof(AppState), typeof(AppState) },
        { typeof(ILibraryDataService), typeof(LibraryDataService) },
        { typeof(IFileMonitoringService), typeof(FileMonitoringService) },
        { typeof(IRepositoryService), typeof(RepositoryService) },
        { typeof(IFormattingPipeline), typeof(FormattingPipeline) },
        { typeof(ICodeReviewService), typeof(CodeReviewService) },
        { typeof(IBaselineStatusService), typeof(MLQT.Services.Checking.BaselineStatusService) },
        { typeof(IModelChangeClassifier), typeof(ModelChangeClassifier) },
        { typeof(IStyleCheckingService), typeof(StyleCheckingService) },
        { typeof(ICustomDictionaryService), typeof(CustomDictionaryService) },
        { typeof(IDictionaryManagerService), typeof(DictionaryManagerService) },
        { typeof(IImpactAnalysisService), typeof(ImpactAnalysisService) },
        { typeof(IExternalResourceService), typeof(ExternalResourceService) },
        { typeof(DymolaInterface.Interfaces.IDymolaInterfaceFactory), typeof(DymolaInterface.DymolaInterfaceFactory) },
        { typeof(OpenModelicaInterface.Interfaces.IOpenModelicaInterfaceFactory), typeof(OpenModelicaInterface.OpenModelicaInterfaceFactory) },
        { typeof(DymolaCheckingService), typeof(DymolaCheckingService) },
        { typeof(OpenModelicaCheckingService), typeof(OpenModelicaCheckingService) },
    };

    [Theory]
    [MemberData(nameof(CoreSingletons))]
    public void EveryCoreService_ResolvesToItsImplementation_AsOneInstanceForTheApplication(
        Type serviceType, Type implementationType)
    {
        using var provider = BuildHostProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService(serviceType);
        var b = second.ServiceProvider.GetRequiredService(serviceType);

        Assert.IsType(implementationType, a);

        // A singleton, not per circuit: the libraries, the graph and the analysis state are the
        // application's, and a second instance per scope would be a second, empty, project.
        Assert.Same(a, b);
    }

    [Fact]
    public void BrowserService_IsPerScope_BecauseItHoldsTheCircuitsJsRuntime()
    {
        using var provider = BuildHostProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<BrowserService>();

        Assert.Same(a, first.ServiceProvider.GetRequiredService<BrowserService>());
        Assert.NotSame(a, second.ServiceProvider.GetRequiredService<BrowserService>());
    }

    [Fact]
    public void MudBlazorsServices_AreRegistered()
    {
        using var provider = BuildHostProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDialogService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISnackbar>());
    }

    [Fact]
    public void ThePlatformServices_AreLeftToTheHost()
    {
        // The three that reach the operating system are the whole of what a host adds. Registered
        // here too, one host's registration would silently replace or be replaced by the other.
        var services = new ServiceCollection().AddMlqtCore();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IFilePickerService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ISettingsService));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPowerManagementService));
    }

    [Fact]
    public void AHostWithoutSettings_FailsWhenItStarts_NotWhenTheFeatureIsUsed()
    {
        var services = new ServiceCollection();
        services.AddMlqtCore();
        AddBlazorRuntime(services);
        services.AddSingleton(Mock.Of<IFilePickerService>());
        services.AddSingleton(Mock.Of<IPowerManagementService>());

        // The repository service reads and writes projects through it. Validation is what turns a
        // host that forgot it into a start-up failure naming the service.
        var error = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(Strict));
        Assert.Contains(nameof(ISettingsService), error.ToString());
    }

    [Fact]
    public void TheApplicationCulture_IsInvariant_WhateverTheMachinesLocale()
    {
        var culture = CultureInfo.DefaultThreadCurrentCulture;
        var uiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("de-DE");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("de-DE");

            new ServiceCollection().AddMlqtCore();

            // A Modelica literal is "1.5" on every machine; parsed against de-DE it would be 15.
            Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(CultureInfo.InvariantCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void Logging_IsInitialised_WithoutAnyComponentRendering()
    {
        new ServiceCollection().AddMlqtCore();

        Assert.False(string.IsNullOrEmpty(LoggingService.LogDirectory));
        Assert.True(Directory.Exists(LoggingService.LogDirectory));
    }

    private static readonly ServiceProviderOptions Strict = new() { ValidateOnBuild = true, ValidateScopes = true };

    /// <summary>
    /// What a host builds: the core list, its three platform services, and the runtime Blazor supplies.
    /// </summary>
    private static ServiceProvider BuildHostProvider()
    {
        var services = new ServiceCollection();
        services.AddMlqtCore();
        AddBlazorRuntime(services);
        AddPlatformServices(services);
        return services.BuildServiceProvider(Strict);
    }

    private static void AddPlatformServices(IServiceCollection services)
    {
        services.AddSingleton(Mock.Of<IFilePickerService>());
        services.AddSingleton(Mock.Of<ISettingsService>());
        services.AddSingleton(Mock.Of<IPowerManagementService>());
    }

    /// <summary>
    /// The services a Blazor host registers for itself (the JS runtime, navigation, logging), which MudBlazor's and
    /// <see cref="BrowserService"/> depend on.
    /// </summary>
    private static void AddBlazorRuntime(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<IJSRuntime>());
        services.AddScoped<NavigationManager, StubNavigationManager>();
        services.AddLogging();
    }

    private sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager() => Initialize("http://localhost/", "http://localhost/");
    }
}

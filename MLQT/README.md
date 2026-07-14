# MLQT

The .NET MAUI application host for the MLQT Modelica library management tool. This project bootstraps the Blazor UI (from MLQT.Shared) within a native application using BlazorWebView.

## Overview

MLQT is the executable entry point that:

- Configures dependency injection for all services
- Provides platform-specific implementations (file picker, settings persistence)
- Hosts the Blazor UI within a MAUI BlazorWebView
- Targets Windows via .NET 10

## Key Concepts

### Platform-Specific Services

Services that require native platform APIs are implemented here:

| Service | Interface | Purpose |
|---------|-----------|---------|
| `FilePickerService` | `IFilePickerService` | Native file/folder selection dialogs via MAUI APIs |
| `SettingsService` | `ISettingsService` | Persistent settings via MAUI Preferences API |
| `PowerManagementService` | `IPowerManagementService` | Prevents the system from sleeping during long operations |

### Service Registration (MauiProgram.cs)

All application services are registered in `MauiProgram.CreateMauiApp()`:

```csharp
// Platform services
services.AddSingleton<IFilePickerService, FilePickerService>();
services.AddSingleton<ISettingsService, SettingsService>();
services.AddSingleton<IPowerManagementService, PowerManagementService>();

// Application state
services.AddSingleton<AppState>();

// Core services
services.AddSingleton<ILibraryDataService, LibraryDataService>();
services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
services.AddSingleton<IRepositoryService, RepositoryService>();
services.AddSingleton<ICodeReviewService, CodeReviewService>();
services.AddSingleton<IStyleCheckingService, StyleCheckingService>();
services.AddSingleton<IImpactAnalysisService, ImpactAnalysisService>();
services.AddSingleton<IExternalResourceService, ExternalResourceService>();
services.AddSingleton<ICustomDictionaryService, CustomDictionaryService>();
services.AddSingleton<IDictionaryManagerService, DictionaryManagerService>();

// Simulation tool factories
services.AddSingleton<IDymolaInterfaceFactory, DymolaInterfaceFactory>();
services.AddSingleton<IOpenModelicaInterfaceFactory, OpenModelicaInterfaceFactory>();

// Simulation checking services
services.AddSingleton<DymolaCheckingService>();
services.AddSingleton<OpenModelicaCheckingService>();

// Scoped services
services.AddScoped<BrowserService>();
```

### Application Structure

```
MLQT/
├── App.xaml              # MAUI application definition
├── MainPage.xaml         # BlazorWebView host page
├── MauiProgram.cs        # DI configuration and app builder
├── Services/
│   ├── FilePickerService.cs        # MAUI file picker implementation
│   ├── SettingsService.cs          # MAUI preferences implementation
│   └── PowerManagementService.cs   # Prevents system sleep during long operations
└── wwwroot/              # Static web assets
```

## Building and Running

```bash
# Build for Windows
dotnet build MLQT/MLQT.csproj

# Run
dotnet run --project MLQT/MLQT.csproj
```

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Dependencies

**NuGet Packages:**
- Microsoft.Maui.Controls
- Microsoft.AspNetCore.Components.WebView.Maui
- Microsoft.Extensions.Logging.Debug (9.0.5)

**Project References:**
- DymolaInterface
- MLQT.Shared
- MLQT.Services
- OpenModelicaInterface

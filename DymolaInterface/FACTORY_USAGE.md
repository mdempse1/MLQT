# Using DymolaInterfaceFactory

The `DymolaInterfaceFactory` provides a way to create and manage `DymolaInterface` instances with configuration loaded from the application's settings service.

## Overview

Instead of directly instantiating `DymolaInterface` with constructor parameters, the factory pattern allows you to:
- Load configuration from `SettingsService` (DymolaPath, HostAddress, PortNumber)
- Manage a singleton instance throughout the application lifecycle
- Reset and recreate the instance when settings change

## Service Registration

The factory is registered in both `MauiProgram.cs` and `Program.cs`:

```csharp
builder.Services.AddSingleton<IDymolaInterfaceFactory, DymolaInterfaceFactory>();
```

## Usage in Components

Inject `IDymolaInterfaceFactory` into your Blazor components or services:

```csharp
@inject IDymolaInterfaceFactory DymolaFactory

@code {
    private async Task SimulateModel()
    {
        // Get or create the DymolaInterface instance (uses settings from SettingsService)
        var dymola = await DymolaFactory.GetOrCreateAsync();

        // Use the instance
        var result = await dymola.SimulateModelAsync(
            "Modelica.Mechanics.Rotational.Examples.CoupledClutches"
        );

        if (result)
        {
            await dymola.PlotAsync(new[] { "J1.w", "J2.w" });
        }
    }

    private async Task OnSettingsChanged()
    {
        // Reset the instance to force recreation with new settings
        await DymolaFactory.ResetAsync();
    }

    private void CheckConnection()
    {
        // Check if instance exists and is connected
        bool connected = DymolaFactory.IsConnected;
    }
}
```

## Settings

The factory reads configuration from the `DymolaSettings` class stored in `SettingsService`:

```csharp
public class DymolaSettings
{
    public string DymolaPath { get; set; } = string.Empty;
    public string HostAddress { get; set; } = "127.0.0.1";
    public int PortNumber { get; set; } = 8082;
    public string CommandLineArguments { get; set; } = string.Empty;
    public string CheckModelCommand { get; set; } = "checkModel(\"<MODELNAME>\");";
}
```

These settings are persisted using:
- **MAUI**: Platform-specific Preferences API
- **Web**: Browser localStorage

## Factory Methods

### GetOrCreateAsync()
```csharp
Task<DymolaInterface> GetOrCreateAsync()
```
Gets the existing singleton instance, or creates a new one if it doesn't exist. The instance is configured using settings from `SettingsService`.

### IsConnected
```csharp
bool IsConnected { get; }
```
Returns `true` if an instance exists and is not in offline mode.

### ResetAsync()
```csharp
Task ResetAsync()
```
Disposes the current instance if it exists. Call this when settings change to force the factory to create a new instance with updated settings on the next `GetOrCreateAsync()` call.

## Thread Safety

The factory uses `SemaphoreSlim` to ensure thread-safe singleton creation and disposal. Multiple concurrent calls to `GetOrCreateAsync()` will wait for the first call to complete and then receive the same instance.

## Example: Settings Update Flow

```csharp
@inject IDymolaInterfaceFactory DymolaFactory
@inject ISettingsService SettingsService

@code {
    private async Task SaveDymolaSettings(DymolaSettings settings)
    {
        // Save updated settings
        await SettingsService.SetAsync("Dymola", settings);

        // Reset the factory to pick up new settings
        await DymolaFactory.ResetAsync();

        // Next call to GetOrCreateAsync() will use the new settings
        var dymola = await DymolaFactory.GetOrCreateAsync();
    }
}
```

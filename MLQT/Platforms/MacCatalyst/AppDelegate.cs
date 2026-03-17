using Foundation;

namespace MLQT;

/// <summary>
/// Mac Catalyst application delegate that creates the MAUI app instance.
/// </summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

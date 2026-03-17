namespace MLQT;

/// <summary>
/// Main application class for the Modelica Editor MAUI application.
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Get display size
        var displayInfo = DeviceDisplay.Current.MainDisplayInfo;

        var win = new Window(new MainPage())
        {
            Title = "MLQT",
            Width = Math.Min(1200, displayInfo.Width / displayInfo.Density),
            Height = Math.Min(900, displayInfo.Height / displayInfo.Density)
        };

        // Center the window
        win.X = (displayInfo.Width / displayInfo.Density - win.Width) / 2;
        win.Y = (displayInfo.Height / displayInfo.Density - win.Height) / 2;

        return win;
    }
}

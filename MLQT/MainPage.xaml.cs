namespace MLQT;

/// <summary>
/// Main content page hosting the Blazor WebView component.
/// </summary>
public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Started to capture a conformance baseline rather than to be used: go straight to the
        // self-test route, which writes its report and exits. See SelfTestLauncher for why this is
        // a start path rather than UI automation.
        if (SelfTestLauncher.IsEnabled)
        {
            SelfTestLauncher.DeclareHost();
            blazorWebView.StartPath = "/selftest";
        }
    }
}

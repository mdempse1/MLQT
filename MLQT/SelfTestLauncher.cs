using MLQT.Shared.Pages;

namespace MLQT;

/// <summary>
/// Runs the <c>/selftest</c> route under the real MAUI app and writes its report to a file.
/// </summary>
/// <remarks>
/// <para>Phase 7a-7, and the piece with a deadline: the MAUI conformance baseline can only be
/// captured while MAUI is still the reference implementation, and cannot be produced afterwards.
/// Photino conformance is then a diff against a committed file rather than a judgement call about
/// whether something looks right.</para>
///
/// <para>No UI automation framework and no CDP. Driving WebView2 through the Chrome DevTools
/// Protocol works on Windows today and does not port — WebKitGTK speaks the WebKit remote inspector
/// protocol instead — so a baseline captured that way would have to be discarded at exactly the
/// moment it was needed. This is the app navigating to its own route and the page writing the file.</para>
/// </remarks>
public static class SelfTestLauncher
{
    /// <summary>Set to 1 to start the app on <c>/selftest</c> instead of the application shell.</summary>
    public const string EnabledVariable = "MLQT_SELFTEST";

    /// <summary>
    /// Where to write the report. The page owns this name, because every host writes its baseline
    /// through the same code.
    /// </summary>
    public const string OutputVariable = SelfTest.OutputPathVariable;

    /// <summary>Whether this process was started to run the self-test.</summary>
    public static bool IsEnabled =>
        Environment.GetEnvironmentVariable(EnabledVariable) is "1" or "true";

    /// <summary>The file to write, or null when none was named.</summary>
    public static string? OutputPath => Environment.GetEnvironmentVariable(OutputVariable);

    /// <summary>
    /// Names this host in the report, so a baseline says which host produced it whatever launched
    /// the process.
    /// </summary>
    public static void DeclareHost() =>
        Environment.SetEnvironmentVariable(SelfTest.HostNameVariable, "MLQT");
}

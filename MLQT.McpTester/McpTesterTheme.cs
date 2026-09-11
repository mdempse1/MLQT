using MudBlazor;

namespace MLQT.McpTester;

/// <summary>
/// The tester's MudBlazor theme, which exists for one reason: its font.
/// </summary>
/// <remarks>
/// <para>Backlog B124. The host page used to pull Roboto from <c>fonts.googleapis.com</c>, so a tool
/// whose whole job is launching local MCP servers rendered differently with no network — and phase
/// 7b-4 removed exactly that dependency everywhere else. It could not be fixed the same way here:
/// MLQT bundles Roboto in <c>MLQT.Shared/wwwroot/fonts</c>, and this project deliberately has no
/// reference to <c>MLQT.Shared</c>.</para>
///
/// <para>So it asks for the platform's own UI font instead, rather than committing a second 231 KB
/// copy of a webfont for a diagnostic tool nobody ships. Roboto stays in the stack because it is
/// installed on most Linux desktops; it is simply never downloaded.</para>
///
/// <para><b>Only <c>Default</c> needs setting.</b> Measured against MudBlazor 9.6: every other
/// variant's <c>FontFamily</c> is null out of the box and falls back to this one, so setting the
/// fourteen of them would be fourteen chances to miss one.</para>
/// </remarks>
internal static class McpTesterTheme
{
    /// <summary>The platform UI font, in the order the platforms are likely to be met.</summary>
    internal static readonly string[] SystemFontStack =
    [
        "-apple-system", "BlinkMacSystemFont", "Segoe UI", "Roboto", "Helvetica Neue",
        "Noto Sans", "Liberation Sans", "Arial", "sans-serif",
    ];

    internal static readonly MudTheme Instance = new()
    {
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = SystemFontStack },
        },
    };
}

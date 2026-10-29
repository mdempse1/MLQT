namespace MLQT.UI.Theme;

/// <summary>
/// The stylesheets and scripts a host page must load for the theme, and where in the page they go.
/// </summary>
/// <remarks>
/// <para><b>The theme surrounds the application's own assets rather than following them</b>, which
/// is why there are leading and trailing lists rather than one. A host page is:</para>
/// <code>
/// LeadingStylesheets, the tool's own stylesheets, TrailingStylesheets
/// the Blazor bootstrap, LeadingScripts, the tool's own scripts, TrailingScripts
/// </code>
/// <para><c>MLQT.Photino</c>'s page is written this way through <c>HostAssetManifest</c>, and
/// <c>MLQT.McpTester</c>'s from this list alone; <c>HostAssetManifestTests</c> holds both.</para>
/// <para><c>mlqt-base.css</c> comes before MudBlazor's stylesheet, where MLQT's <c>app.css</c> always
/// was: its rules either carry <c>!important</c> or win on specificity, and moving it after
/// MudBlazor would let rules that lose today start to apply. <c>mlqt-density.css</c> comes
/// <b>after</b> MudBlazor's, because it is a set of overrides of MudBlazor's own rules at the same
/// specificity, and wins only by coming later. It was an inline <c>&lt;style&gt;</c> in MLQT's
/// <c>MainLayout</c> until the theme became a package, which is how it always came last.</para>
///
/// <para><c>mudBlazorWorkarounds.js</c> edits MudBlazor's stylesheet once it has loaded, so it goes
/// after every stylesheet - which a script does by waiting for the sheets above it.</para>
///
/// <para>Every entry is a local path. A desktop tool must start with no network, and a font that
/// fails to download is not an error anywhere: the page simply renders in the wrong typeface.</para>
/// </remarks>
public static class ThemeAssetManifest
{
    /// <summary>The path every asset of this package is served under.</summary>
    public const string ContentRoot = "_content/MLQT.UI.Theme/";

    /// <summary>MudBlazor's own stylesheet, which the theme's two stylesheets sit either side of.</summary>
    public const string MudBlazorStylesheet = "_content/MudBlazor/MudBlazor.min.css";

    /// <summary>MudBlazor's script, which every MudBlazor page needs before anything that uses it.</summary>
    public const string MudBlazorScript = "_content/MudBlazor/MudBlazor.min.js";

    /// <summary>The stylesheets that come first in the page, before the tool's own.</summary>
    public static IReadOnlyList<string> LeadingStylesheets { get; } =
    [
        ContentRoot + "mlqt-base.css",
    ];

    /// <summary>The stylesheets that come after the tool's own, in order: the font, MudBlazor, and
    /// the density overrides that have to follow MudBlazor.</summary>
    public static IReadOnlyList<string> TrailingStylesheets { get; } =
    [
        ContentRoot + "fonts/roboto.css",
        MudBlazorStylesheet,
        ContentRoot + "mlqt-density.css",
    ];

    /// <summary>The scripts that come before the tool's own.</summary>
    public static IReadOnlyList<string> LeadingScripts { get; } =
    [
        MudBlazorScript,
    ];

    /// <summary>The scripts that come after the tool's own.</summary>
    public static IReadOnlyList<string> TrailingScripts { get; } =
    [
        ContentRoot + "mudBlazorWorkarounds.js",
    ];
}

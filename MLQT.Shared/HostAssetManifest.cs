namespace MLQT.Shared;

/// <summary>
/// The stylesheets and scripts every host page must load, in the order it must load them.
/// </summary>
/// <remarks>
/// <para>Phase 7a-6. Each host has its own <c>index.html</c> — they differ only in the Blazor
/// bootstrap script, <c>blazor.webview.js</c> for a webview host and <c>blazor.server.js</c> for the
/// test host — and everything else in them has to be identical. Kept as three hand-maintained
/// copies, that would drift silently, and the failure would not look like a missing script: it
/// would look like the Dependencies page rendering an empty box.</para>
///
/// <para><b>Order is load-bearing.</b> Cytoscape's extensions register against the global
/// <c>cytoscape</c>, so every <c>cytoscape-*</c> file has to follow <c>cytoscape.min.js</c>;
/// <c>cose-base</c> is built on <c>layout-base</c> and <c>cytoscape-fcose</c> on both. A host page
/// with the right scripts in the wrong order throws at load, in a script the user never sees.</para>
///
/// <para>MLQT's own interop files come last, because they call into those libraries.</para>
/// </remarks>
public static class HostAssetManifest
{
    /// <summary>
    /// The stylesheets, in order. <c>MLQT.styles.css</c> is the host's own scoped-CSS bundle and is
    /// named after the host assembly, so it is not here — each host adds its own.
    /// </summary>
    public static IReadOnlyList<string> Stylesheets { get; } =
    [
        "_content/MLQT.Shared/app.css",
        "app.css",
        "https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap",
        "_content/MudBlazor/MudBlazor.min.css",
    ];

    /// <summary>
    /// The scripts, in order, excluding the Blazor bootstrap — that is the one thing hosts differ
    /// on, and naming it here would make the list impossible for any of them to satisfy.
    /// </summary>
    public static IReadOnlyList<string> Scripts { get; } =
    [
        "_content/MudBlazor/MudBlazor.min.js",

        // Cytoscape and its layout extensions. Every line below cytoscape.min.js registers against
        // the global it defines, and the last three build on the two before them.
        "_content/MLQT.Shared/lib/cytoscape.min.js",
        "_content/MLQT.Shared/lib/dagre.min.js",
        "_content/MLQT.Shared/lib/cytoscape-dagre.js",
        "_content/MLQT.Shared/lib/klayjs.js",
        "_content/MLQT.Shared/lib/cytoscape-klay.js",
        "_content/MLQT.Shared/lib/weaver.min.js",
        "_content/MLQT.Shared/lib/cytoscape-spread.js",
        "_content/MLQT.Shared/lib/layout-base.js",
        "_content/MLQT.Shared/lib/cose-base.js",
        "_content/MLQT.Shared/lib/cytoscape-fcose.js",

        // MLQT's own interop, last: it calls into the libraries above.
        "_content/MLQT.Shared/cytoscapeGraph.js",
        "_content/MLQT.Shared/diffViewer.js",
        "_content/MLQT.Shared/spellCheck.js",
    ];

    /// <summary>
    /// The Blazor bootstrap script for a webview host (MAUI today, Photino next). The test host uses
    /// <c>_framework/blazor.server.js</c> instead, which is the only difference between the pages.
    /// </summary>
    public const string WebViewBootstrapScript = "_framework/blazor.webview.js";

    /// <summary>The Blazor bootstrap script for a server-rendered host.</summary>
    public const string ServerBootstrapScript = "_framework/blazor.server.js";
}

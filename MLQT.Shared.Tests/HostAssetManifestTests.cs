using System.Text.RegularExpressions;
using MLQT.Shared;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every host page loads the same assets in the same order, checked by reading the pages.
///
/// <para>The reason this is a test and not a convention: the pages differ only in the Blazor
/// bootstrap script, they are maintained by hand, and drift between them does not look like a
/// missing script. It looks like the Dependencies page rendering an empty box on one host and not
/// the other — at which point the question is whether Cytoscape is broken under that engine, which
/// is the phase 7b question this manifest exists to keep separate from a typo.</para>
/// </summary>
public class HostAssetManifestTests
{
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.Shared", "_Imports.razor")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    /// <summary>Every host page in the repository, by the assembly that owns it.</summary>
    public static TheoryData<string, string> HostPages()
    {
        var data = new TheoryData<string, string>();
        foreach (var host in new[] { "MLQT", "MLQT.Photino" })
            data.Add(host, Path.Combine(RepositoryRoot(), host, "wwwroot", "index.html"));
        return data;
    }

    private static List<string> ScriptsIn(string html) =>
        Regex.Matches(html, @"<script[^>]*\bsrc=""([^""]+)""")
             .Select(m => m.Groups[1].Value)
             .ToList();

    private static List<string> StylesheetsIn(string html) =>
        Regex.Matches(html, @"<link[^>]*\bhref=""([^""]+)""")
             .Select(m => m.Groups[1].Value)
             .Where(h => !h.StartsWith("data:"))          // the inline empty favicon
             .Where(h => !h.EndsWith(".styles.css"))      // each host's own scoped-CSS bundle
             .ToList();

    [Theory]
    [MemberData(nameof(HostPages))]
    public void AHostPageExists(string host, string path)
    {
        Assert.True(File.Exists(path), $"{host} has no wwwroot/index.html at {path}");
    }

    [Theory]
    [MemberData(nameof(HostPages))]
    public void ItLoadsExactlyTheManifestsScripts_InOrder(string host, string path)
    {
        Assert.True(File.Exists(path), $"{host} has no host page to check");

        var scripts = ScriptsIn(File.ReadAllText(path))
            .Where(s => s != HostAssetManifest.WebViewBootstrapScript
                     && s != HostAssetManifest.ServerBootstrapScript)
            .ToList();

        Assert.Equal(HostAssetManifest.Scripts, scripts);
    }

    [Theory]
    [MemberData(nameof(HostPages))]
    public void ItLoadsExactlyTheManifestsStylesheets_InOrder(string host, string path)
    {
        Assert.True(File.Exists(path), $"{host} has no host page to check");

        Assert.Equal(HostAssetManifest.Stylesheets, StylesheetsIn(File.ReadAllText(path)));
    }

    [Theory]
    [MemberData(nameof(HostPages))]
    public void ItLoadsOneBlazorBootstrapScript(string host, string path)
    {
        // Exactly one, and one of the two the manifest names. A page with neither does not start; a
        // page with both starts twice.
        var bootstraps = ScriptsIn(File.ReadAllText(path))
            .Where(s => s == HostAssetManifest.WebViewBootstrapScript
                     || s == HostAssetManifest.ServerBootstrapScript)
            .ToList();

        Assert.True(bootstraps.Count == 1,
            $"{host} loads {bootstraps.Count} Blazor bootstrap scripts; it needs exactly one");
    }

    [Fact]
    public void CytoscapeExtensionsComeAfterCytoscape()
    {
        // Each extension registers against the global cytoscape defines. Loaded first, it throws at
        // page load, in a script no user ever looks at.
        var scripts = HostAssetManifest.Scripts;
        var core = scripts.ToList().FindIndex(s => s.EndsWith("cytoscape.min.js"));

        Assert.True(core >= 0, "the manifest has to include cytoscape itself");
        foreach (var extension in scripts.Where(s => s.Contains("cytoscape-")))
            Assert.True(scripts.ToList().IndexOf(extension) > core, $"{extension} must follow cytoscape.min.js");
    }

    [Fact]
    public void TheLayoutBaseChainIsInOrder()
    {
        // cose-base is built on layout-base, and cytoscape-fcose on both.
        var scripts = HostAssetManifest.Scripts.ToList();
        var layoutBase = scripts.FindIndex(s => s.EndsWith("layout-base.js"));
        var coseBase = scripts.FindIndex(s => s.EndsWith("cose-base.js"));
        var fcose = scripts.FindIndex(s => s.EndsWith("cytoscape-fcose.js"));

        Assert.True(layoutBase >= 0 && coseBase >= 0 && fcose >= 0);
        Assert.True(coseBase > layoutBase, "cose-base builds on layout-base");
        Assert.True(fcose > coseBase, "cytoscape-fcose builds on cose-base");
    }

    [Fact]
    public void MlqtsOwnInteropComesLast()
    {
        // It calls into the libraries above it.
        var scripts = HostAssetManifest.Scripts.ToList();
        var lastLibrary = scripts.FindLastIndex(s => s.Contains("/lib/"));

        foreach (var own in new[] { "cytoscapeGraph.js", "diffViewer.js", "spellCheck.js" })
        {
            var index = scripts.FindIndex(s => s.EndsWith(own));
            Assert.True(index > lastLibrary, $"{own} must load after every library");
        }
    }

    [Fact]
    public void EveryLocalAssetTheManifestNamesExists()
    {
        // A path that resolves to nothing fails silently in a browser: the script does not load and
        // whatever needed it is simply absent.
        var shared = Path.Combine(RepositoryRoot(), "MLQT.Shared", "wwwroot");

        foreach (var asset in HostAssetManifest.Scripts.Concat(HostAssetManifest.Stylesheets))
        {
            const string prefix = "_content/MLQT.Shared/";
            if (!asset.StartsWith(prefix))
                continue;   // another package's, or the host's own, or a URL

            var path = Path.Combine(shared, asset[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{asset} is in the manifest but not in MLQT.Shared/wwwroot");
        }
    }
}

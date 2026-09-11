using System.Text.RegularExpressions;
using MLQT.Shared;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every host page loads the assets the manifest names, in the order it names them, checked by
/// reading the pages.
///
/// <para>The reason this is a test and not a convention: a host page is maintained by hand, and a
/// script missing from it does not look like a missing script. It looks like the Dependencies page
/// rendering an empty box — at which point the question is whether Cytoscape is broken under that
/// engine, which is the phase 7b question this manifest exists to keep separate from a typo.</para>
///
/// <para>It was written for two hosts, to catch drift between them; 7b-8 left one. The manifest is
/// still the thing the page is held to, which is the half that mattered — an asset added to
/// <c>HostAssetManifest</c> and not to the page is the same defect with one host as with two, and
/// macOS or a second window host would rejoin the list rather than need a new test.</para>
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
        foreach (var host in new[] { "MLQT.Photino" })
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
    public void NoAssetIsFetchedFromTheNetwork()
    {
        // Phase 7b-4, and the reason the font is bundled. Roboto came from fonts.googleapis.com, so
        // starting a *desktop* application made a network round-trip - slow on a good connection,
        // and on an offline or locked-down machine a failure with no error: the UI simply renders in
        // whatever the fallback font is, which looks like a styling bug rather than a missing asset.
        // Stated over both lists because the next one to arrive is as likely to be a script.
        foreach (var asset in HostAssetManifest.Scripts.Concat(HostAssetManifest.Stylesheets))
            Assert.False(asset.Contains("//", StringComparison.Ordinal),
                $"{asset} is fetched over the network; a desktop host must start without one");
    }

    [Fact]
    public void NoHostPageInTheRepositoryFetchesAnythingFromTheNetwork()
    {
        // The same promise as above, asked of the *pages* rather than the manifest - because the
        // manifest only governs the hosts built from it, and B124 was the one that is not:
        // MLQT.McpTester keeps a hand-written page and has no reference to MLQT.Shared, so it went on
        // linking fonts.googleapis.com for a phase after 7b-4 removed that everywhere else. A
        // diagnostic tool for launching local MCP servers is exactly the thing somebody runs on a
        // machine with no network.
        //
        // Every wwwroot/index.html in the repository, found rather than listed, so the next host
        // inherits the rule instead of needing to be remembered.
        var pages = Directory
            .EnumerateFiles(RepositoryRoot(), "index.html", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + "wwwroot" + Path.DirectorySeparatorChar))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();

        Assert.True(pages.Count >= 2,
            $"found {pages.Count} host pages; expected at least MLQT.Photino and MLQT.McpTester");

        // link/script/img only: an <a href> is something the user clicks, not something the page
        // fetches while loading, and the About dialog's external links are deliberate.
        var assets = pages
            .SelectMany(page => Regex
                .Matches(File.ReadAllText(page),
                         @"<(?:link|script|img)[^>]*?(?:href|src)=""(?<url>[^""]+)""",
                         RegexOptions.IgnoreCase)
                .Select(m => (Host: Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(page))!),
                              Url: m.Groups["url"].Value)))
            .ToList();

        // **The scan found something.** Without this the assertion below passes over an empty list,
        // which is the failure shape this repository keeps finding - and it found it here: the first
        // version of this test carried an invisible control character in its pattern, matched nothing,
        // and passed against a page that did link fonts.googleapis.com.
        Assert.True(assets.Count >= 10,
            $"only found {assets.Count} assets across {pages.Count} host pages; the scan is not reading them");

        var offenders = assets
            .Where(a => a.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || a.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                        || a.Url.StartsWith("//", StringComparison.Ordinal))
            .Select(a => $"{a.Host}: {a.Url}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "These are fetched over the network, so the application renders differently offline with "
            + "no error anywhere: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryFontFileTheBundledStylesheetNamesExists()
    {
        // The manifest names roboto.css and EveryLocalAssetTheManifestNamesExists checks that file is
        // there - but the nine .woff2 files are named inside it, one level below anything the manifest
        // can see. A missing one fails the way the network link did: silently, in a fallback typeface.
        var fonts = Path.Combine(RepositoryRoot(), "MLQT.Shared", "wwwroot", "fonts");
        var css = File.ReadAllText(Path.Combine(fonts, "roboto.css"));

        // The urls are unquoted because the file is generated from the Google Fonts CSS, which writes
        // them that way; a hand-edited quote would fail File.Exists and say so.
        var urls = Regex.Matches(css, @"url\(([^)]+)\)").Select(m => m.Groups[1].Value.Trim()).ToList();

        Assert.Equal(9, urls.Count);   // one variable face per subset; four static weights each would be 36
        foreach (var url in urls)
            Assert.True(File.Exists(Path.Combine(fonts, url)), $"{url} is in roboto.css but not in wwwroot/fonts");
    }

    [Fact]
    public void TheBundledFontCoversEveryWeightMudBlazorAsks()
    {
        // The link it replaced requested 300, 400, 500 and 700. A variable face declaring 100 900
        // covers all four and anything MudBlazor adds later; a bundle of static weights would not,
        // and the symptom - one component in a slightly wrong weight - is not one anybody reports.
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "MLQT.Shared", "wwwroot", "fonts", "roboto.css"));

        Assert.Equal(9, Regex.Matches(css, @"font-weight:\s*100 900").Count);
        Assert.Contains("font-family: 'Roboto'", css);
    }

    [Fact]
    public void TheBundledFontCarriesItsLicence()
    {
        // Redistributing a font means shipping its licence. Roboto moved from Apache 2.0 to the SIL
        // Open Font License with Roboto 3, which is the version Google Fonts serves - so this is the
        // licence that has to be here, not the one Roboto is remembered as having.
        var licence = Path.Combine(RepositoryRoot(), "MLQT.Shared", "wwwroot", "fonts", "OFL.txt");

        Assert.True(File.Exists(licence), "the bundled font has no licence file");
        Assert.Contains("SIL Open Font License", File.ReadAllText(licence));
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

using System.Text.Json;
using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Finding the web assets of an application that was built rather than published.
/// </summary>
/// <remarks>
/// <para>Phase 7b-5. <c>dotnet publish</c> writes a real <c>wwwroot</c>; <c>dotnet build</c> writes a
/// manifest pointing at the originals, scattered across the source tree, other projects and the NuGet
/// cache. Only the first was handled, so <c>MLQT.Photino</c> ran <b>only from a publish</b> — F5 and
/// <c>dotnet run</c> opened a window that loaded nothing at all, with no error, because a webview
/// showing nothing looks exactly like one that is still starting.</para>
///
/// <para>Against a manifest written to disk in the SDK's own shape rather than a fake: the format is
/// the thing being relied on, and its two mechanisms — explicit assets and directory patterns —
/// interact in a way that is easy to get backwards.</para>
/// </remarks>
public class StaticWebAssetManifestTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "mlqt-swa-" + Guid.NewGuid().ToString("N"));

    public StaticWebAssetManifestTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ---- building a manifest -------------------------------------------------------------------

    /// <summary>Creates a file under a content root and returns the path the manifest should give.</summary>
    private string Given(string contentRoot, string relative)
    {
        var path = Path.Combine(_root, contentRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static Dictionary<string, object> Node(
        (int Root, string SubPath)? asset = null,
        int? pattern = null,
        Dictionary<string, object>? children = null)
    {
        var node = new Dictionary<string, object>();

        if (asset is { } a)
            node["Asset"] = new Dictionary<string, object> { ["ContentRootIndex"] = a.Root, ["SubPath"] = a.SubPath };

        if (pattern is { } p)
            node["Patterns"] = new[]
            {
                new Dictionary<string, object> { ["ContentRootIndex"] = p, ["Pattern"] = "**", ["Depth"] = 0 }
            };

        if (children is not null)
            node["Children"] = children;

        return node;
    }

    private static Dictionary<string, object> Children(params (string Name, Dictionary<string, object> Node)[] entries) =>
        entries.ToDictionary(e => e.Name, e => (object)e.Node);

    /// <summary>Writes a manifest in the SDK's shape and loads it.</summary>
    private StaticWebAssetManifest Manifest(Dictionary<string, object> root, params string[] contentRoots)
    {
        var manifest = new Dictionary<string, object>
        {
            ["ContentRoots"] = contentRoots.Select(r => Path.Combine(_root, r) + Path.DirectorySeparatorChar).ToArray(),
            ["Root"] = root
        };

        var path = Path.Combine(_root, "app.staticwebassets.runtime.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest));

        return StaticWebAssetManifest.Load(path)
               ?? throw new InvalidOperationException("the manifest did not load");
    }

    // ---- explicit assets -----------------------------------------------------------------------

    [Fact]
    public void AnAssetResolvesToTheFileItNames()
    {
        // The case that matters most: blazor.webview.js lives in a NuGet package, nowhere near the
        // application, and is served from a path that exists in no folder on disk.
        var expected = Given("packages", "blazor.webview.js");

        var manifest = Manifest(
            Node(children: Children(("_framework",
                Node(children: Children(("blazor.webview.js", Node(asset: (0, "blazor.webview.js")))))))),
            "packages");

        Assert.Equal(expected, manifest.Resolve("_framework/blazor.webview.js"));
    }

    [Fact]
    public void AnAssetPointingAtNothingResolvesToNothing()
    {
        // A manifest outlives the files it names - a package dropped from the cache, a project
        // cleaned. Handing back a path to a file that is not there turns a miss into a webview error.
        var manifest = Manifest(
            Node(children: Children(("gone.js", Node(asset: (0, "gone.js"))))), "packages");

        Assert.Null(manifest.Resolve("gone.js"));
    }

    [Fact]
    public void ARequestThatGoesPastAnAssetIsNotThatAsset()
    {
        // Stopping at the deepest node that matched is not the same as matching the whole path. A
        // request for something *under* a file is not that file, and answering with it would serve
        // the wrong bytes rather than a miss.
        Given("packages", "thing.js");

        var manifest = Manifest(
            Node(children: Children(("thing.js", Node(asset: (0, "thing.js"))))), "packages");

        Assert.Null(manifest.Resolve("thing.js/and/more"));
    }

    // ---- patterns ------------------------------------------------------------------------------

    [Fact]
    public void APatternServesAnythingBeneathIt()
    {
        // The project's own wwwroot is a pattern at the root of the tree, so index.html - the first
        // thing the host asks for - is found this way and not as an asset.
        var expected = Given("wwwroot", "index.html");

        var manifest = Manifest(Node(pattern: 0), "wwwroot");

        Assert.Equal(expected, manifest.Resolve("index.html"));
    }

    [Fact]
    public void APatternServesNestedPathsToo()
    {
        var expected = Given("wwwroot", "lib/cytoscape.min.js");

        var manifest = Manifest(Node(pattern: 0), "wwwroot");

        Assert.Equal(expected, manifest.Resolve("lib/cytoscape.min.js"));
    }

    [Fact]
    public void ADeeperPatternServesFromWhereItStarts()
    {
        // _content/MLQT.Shared is a pattern two levels down, and what is handed to the content root
        // is what remains after those two segments - not the whole request.
        var expected = Given("shared", "fonts/roboto.css");

        var manifest = Manifest(
            Node(children: Children(("_content",
                Node(children: Children(("MLQT.Shared", Node(pattern: 0))))))),
            "shared");

        Assert.Equal(expected, manifest.Resolve("_content/MLQT.Shared/fonts/roboto.css"));
    }

    [Fact]
    public void APatternDoesNotInventFilesThatAreNotThere()
    {
        var manifest = Manifest(Node(pattern: 0), "wwwroot");

        Assert.Null(manifest.Resolve("nothing-here.txt"));
    }

    // ---- the two together ----------------------------------------------------------------------

    [Fact]
    public void AnAssetWinsOverAPatternThatWouldAlsoMatch()
    {
        // The interaction that is easy to get backwards, and expensive when it is. The project's own
        // wwwroot has a ** pattern at the root, so consulting patterns first shadows every asset in
        // the manifest with a file that does not exist - and everything from every package and every
        // other project stops resolving, which looks the same as having no manifest at all.
        Given("wwwroot", "shadow.js");
        var expected = Given("packages", "real.js");

        var manifest = Manifest(
            Node(pattern: 0, children: Children(("shadow.js", Node(asset: (1, "real.js"))))),
            "wwwroot", "packages");

        Assert.Equal(expected, manifest.Resolve("shadow.js"));
    }

    [Fact]
    public void APatternStillCoversWhatNoAssetNames()
    {
        var expected = Given("wwwroot", "app.css");

        var manifest = Manifest(
            Node(pattern: 0, children: Children(("other.js", Node(asset: (1, "other.js"))))),
            "wwwroot", "packages");

        Assert.Equal(expected, manifest.Resolve("app.css"));
    }

    [Fact]
    public void TheNearestPatternWins()
    {
        // Two patterns on the way down. The deeper one describes the more specific folder, and taking
        // the outer one looks for the file in the wrong content root - where, on a real machine, a
        // file of that name may well exist.
        Given("outer", "_content/Thing/deep.css");
        var expected = Given("inner", "deep.css");

        var manifest = Manifest(
            Node(pattern: 0, children: Children(("_content",
                Node(children: Children(("Thing", Node(pattern: 1))))))),
            "outer", "inner");

        Assert.Equal(expected, manifest.Resolve("_content/Thing/deep.css"));
    }

    // ---- shapes that are not a manifest --------------------------------------------------------

    [Fact]
    public void AMissingManifestIsNotAnError()
    {
        // A published application has none and does not need one, so this is an ordinary answer and
        // the caller decides what it means.
        Assert.Null(StaticWebAssetManifest.Load(Path.Combine(_root, "not-there.json")));
    }

    [Fact]
    public void AnUnreadableManifestIsNotAnError()
    {
        var path = Path.Combine(_root, "broken.json");
        File.WriteAllText(path, "this is not json");

        Assert.Null(StaticWebAssetManifest.Load(path));
    }

    [Fact]
    public void AnEmptyRequestResolvesToNothing()
    {
        var manifest = Manifest(Node(pattern: 0), "wwwroot");

        Assert.Null(manifest.Resolve(""));
        Assert.Null(manifest.Resolve("/"));
    }

    [Fact]
    public void ALeadingSlashIsNotPartOfThePath()
    {
        // The webview asks with a leading slash and Photino passes subpaths through in both shapes.
        var expected = Given("wwwroot", "lib/thing.js");

        var manifest = Manifest(Node(pattern: 0), "wwwroot");

        Assert.Equal(expected, manifest.Resolve("/lib/thing.js"));
        Assert.Equal(expected, manifest.Resolve("lib/thing.js"));
    }

    [Fact]
    public void TheManifestIsNamedAfterTheApplication()
    {
        Assert.Equal(Path.Combine(_root, "MLQT.Photino.staticwebassets.runtime.json"),
                     StaticWebAssetManifest.PathFor(_root, "MLQT.Photino"));
    }
}

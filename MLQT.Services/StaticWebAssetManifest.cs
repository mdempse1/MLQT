using System.Text.Json;

namespace MLQT.Services;

/// <summary>
/// Resolves a web request path to a file on disk, using the manifest the .NET SDK writes beside a
/// built application.
/// </summary>
/// <remarks>
/// <para><b>Why this is needed at all.</b> <c>dotnet publish</c> copies every static web asset into a
/// real <c>wwwroot</c> folder; <c>dotnet build</c> does not. A built application instead gets
/// <c>&lt;name&gt;.staticwebassets.runtime.json</c>, a map from request path to wherever the file
/// actually lives — the project's own <c>wwwroot</c>, another project's, a NuGet package's
/// <c>staticwebassets</c> folder, or an <c>obj</c> directory for generated ones like scoped CSS.
/// ASP.NET Core reads it for you; a desktop host holding a plain file provider does not.</para>
///
/// <para>So <c>MLQT.Photino</c> ran only from a publish. Pressing F5, or running the built
/// executable, opened a window that loaded nothing at all, with no error — the host asked for
/// <c>index.html</c>, the folder was not there, and a webview showing nothing looks exactly like a
/// webview that is still starting. Phase 7b-2 recorded that as the developer story being unresolved;
/// this resolves it.</para>
///
/// <para><b>The format.</b> A list of content roots and a tree of path segments. A node either names
/// an asset — a content root plus a path inside it — or carries a pattern, which serves anything
/// under that root from that point down. Both are needed: the project's own <c>wwwroot</c> is a
/// pattern at the tree root, so <c>index.html</c> is found by pattern, while
/// <c>_framework/blazor.webview.js</c> is an explicit asset pointing into a NuGet package.</para>
/// </remarks>
public sealed class StaticWebAssetManifest
{
    private readonly string[] _contentRoots;
    private readonly Node _root;

    private sealed class Node
    {
        public Dictionary<string, Node> Children { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public (int ContentRootIndex, string SubPath)? Asset { get; init; }
        public (int ContentRootIndex, string Pattern)? Pattern { get; init; }
    }

    private StaticWebAssetManifest(string[] contentRoots, Node root)
    {
        _contentRoots = contentRoots;
        _root = root;
    }

    /// <summary>The manifest an application of this name would have written beside itself.</summary>
    public static string PathFor(string baseDirectory, string assemblyName) =>
        Path.Combine(baseDirectory, $"{assemblyName}.staticwebassets.runtime.json");

    /// <summary>Reads a manifest, or returns null if there is not a usable one there.</summary>
    /// <remarks>
    /// Null rather than an exception: a published application has no manifest and does not need one,
    /// so "not there" is an ordinary answer and the caller decides what it means.
    /// </remarks>
    public static StaticWebAssetManifest? Load(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;

            var contentRoots = root.GetProperty("ContentRoots")
                                   .EnumerateArray()
                                   .Select(e => e.GetString() ?? string.Empty)
                                   .ToArray();

            return new StaticWebAssetManifest(contentRoots, ReadNode(root.GetProperty("Root")));
        }
        catch (Exception ex)
        {
            LoggingService.Error(nameof(StaticWebAssetManifest), $"Could not read {manifestPath}", ex);
            return null;
        }
    }

    private static Node ReadNode(JsonElement element)
    {
        var children = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("Children", out var kids) && kids.ValueKind == JsonValueKind.Object)
            foreach (var child in kids.EnumerateObject())
                children[child.Name] = ReadNode(child.Value);

        (int, string)? asset = null;
        if (element.TryGetProperty("Asset", out var a) && a.ValueKind == JsonValueKind.Object)
            asset = (a.GetProperty("ContentRootIndex").GetInt32(), a.GetProperty("SubPath").GetString() ?? "");

        // Only the first pattern is kept. The SDK writes one per node in every manifest seen, and
        // guessing between several would be a guess; an unresolved path simply falls through to the
        // caller, which is the same as a file that is not there.
        (int, string)? pattern = null;
        if (element.TryGetProperty("Patterns", out var p) && p.ValueKind == JsonValueKind.Array)
            foreach (var candidate in p.EnumerateArray())
            {
                pattern = (candidate.GetProperty("ContentRootIndex").GetInt32(),
                           candidate.GetProperty("Pattern").GetString() ?? "**");
                break;
            }

        return new Node { Children = children, Asset = asset, Pattern = pattern };
    }

    /// <summary>
    /// The file backing a request path, or null when the manifest does not describe one.
    /// </summary>
    /// <param name="requestPath">A web path such as <c>_content/MLQT.Shared/app.css</c>.</param>
    /// <remarks>
    /// The deepest pattern seen on the way down wins, and it is only consulted when no explicit asset
    /// matched — the SDK puts a <c>**</c> pattern for the project's own <c>wwwroot</c> at the tree
    /// root, so trying patterns first would shadow every other project's and package's assets with
    /// files that do not exist.
    /// </remarks>
    public string? Resolve(string requestPath)
    {
        var segments = requestPath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        var node = _root;
        (int Root, int From)? pattern = node.Pattern is { } p0 ? (p0.ContentRootIndex, 0) : null;

        var matched = 0;
        for (; matched < segments.Length; matched++)
        {
            if (!node.Children.TryGetValue(segments[matched], out var child))
                break;

            node = child;
            if (node.Pattern is { } p)
                pattern = (p.ContentRootIndex, matched + 1);
        }

        if (matched == segments.Length && node.Asset is { } asset)
            return Combine(asset.ContentRootIndex, asset.SubPath);

        if (pattern is { } found)
            return Combine(found.Root, string.Join('/', segments.Skip(found.From)));

        return null;
    }

    private string? Combine(int contentRootIndex, string subPath)
    {
        if (contentRootIndex < 0 || contentRootIndex >= _contentRoots.Length)
            return null;

        var path = Path.Combine(_contentRoots[contentRootIndex],
                                subPath.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(path) ? path : null;
    }
}

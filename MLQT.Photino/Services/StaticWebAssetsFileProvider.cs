using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;
using MLQT.Services;

namespace MLQT.Photino.Services;

/// <summary>
/// Serves the application's web assets from wherever the build left them.
/// </summary>
/// <remarks>
/// <para>A published application has a real <c>wwwroot</c> and needs none of this. A <b>built</b> one
/// does not: the SDK writes a manifest instead, and a host holding a plain
/// <see cref="PhysicalFileProvider"/> finds nothing — so <c>dotnet run</c> and F5 opened a window that
/// loaded nothing, silently. See <see cref="StaticWebAssetManifest"/> for the format and the history.</para>
///
/// <para>Only <see cref="GetFileInfo"/> is answered. Blazor's webview asks for named files and never
/// enumerates, and a directory listing built from a manifest whose patterns resolve lazily would be
/// guesswork; returning "not found" is the honest answer to a question nothing asks.</para>
/// </remarks>
internal sealed class StaticWebAssetsFileProvider(StaticWebAssetManifest manifest) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath)
    {
        var resolved = manifest.Resolve(subpath);

        return resolved is null
            ? new NotFoundFileInfo(subpath)
            : new PhysicalFileInfo(new FileInfo(resolved));
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}

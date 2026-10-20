using System.Text.RegularExpressions;
using ModelicaParser.Helpers;
using MLQT.Services;

namespace MLQT.McpServer.Helpers;

/// <summary>One copy of a library found in a folder: where it is, the version it says it is, whether it
/// is encrypted, and the versions it declares it can stand in for unchanged
/// (<c>conversion(noneFromVersion=...)</c>).</summary>
internal sealed record LibraryCandidate(string Path, ModelicaVersion? Version, bool Encrypted, IReadOnlyList<ModelicaVersion> NoneFromVersions);

/// <summary>
/// The copies of a named library in one folder, in the four forms the specification allows a library to
/// be stored in (MLS §13.3, §18.8.3): <c>Name/package.mo</c>, <c>Name Version/package.mo</c>,
/// <c>Name.mo</c> and <c>Name Version.mo</c>. An encrypted <c>package.moe</c> folder counts too. A
/// version is read from the name first, then from the package's own <c>version</c> annotation.
/// </summary>
internal static class LibraryCandidates
{
    // "version" written as an element of the package's annotation: after "annotation(" or a comma. In
    // uses(Modelica(version=...)) and conversion(from(version=...)) it follows a name and "(", and is
    // somebody else's version.
    private static readonly Regex OwnVersion = new(@"(?:\bannotation\s*\(|,)\s*version\s*=\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NoneFrom = new(@"\bnoneFromVersion\s*=\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<LibraryCandidate> Find(string folder, string name)
    {
        var found = new List<LibraryCandidate>();
        if (!Directory.Exists(folder))
            return found;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        foreach (var entry in entries.OrderBy(e => e, StringComparer.Ordinal))
        {
            var leaf = System.IO.Path.GetFileName(entry);
            var isFile = File.Exists(entry);
            var stem = isFile ? System.IO.Path.GetFileNameWithoutExtension(leaf) : leaf;
            if (isFile && !leaf.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                continue;
            string? versionInName;
            if (stem == name)
                versionInName = null;
            else if (stem.StartsWith(name + " ", StringComparison.Ordinal))
                versionInName = stem[(name.Length + 1)..];
            else
                continue;

            if (isFile)
            {
                found.Add(Read(entry, entry, versionInName, encrypted: false));
            }
            else if (File.Exists(System.IO.Path.Combine(entry, "package.mo")))
            {
                found.Add(Read(entry, System.IO.Path.Combine(entry, "package.mo"), versionInName, encrypted: false));
            }
            else if (EncryptedLibraryDetector.IsEncryptedLibraryRoot(entry))
            {
                var info = EncryptedLibraryDetector.Detect(entry);
                found.Add(new LibraryCandidate(entry, ModelicaVersion.Parse(versionInName ?? info?.Version), true, []));
            }
        }
        return found;
    }

    private static LibraryCandidate Read(string path, string packageFile, string? versionInName, bool encrypted)
    {
        string text;
        try
        {
            text = ModelicaFileEncoding.ReadAllTextOnly(packageFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = string.Empty;
        }
        var version = ModelicaVersion.Parse(versionInName) ?? ModelicaVersion.Parse(OwnVersion.Match(text) is { Success: true } m ? m.Groups[1].Value : null);
        var noneFrom = NoneFrom.Matches(text).Select(x => ModelicaVersion.Parse(x.Groups[1].Value)).OfType<ModelicaVersion>().ToList();
        return new LibraryCandidate(path, version, encrypted, noneFrom);
    }
}

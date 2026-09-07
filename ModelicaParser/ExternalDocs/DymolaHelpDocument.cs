using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ModelicaParser.ExternalDocs;

/// <summary>
/// Everything recovered from one library's generated help directory.
/// </summary>
/// <param name="Classes">Documented classes, ordered by name.</param>
/// <param name="FilesRead">How many HTML files were parsed as class documentation.</param>
/// <param name="FilesSkipped">How many HTML files were present but not Dymola-generated class
/// documentation, and were therefore ignored.</param>
public sealed record DymolaHelpDocument(
    IReadOnlyList<DocumentedClass> Classes,
    int FilesRead,
    int FilesSkipped)
{
    public static DymolaHelpDocument Empty { get; } = new([], 0, 0);
}

/// <summary>
/// Reads a library's whole <c>help/</c> directory, resolving the one piece of information a single
/// file cannot supply on its own: whether a <b>package</b> has an icon.
///
/// <para>A class's heading carries its rendered icon, so its presence answers the question
/// directly — except for the package that owns each page, whose heading never carries one. That
/// package's icon has to come from the small image its parent showed for it in the package-content
/// table, and telling a real icon from the placeholder the generator draws for a class that has
/// none requires knowing what the placeholder looks like.</para>
///
/// <para>Rather than hard-code that (it is a rendered image, and both its content and the file
/// naming around it have changed between Dymola releases), the placeholder set is <b>calibrated
/// from the document itself</b>: every non-package class whose heading carried no icon is known to
/// have none, so whatever images those classes were given are by definition placeholders. There
/// turns out to be one per class restriction — types, connectors and records each get a different
/// default — which is why this collects a set rather than a single value.</para>
/// </summary>
public static class DymolaHelpReader
{
    /// <summary>
    /// Reads the help directory of a library. Returns <see cref="DymolaHelpDocument.Empty"/> when
    /// the directory does not exist or holds nothing recognisable, so a library that ships no
    /// usable documentation degrades to "we know nothing about it" rather than throwing.
    /// </summary>
    public static DymolaHelpDocument Read(string helpDirectory)
    {
        if (string.IsNullOrWhiteSpace(helpDirectory) || !Directory.Exists(helpDirectory))
            return DymolaHelpDocument.Empty;

        string[] files;
        try
        {
            files = Directory.GetFiles(helpDirectory, "*.html", SearchOption.TopDirectoryOnly);
        }
        catch (IOException)
        {
            return DymolaHelpDocument.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return DymolaHelpDocument.Empty;
        }

        var parsed = new ConcurrentBag<ParsedHelpFile>();
        var skipped = 0;
        Parallel.ForEach(files, file =>
        {
            var result = ParseOne(file);
            if (result is null || result.Classes.Count == 0)
            {
                Interlocked.Increment(ref skipped);
                return;
            }

            parsed.Add(result);
        });

        var byName = new Dictionary<string, DocumentedClass>(StringComparer.Ordinal);
        var iconByClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in parsed)
        {
            foreach (var documented in file.Classes)
                byName.TryAdd(documented.FullName, documented);

            foreach (var (name, icon) in file.IconByClass)
                iconByClass.TryAdd(name, icon);
        }

        var classes = ResolvePackageIcons(byName, iconByClass, helpDirectory);

        return new DymolaHelpDocument(classes, files.Length - skipped, skipped);
    }

    private static ParsedHelpFile? ParseOne(string file)
    {
        try
        {
            return DymolaHelpParser.ParseFile(File.ReadAllText(file));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<DocumentedClass> ResolvePackageIcons(
        Dictionary<string, DocumentedClass> byName,
        Dictionary<string, string> iconByClass,
        string helpDirectory)
    {
        var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? HashOf(string imageName)
        {
            if (hashes.TryGetValue(imageName, out var cached))
                return cached;

            var hash = HashImage(helpDirectory, imageName);
            hashes[imageName] = hash;
            return hash;
        }

        // Calibration: a class whose heading carried no icon has none, so whatever image it was
        // given in its parent's content table is a placeholder.
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var documented in byName.Values)
        {
            if (documented.HasIcon != false || !iconByClass.TryGetValue(documented.FullName, out var image))
                continue;

            if (HashOf(image) is { } hash)
                placeholders.Add(hash);
        }

        var resolved = new List<DocumentedClass>(byName.Count);
        foreach (var documented in byName.Values)
        {
            if (documented.HasIcon is not null)
            {
                resolved.Add(documented);
                continue;
            }

            // Unresolvable stays null. The library's root package has no parent to have shown an
            // icon for it, so this is the normal outcome for exactly one class per library.
            if (!iconByClass.TryGetValue(documented.FullName, out var image) || HashOf(image) is not { } hash)
            {
                resolved.Add(documented);
                continue;
            }

            resolved.Add(documented with
            {
                HasIcon = !placeholders.Contains(hash),
                IconImagePath = image
            });
        }

        resolved.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));
        return resolved;
    }

    private static string? HashImage(string helpDirectory, string imageName)
    {
        try
        {
            // Icon references are plain file names beside the HTML; anything else (a path into
            // Resources, an absolute URL) is not an icon render and cannot be calibrated.
            if (imageName.Contains('/') || imageName.Contains('\\') || Path.IsPathRooted(imageName))
                return null;

            var path = Path.Combine(helpDirectory, imageName);
            if (!File.Exists(path))
                return null;

            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

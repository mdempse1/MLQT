using MLQT.Services.DataTypes;

namespace MLQT.Services;

/// <summary>
/// Recognises a Modelica library that ships encrypted, and reads the little metadata that is
/// still legible beside the encrypted package.
/// </summary>
public static class EncryptedLibraryDetector
{
    /// <summary>File name of the encrypted package that marks such a library.</summary>
    public const string EncryptedPackageFileName = "package.moe";

    private const string HelpDirectoryName = "help";
    private const string LibraryInfoFileName = "libraryinfo.mos";

    /// <summary>
    /// Whether the directory is the root of an encrypted library.
    /// </summary>
    public static bool IsEncryptedLibraryRoot(string directoryPath) =>
        !string.IsNullOrWhiteSpace(directoryPath) &&
        File.Exists(Path.Combine(directoryPath, EncryptedPackageFileName));

    /// <summary>
    /// Reads what can be established about an encrypted library, or returns null when the
    /// directory is not one.
    /// </summary>
    public static EncryptedLibraryInfo? Detect(string directoryPath)
    {
        if (!IsEncryptedLibraryRoot(directoryPath))
            return null;

        var root = Path.GetFullPath(directoryPath);
        var directoryName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var helpDirectory = Path.Combine(root, HelpDirectoryName);
        var hasHelp = Directory.Exists(helpDirectory) &&
                      Directory.EnumerateFiles(helpDirectory, "*.html").Any();

        var (infoName, infoVersion) = ReadLibraryInfo(Path.Combine(root, LibraryInfoFileName));
        var (directoryBaseName, directoryVersion) = SplitVersionedDirectoryName(directoryName);

        // The versioned directory name leads. It is what a tool resolves against when deciding
        // which copy of a library to load, so it states which version is actually on the machine —
        // exactly the question the dependency-version check asks. libraryinfo.mos is the fallback,
        // and the only source for the libraries that ship no version suffix at all.
        var version = directoryVersion ?? infoVersion;

        var name = !string.IsNullOrWhiteSpace(directoryBaseName) ? directoryBaseName
            : !string.IsNullOrWhiteSpace(infoName) ? infoName!
            : directoryName;

        return new EncryptedLibraryInfo(
            root,
            Path.Combine(root, EncryptedPackageFileName),
            hasHelp ? helpDirectory : null,
            name,
            version);
    }

    /// <summary>
    /// Splits a Modelica versioned directory name ("Battery 2.9.0") into its library name and
    /// version, per the language specification's versioned-directory convention.
    ///
    /// <para>The suffix is only accepted when it actually looks like a version. Without that
    /// guard a library whose name merely contains a space ("My Library") would report a version
    /// of "Library", and — since the directory name is consulted first — that nonsense would win
    /// over the correct value sitting in <c>libraryinfo.mos</c>.</para>
    /// </summary>
    internal static (string Name, string? Version) SplitVersionedDirectoryName(string directoryName)
    {
        var lastSpace = directoryName.LastIndexOf(' ');
        if (lastSpace <= 0 || lastSpace == directoryName.Length - 1)
            return (directoryName, null);

        var suffix = directoryName[(lastSpace + 1)..];
        return LooksLikeVersion(suffix)
            ? (directoryName[..lastSpace].TrimEnd(), suffix)
            : (directoryName, null);
    }

    /// <summary>
    /// A version starts with a digit and continues with the characters versions are made of.
    /// This deliberately admits build suffixes ("4.0.0+maint") because the version comparison
    /// downstream already knows to ignore anything past the numeric segments.
    /// </summary>
    private static bool LooksLikeVersion(string text)
    {
        if (text.Length == 0 || !char.IsAsciiDigit(text[0]))
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '+' && c != '-' && c != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the display name and version from a <c>libraryinfo.mos</c> registration script.
    /// The file is a Dymola script rather than structured data, so the two values are lifted out
    /// of it directly; anything unexpected in the file simply yields nulls.
    /// </summary>
    private static (string? Name, string? Version) ReadLibraryInfo(string libraryInfoPath)
    {
        if (!File.Exists(libraryInfoPath))
            return (null, null);

        try
        {
            var content = File.ReadAllText(libraryInfoPath);
            return (ReadQuotedValue(content, "reference"), ReadQuotedValue(content, "version"));
        }
        catch (IOException)
        {
            return (null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static string? ReadQuotedValue(string content, string key)
    {
        var index = 0;
        while (index < content.Length)
        {
            index = content.IndexOf(key, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            var after = index + key.Length;

            // Require a delimiter before the key so "ModelicaVersion" is not read as "version".
            var precededProperly = index == 0 || !char.IsLetterOrDigit(content[index - 1]);
            if (precededProperly)
            {
                while (after < content.Length && char.IsWhiteSpace(content[after]))
                    after++;

                if (after < content.Length && content[after] == '=')
                {
                    after++;
                    while (after < content.Length && char.IsWhiteSpace(content[after]))
                        after++;

                    if (after < content.Length && content[after] == '"')
                    {
                        var end = content.IndexOf('"', after + 1);
                        if (end > after)
                        {
                            var value = content[(after + 1)..end].Trim();
                            if (value.Length > 0)
                                return value;
                        }
                    }
                }
            }

            index += key.Length;
        }

        return null;
    }
}

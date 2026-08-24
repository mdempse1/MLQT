namespace MLQT.Services.Tests.ExternalDocs;

/// <summary>
/// Locates an installed Dymola library folder so the encrypted-library tests can run against
/// real vendor documentation rather than only against fixtures.
///
/// <para>These tests validate a format MLQT does not control, so exercising the genuine article
/// is the point of them. They are skipped when no Dymola is installed — a build machine without
/// one is a normal situation, not a failure — but they must never be allowed to pass vacuously
/// somewhere Dymola <i>is</i> present, so every one of them asserts on real numbers when it
/// finds an install.</para>
/// </summary>
internal static class DymolaInstall
{
    /// <summary>
    /// The <c>Modelica\Library</c> folder of the newest installed Dymola, or null when none is
    /// installed. Newest is chosen by directory name, which sorts correctly for Dymola's
    /// "2025x", "2026x", "2026x Refresh 1" naming.
    /// </summary>
    public static string? LibraryRoot { get; } = FindLibraryRoot();

    public static bool IsAvailable => LibraryRoot is not null;

    private static string? FindLibraryRoot()
    {
        foreach (var programFiles in ProgramFilesRoots())
        {
            if (!Directory.Exists(programFiles))
                continue;

            var candidate = Directory.GetDirectories(programFiles, "Dymola *")
                .Select(dymola => Path.Combine(dymola, "Modelica", "Library"))
                .Where(Directory.Exists)
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (candidate is not null)
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    /// <summary>
    /// Path of an installed library directory whose name starts with <paramref name="prefix"/>
    /// (e.g. "Modelica 4."), or null when it is not installed.
    /// </summary>
    public static string? FindLibrary(string prefix)
    {
        if (LibraryRoot is null)
            return null;

        return Directory.GetDirectories(LibraryRoot, prefix + "*")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Every installed encrypted library (one shipping a <c>package.moe</c>).</summary>
    public static IReadOnlyList<string> EncryptedLibraries()
    {
        if (LibraryRoot is null)
            return [];

        return Directory.GetDirectories(LibraryRoot)
            .Where(EncryptedLibraryDetector.IsEncryptedLibraryRoot)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

using MLQT.Services.DataTypes;

namespace MLQT.Services;

/// <summary>
/// The old name of <see cref="LibraryPrecedence"/>, from when its whole rule was that readable source
/// supersedes an encrypted build. Kept, forwarding, for code built against it; it goes at the release
/// after next.
/// </summary>
[Obsolete("Use LibraryPrecedence, which decides between every kind of copy of a library.")]
public static class SourceSupersedesEncrypted
{
    /// <inheritdoc cref="LibraryPrecedence.SameLibrary"/>
    public static bool SameLibrary(string? a, string? b) => LibraryPrecedence.SameLibrary(a, b);

    /// <inheritdoc cref="LibraryPrecedence.ReadableSourceFor"/>
    public static string? ReadableSourceFor(
        string? encryptedName, IEnumerable<(string Name, string Location)> readable) =>
        LibraryPrecedence.ReadableSourceFor(encryptedName, readable);

    /// <inheritdoc cref="LibraryPrecedence.Retires"/>
    public static IReadOnlyList<LoadedLibrary> Retires(LoadedLibrary arriving, IEnumerable<LoadedLibrary> loaded) =>
        LibraryPrecedence.Retires(arriving, loaded);
}

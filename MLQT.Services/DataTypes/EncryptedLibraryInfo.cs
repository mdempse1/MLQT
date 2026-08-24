namespace MLQT.Services.DataTypes;

/// <summary>
/// A Modelica library that ships encrypted — a single unreadable <c>package.moe</c> in place of a
/// source tree — together with the vendor documentation that describes what is inside it.
/// </summary>
/// <param name="RootPath">The library's root directory.</param>
/// <param name="EncryptedPackagePath">Full path of the encrypted <c>package.moe</c>.</param>
/// <param name="HelpDirectory">Full path of the generated documentation directory, or null when
/// the library ships none. Without it nothing about the library's classes can be recovered.</param>
/// <param name="Name">Best guess at the library's top-level package name, from the directory name
/// or <c>libraryinfo.mos</c>. The documentation's own root class is more authoritative and should
/// be preferred once parsed.</param>
/// <param name="Version">The library version, or null when neither source stated one.</param>
public sealed record EncryptedLibraryInfo(
    string RootPath,
    string EncryptedPackagePath,
    string? HelpDirectory,
    string Name,
    string? Version)
{
    /// <summary>
    /// Whether anything can actually be recovered from this library. False for a library shipping
    /// no documentation at all, which stays entirely opaque and must be treated as an external
    /// namespace rather than as a set of classes we can check against.
    /// </summary>
    public bool HasDocumentation => !string.IsNullOrEmpty(HelpDirectory);
}

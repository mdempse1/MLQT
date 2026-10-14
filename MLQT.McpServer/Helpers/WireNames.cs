using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using RevisionControl;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The text an enum value is sent to a client as, for every enum a tool result carries as a string.
/// </summary>
/// <remarks>
/// <b>Written down, never <c>ToString()</c></b>: these strings are part of what a client reads, and an
/// obfuscated build renames enum members, so <c>ToString()</c> would send whatever the obfuscator chose
/// (B502). Each is the member's own name, which <c>WireNamesTests</c> holds for every member of every
/// enum here, so a member added without its line here fails a test instead of reaching a client as a
/// number.
/// </remarks>
internal static class WireNames
{
    public static string Of(ReadOnlySourceKind value) => value switch
    {
        ReadOnlySourceKind.RecoveredFromDocumentation => "RecoveredFromDocumentation",
        ReadOnlySourceKind.Supplied => "Supplied",
        _ => Unnamed(value),
    };

    public static string Of(ResourceWarningType value) => value switch
    {
        ResourceWarningType.MissingFile => "MissingFile",
        ResourceWarningType.AbsolutePath => "AbsolutePath",
        _ => Unnamed(value),
    };

    public static string Of(ResourceReferenceType value) => value switch
    {
        ResourceReferenceType.LoadResource => "LoadResource",
        ResourceReferenceType.LoadSelector => "LoadSelector",
        ResourceReferenceType.LoadResourceParameter => "LoadResourceParameter",
        ResourceReferenceType.UriReference => "UriReference",
        ResourceReferenceType.ExternalInclude => "ExternalInclude",
        ResourceReferenceType.ExternalLibrary => "ExternalLibrary",
        ResourceReferenceType.ExternalIncludeDirectory => "ExternalIncludeDirectory",
        ResourceReferenceType.ExternalLibraryDirectory => "ExternalLibraryDirectory",
        ResourceReferenceType.ExternalSourceDirectory => "ExternalSourceDirectory",
        _ => Unnamed(value),
    };

    public static string Of(RepositoryVcsType value) => value switch
    {
        RepositoryVcsType.Local => "Local",
        RepositoryVcsType.Git => "Git",
        RepositoryVcsType.SVN => "SVN",
        _ => Unnamed(value),
    };

    public static string Of(LibrarySourceType value) => value switch
    {
        LibrarySourceType.File => "File",
        LibrarySourceType.Directory => "Directory",
        LibrarySourceType.Zip => "Zip",
        LibrarySourceType.Git => "Git",
        LibrarySourceType.SVN => "SVN",
        LibrarySourceType.EncryptedDirectory => "EncryptedDirectory",
        LibrarySourceType.Supplied => "Supplied",
        _ => Unnamed(value),
    };

    public static string Of(VcsFileStatus value) => value switch
    {
        VcsFileStatus.Modified => "Modified",
        VcsFileStatus.Added => "Added",
        VcsFileStatus.Deleted => "Deleted",
        VcsFileStatus.Renamed => "Renamed",
        VcsFileStatus.Untracked => "Untracked",
        VcsFileStatus.Conflicted => "Conflicted",
        _ => Unnamed(value),
    };

    public static string Of(VcsChangeType value) => value switch
    {
        VcsChangeType.Added => "Added",
        VcsChangeType.Modified => "Modified",
        VcsChangeType.Deleted => "Deleted",
        VcsChangeType.Renamed => "Renamed",
        VcsChangeType.Copied => "Copied",
        _ => Unnamed(value),
    };

    public static string Of(ClassElementKind value) => value switch
    {
        ClassElementKind.Component => "Component",
        ClassElementKind.Extends => "Extends",
        ClassElementKind.Import => "Import",
        ClassElementKind.Class => "Class",
        _ => Unnamed(value),
    };

    // A value with no line above: its number, which at least names it the same way in every build.
    private static string Unnamed<T>(T value) where T : struct, Enum =>
        Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

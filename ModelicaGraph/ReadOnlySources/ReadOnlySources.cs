using System.Text;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// The answers about read-only libraries that more than one layer asks: which paths may never be
/// written, which copy of a class wins, and how a provenance note is written into a class.
/// </summary>
public static class ReadOnlySources
{
    /// <summary>
    /// The prefix of every file path given to a class held only in memory. Deliberately not a path
    /// anything can open: a write that took such a class's file path at face value would otherwise
    /// create a file that looks like the vendor's library.
    /// </summary>
    public const string InMemoryPathPrefix = "mlqt-readonly://";

    /// <summary>The root every in-memory file of <paramref name="libraryName"/> sits under.</summary>
    public static string InMemoryRoot(string libraryName) => InMemoryPathPrefix + libraryName;

    /// <summary>Whether a path names a file held only in memory.</summary>
    public static bool IsInMemoryPath(string? path) =>
        path is not null && path.StartsWith(InMemoryPathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a path names a file MLQT must never write: an encrypted <c>package.moe</c>, or a file
    /// of a library held only in memory. Every write path asks this before taking a class's file
    /// path at face value, so a new kind of read-only source is refused everywhere by being added
    /// here.
    ///
    /// <para>Decided by the path alone, so the answer does not depend on how much of the graph has
    /// been built, and is still right for a path that has not been loaded at all.</para>
    /// </summary>
    public static bool IsReadOnlyPath(string? path) =>
        ExternalStubBuilder.IsEncryptedPackageFile(path) || IsInMemoryPath(path);

    /// <summary>
    /// What kind of read-only class a node is, or null when it was read from source. A recovered
    /// class keeps the documentation it was rebuilt from; a supplied one does not.
    /// </summary>
    public static ReadOnlySourceKind? KindOf(ModelNode node) =>
        !node.IsExternalStub ? null
        : node.RecoveredFromDocumentation is not null ? ReadOnlySourceKind.RecoveredFromDocumentation
        : ReadOnlySourceKind.Supplied;

    /// <summary>
    /// How strongly a copy of a library or class holds its place: readable source (null) 2,
    /// <see cref="ReadOnlySourceKind.Supplied"/> 1, <see cref="ReadOnlySourceKind.RecoveredFromDocumentation"/>
    /// 0. For the same library the higher retires the lower whole, in whichever order they arrive.
    ///
    /// <para><b>The one ranking.</b> Library precedence, the graph keeping one of two copies of a
    /// class, and a class refusing to move into a lower-ranked file all ask it, so the three cannot
    /// disagree about which copy a user is shown.</para>
    /// </summary>
    public static int Precedence(ReadOnlySourceKind? kind) => kind switch
    {
        null => 2,
        ReadOnlySourceKind.Supplied => 1,
        _ => 0
    };

    /// <summary>The precedence of the source a file belongs to, judged by its path.</summary>
    internal static int PrecedenceOfFile(string? path) =>
        ExternalStubBuilder.IsEncryptedPackageFile(path) ? Precedence(ReadOnlySourceKind.RecoveredFromDocumentation)
        : IsInMemoryPath(path) ? Precedence(ReadOnlySourceKind.Supplied)
        : Precedence(null);

    /// <summary>
    /// A provenance note as the comment block a class opens with: each line behind <c>// </c>, and a
    /// line feed after the last.
    /// </summary>
    internal static string Banner(string provenanceNote)
    {
        var banner = new StringBuilder();
        foreach (var line in provenanceNote.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
            banner.Append(line.Length == 0 ? "//" : "// ").Append(line).Append('\n');
        return banner.ToString();
    }
}

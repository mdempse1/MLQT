using ModelicaParser.ExternalDocs;

namespace ModelicaGraph;

/// <summary>
/// A library whose classes MLQT is given rather than reads from a checkout: loaded for reference,
/// never written, never reported on, and never the vendor's own source.
///
/// <para><b>The one way such a library comes in.</b> An encrypted library rebuilt from its vendor's
/// help HTML is one implementation (<see cref="ReadOnlySourceKind.RecoveredFromDocumentation"/>); a
/// host that holds a vendor-issued description of a library in memory is the other
/// (<see cref="ReadOnlySourceKind.Supplied"/>). Both reach the graph through
/// <see cref="ReadOnlySourceLoader"/>, so what "read-only" means is decided once — on the class
/// (<see cref="DataTypes.ModelNode.IsExternalStub"/>), on its file
/// (<see cref="ReadOnlySources.IsReadOnlyPath"/>) and on the library — rather than once per source.</para>
///
/// <para><b>Read once, at load.</b> Everything a loaded class needs is then in the graph, which is
/// what lets every existing tool answer on it without knowing where it came from. A source is never
/// called back, so it holds no lock, thread or lifetime obligation towards MLQT.</para>
/// </summary>
public interface IReadOnlyClassSource
{
    /// <summary>The top-level package name. Precedence between copies of a library is by this name.</summary>
    string LibraryName { get; }

    /// <summary>The library's version, or null when it is not known. Compared exactly.</summary>
    string? LibraryVersion { get; }

    /// <summary>What the classes are, which decides their precedence and how they are read.</summary>
    ReadOnlySourceKind Kind { get; }

    /// <summary>
    /// Where the classes are said to live, or null for a library held only in memory — whose classes
    /// are then placed under <see cref="ReadOnlySources.InMemoryRoot"/>. A location must itself be a
    /// path <see cref="ReadOnlySources.IsReadOnlyPath"/> accepts, such as an encrypted
    /// <c>package.moe</c>: a class's file path is what every write path asks before writing.
    /// </summary>
    string? Location { get; }

    /// <summary>
    /// Plain text every class opens with, as <c>//</c> comment lines, saying where it came from and
    /// that it is not the vendor's source. It travels with the text, so it is still there when the
    /// code is copied out of a viewer or reaches an agent.
    /// </summary>
    string ProvenanceNote { get; }

    /// <summary>
    /// The classes. <see cref="ReadOnlySourceKind.Supplied"/> fills
    /// <see cref="ReadOnlySourceContent.Texts"/> and
    /// <see cref="ReadOnlySourceKind.RecoveredFromDocumentation"/> fills
    /// <see cref="ReadOnlySourceContent.Documented"/>; the other list must be empty.
    /// </summary>
    ReadOnlySourceContent Read(CancellationToken cancellationToken);
}

/// <summary>
/// What a read-only library's classes are. The order is their precedence: for the same library,
/// readable source beats <see cref="Supplied"/>, which beats <see cref="RecoveredFromDocumentation"/>
/// (<see cref="ReadOnlySources.Precedence"/>).
/// </summary>
public enum ReadOnlySourceKind
{
    /// <summary>
    /// Rebuilt from the vendor's documentation: names, descriptions, base classes, whether there is
    /// an icon, and member lists with no types. Each class keeps the record it was rebuilt from in
    /// <see cref="DataTypes.ModelNode.RecoveredFromDocumentation"/>.
    /// </summary>
    RecoveredFromDocumentation,

    /// <summary>
    /// Modelica text a host supplies — declarations, connectors, graphics and whatever equations the
    /// vendor makes visible — parsed like any file but held only in memory.
    /// </summary>
    Supplied
}

/// <summary>What <see cref="IReadOnlyClassSource.Read"/> returns.</summary>
public sealed class ReadOnlySourceContent
{
    /// <summary>Modelica text, one entry per file-equivalent. For <see cref="ReadOnlySourceKind.Supplied"/>.</summary>
    public IReadOnlyList<SuppliedText> Texts { get; init; } = [];

    /// <summary>Classes recovered from documentation. For <see cref="ReadOnlySourceKind.RecoveredFromDocumentation"/>.</summary>
    public IReadOnlyList<DocumentedClass> Documented { get; init; } = [];
}

/// <summary>
/// One file's worth of supplied Modelica: a class with the classes nested in it, and the
/// <c>within</c> clause placing it, exactly as it would be written to a <c>.mo</c> file.
/// </summary>
/// <param name="RelativePath">Where the text would sit in the library, such as
/// <c>Lib/Sub/package.mo</c>. Names the class's file node under
/// <see cref="ReadOnlySources.InMemoryRoot"/>; it is never a path on disk.</param>
/// <param name="Text">The Modelica text.</param>
public sealed record SuppliedText(string RelativePath, string Text);

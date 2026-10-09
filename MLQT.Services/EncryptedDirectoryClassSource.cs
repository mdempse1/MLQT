using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaParser.ExternalDocs;

namespace MLQT.Services;

/// <summary>
/// An encrypted library as a read-only class source: its classes rebuilt from the vendor's
/// generated help HTML, standing for the <c>package.moe</c> they cannot be read from.
///
/// <para>The first implementation of <see cref="IReadOnlyClassSource"/>, and the reason the
/// interface is shaped as it is: whatever a host supplies from memory reaches the graph through
/// the same loader, the same precedence and the same write guards as this.</para>
/// </summary>
internal sealed class EncryptedDirectoryClassSource(EncryptedLibraryInfo detected) : IReadOnlyClassSource
{
    /// <inheritdoc/>
    public string LibraryName => detected.Name;

    /// <inheritdoc/>
    public string? LibraryVersion => detected.Version;

    /// <inheritdoc/>
    public ReadOnlySourceKind Kind => ReadOnlySourceKind.RecoveredFromDocumentation;

    /// <inheritdoc/>
    public string? Location => detected.EncryptedPackagePath;

    /// <inheritdoc/>
    public string ProvenanceNote => ExternalStubBuilder.RecoveredProvenanceNote;

    /// <summary>What the last <see cref="Read"/> found, for the load's log. Empty before one.</summary>
    public DymolaHelpDocument Document { get; private set; } = DymolaHelpDocument.Empty;

    /// <summary>
    /// The documented classes, or none for a library that ships no documentation. Loading zero
    /// classes is the honest outcome there: the namespace stays opaque, so references into it remain
    /// unresolved and are treated as external rather than as pointing at classes we "know" are
    /// absent.
    /// </summary>
    public ReadOnlySourceContent Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Document = detected.HasDocumentation
            ? DymolaHelpReader.Read(detected.HelpDirectory!)
            : DymolaHelpDocument.Empty;
        return new ReadOnlySourceContent { Documented = Document.Classes };
    }
}

using ModelicaParser.ExternalDocs;

namespace ModelicaGraph;

/// <summary>What <see cref="IReadOnlyClassSource.Read"/> returns.</summary>
public sealed class ReadOnlySourceContent
{
    /// <summary>Modelica text, one entry per file-equivalent. For <see cref="ReadOnlySourceKind.Supplied"/>.</summary>
    public IReadOnlyList<SuppliedText> Texts { get; init; } = [];

    /// <summary>Classes recovered from documentation. For <see cref="ReadOnlySourceKind.RecoveredFromDocumentation"/>.</summary>
    public IReadOnlyList<DocumentedClass> Documented { get; init; } = [];
}

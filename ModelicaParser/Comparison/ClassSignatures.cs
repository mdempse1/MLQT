using ModelicaParser.Helpers;

namespace ModelicaParser.Comparison;

/// <summary>
/// Every class in one version of a file, reduced to what a comparison needs.
/// </summary>
/// <param name="Parsed">
/// False when the text could not be read as Modelica. Nothing is claimed about a file in that state
/// — a half-recovered parse tree produces half a signature, and a signature that is missing an
/// equation because the parser gave up would report a real change as no change at all.
/// </param>
/// <param name="Classes">The classes found, by full Modelica name. Empty when <paramref name="Parsed"/> is false.</param>
public sealed record ClassSignatures(bool Parsed, IReadOnlyDictionary<string, ClassSignature> Classes)
{
    /// <summary>There is no such version of the file — it is new, or nothing has it committed.</summary>
    public static readonly ClassSignatures Absent =
        new(Parsed: true, new Dictionary<string, ClassSignature>(StringComparer.Ordinal));

    /// <summary>The file is there but could not be read.</summary>
    public static readonly ClassSignatures Unreadable =
        new(Parsed: false, new Dictionary<string, ClassSignature>(StringComparer.Ordinal));

    /// <summary>
    /// Reduces one Modelica file's text to a signature per class in it.
    /// </summary>
    /// <remarks>
    /// <para>Null in means <see cref="Absent"/>: a file the version control system has no copy of is
    /// a complete answer, not a missing one, and every class in the other version is new.</para>
    ///
    /// <para>The text is preprocessed once and that exact string is both parsed and sliced, so the
    /// offsets ANTLR reports index the source the surface comparison reads.</para>
    /// </remarks>
    /// <param name="fileText">A whole <c>.mo</c> file's text, or a single class's source.</param>
    public static ClassSignatures Of(string? fileText)
    {
        if (fileText is null)
            return Absent;

        var source = ModelicaParserHelper.PreprocessCode(fileText);

        try
        {
            var (tree, errors) = ModelicaParserHelper.ParseWithErrors(source);
            if (errors.Count > 0)
                return Unreadable;

            var visitor = new ClassSignatureVisitor(source);
            visitor.Visit(tree);

            // First wins. Two classes of the same full name is not legal Modelica, and picking one
            // arbitrarily is better than throwing away the file over it.
            var classes = new Dictionary<string, ClassSignature>(StringComparer.Ordinal);
            foreach (var signature in visitor.Signatures)
                classes.TryAdd(signature.FullName, signature);

            return new ClassSignatures(Parsed: true, classes);
        }
        catch (Exception)
        {
            // ANTLR error recovery can leave a tree the visitor cannot walk. That is the same
            // answer as a syntax error: nothing is known about this version.
            return Unreadable;
        }
    }
}

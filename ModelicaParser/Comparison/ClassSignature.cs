namespace ModelicaParser.Comparison;

/// <summary>
/// One class reduced to the two strings a comparison needs: what it means, and what it says.
/// </summary>
/// <remarks>
/// <para><b>What <see cref="Semantic"/> leaves out is not thrown away.</b> Each display-only
/// annotation lands in exactly one of <see cref="Graphics"/>, <see cref="Documentation"/>,
/// <see cref="Dialog"/> and <see cref="Tooling"/> (by <see cref="SimulationAnnotations.CategoryOf"/>),
/// and description strings in <see cref="Documentation"/>. So with <see cref="Semantic"/> equal, the
/// one of those that differs says what kind of cosmetic edit it was — "only the graphics changed" —
/// without a caller repeating the list of display-only names.</para>
///
/// <para><b>All of them exclude the class's nested classes</b>, which each appear as a placeholder carrying
/// only their name. A nested class has a signature of its own, so a package whose only edit is
/// inside one of its children compares equal on both counts and the child carries the change. That
/// is what keeps a marker on the class that changed instead of on every package above it.</para>
/// </remarks>
/// <param name="FullName">
/// The class's full Modelica name, built from the file's <c>within</c> clause and its enclosing
/// classes — the same string <c>GraphBuilder.GenerateModelId</c> produces, so a caller holding a
/// <c>ModelNode</c> can look one up by <c>Id</c>.
/// </param>
/// <param name="Semantic">
/// A canonical token stream with everything a translator ignores removed: comments, description
/// strings, and the annotations <see cref="SimulationAnnotations"/> vouches for. Two classes with
/// equal <see cref="Semantic"/> simulate identically. Not intended to be read — only compared.
/// </param>
/// <param name="Surface">
/// The class's own source, verbatim, with its nested classes replaced by their names. Compared only
/// after <see cref="Semantic"/> has matched, to tell a class that was reformatted from one that was
/// not touched at all.
/// </param>
public sealed record ClassSignature(string FullName, string Semantic, string Surface)
{
    /// <summary>
    /// The class's <see cref="CosmeticCategory.Graphics"/> annotations — what <see cref="Semantic"/>
    /// dropped for being drawing — in the same canonical form, so equal strings mean the class draws
    /// the same. Each annotation's elements are sorted, as the kept ones are, so reordering alone is
    /// not a change. Empty when it has none.
    /// </summary>
    public string Graphics { get; init; } = "";

    /// <summary>
    /// What <see cref="Semantic"/> dropped for being documentation: the description strings, in
    /// document order, and the <see cref="CosmeticCategory.Documentation"/> annotations. Comments are
    /// not here — they are part of no canonical form, and only <see cref="Surface"/> sees them.
    /// </summary>
    public string Documentation { get; init; } = "";

    /// <summary>The class's <see cref="CosmeticCategory.Dialog"/> annotations, as <see cref="Graphics"/> holds its drawing.</summary>
    public string Dialog { get; init; } = "";

    /// <summary>The class's <see cref="CosmeticCategory.Tooling"/> annotations, as <see cref="Graphics"/> holds its drawing.</summary>
    public string Tooling { get; init; } = "";
}

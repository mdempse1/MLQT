namespace ModelicaParser.Comparison;

/// <summary>
/// One class reduced to the two strings a comparison needs: what it means, and what it says.
/// </summary>
/// <remarks>
/// <para><b>Both exclude the class's nested classes</b>, which each appear as a placeholder carrying
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
public sealed record ClassSignature(string FullName, string Semantic, string Surface);

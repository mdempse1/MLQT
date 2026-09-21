namespace ModelicaParser.Comparison;

/// <summary>
/// Compares two versions of a Modelica file class by class, and says what each difference amounts to.
/// </summary>
/// <remarks>
/// <para><b>The single answer to "what kind of change is this?" (B191).</b> Every surface that shows
/// a change marker asks here, so the library browser's marker, its filter and anything added later
/// cannot come to different conclusions about the same edit.</para>
///
/// <para><b>Why a whole file at a time.</b> A class's identity is its full Modelica name, and that
/// name comes from the file's <c>within</c> clause and its enclosing classes — neither of which a
/// single class's source carries. Comparing per file also parses each version once rather than once
/// per class, which for a package of a few hundred nested classes is the difference between one
/// parse and a few hundred.</para>
/// </remarks>
public static class ClassChangeClassifier
{
    /// <summary>
    /// What changed in each class of <paramref name="workingText"/>, against <paramref name="committedText"/>.
    /// </summary>
    /// <param name="committedText">
    /// The committed version's text, or null when there is none — a new file, an untracked one, or a
    /// repository with no version control at all. Every class then reads as
    /// <see cref="ClassChangeKind.Added"/>.
    /// </param>
    /// <param name="workingText">The working copy's text. Null or unparseable yields an empty result.</param>
    /// <returns>
    /// One entry per class in the working copy, by full Modelica name — the same string a
    /// <c>ModelNode.Id</c> carries. Classes that exist only in the committed version are not
    /// reported: they have nothing left in the tree to mark.
    /// </returns>
    public static IReadOnlyDictionary<string, ClassChangeKind> Compare(string? committedText, string? workingText)
        => Compare(ClassSignatures.Of(committedText), ClassSignatures.Of(workingText));

    /// <summary>
    /// The same comparison over signatures already computed — for a caller holding one version's
    /// signatures across several comparisons.
    /// </summary>
    public static IReadOnlyDictionary<string, ClassChangeKind> Compare(
        ClassSignatures committed, ClassSignatures working)
    {
        var result = new Dictionary<string, ClassChangeKind>(working.Classes.Count, StringComparer.Ordinal);

        // Nothing is claimed about a version that could not be read. Reporting every class of an
        // unreadable committed version as Added would be worse than saying so: a file with a syntax
        // error in it is exactly when a reviewer is relying on the marker.
        var known = committed.Parsed && working.Parsed;

        foreach (var (fullName, after) in working.Classes)
        {
            if (!known)
            {
                result[fullName] = ClassChangeKind.Unknown;
                continue;
            }

            result[fullName] = committed.Classes.TryGetValue(fullName, out var before)
                ? Compare(before, after)
                : ClassChangeKind.Added;
        }

        return result;
    }

    /// <summary>
    /// What the difference between two versions of one class amounts to.
    /// </summary>
    /// <remarks>
    /// Meaning is asked first and text second, so a class that was reformatted, re-worded or redrawn
    /// is <see cref="ClassChangeKind.Cosmetic"/> and one that was not touched at all is
    /// <see cref="ClassChangeKind.Unchanged"/>. Both comparisons exclude the class's nested classes,
    /// which carry their own answers.
    /// </remarks>
    public static ClassChangeKind Compare(ClassSignature before, ClassSignature after)
    {
        if (!string.Equals(before.Semantic, after.Semantic, StringComparison.Ordinal))
            return ClassChangeKind.AffectsSimulation;

        return string.Equals(before.Surface, after.Surface, StringComparison.Ordinal)
            ? ClassChangeKind.Unchanged
            : ClassChangeKind.Cosmetic;
    }
}

using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;

namespace MLQT.Services.Checking;

/// <summary>
/// What accepting a spelling covers: which word is recorded, and which findings it clears.
/// </summary>
/// <remarks>
/// <para>Backlog B119, the remainder of B20 for <c>CodeReview</c>. Accepting a word from the Code
/// Review page has three parts — write it to the repository's dictionary, work out which findings it
/// has just answered, and remove those. Only the first was anywhere testable.</para>
///
/// <para><b>Scope is the whole point of the feature</b>, and it is where getting it wrong is
/// invisible: the accepted-words list is per repository, committed with the code, so accepting a term
/// in one library must not silence the same word in another team's. The page had that rule inline as
/// a set intersection no test could reach.</para>
/// </remarks>
public static class SpellingAcceptance
{
    /// <summary>
    /// The word to record for an accepted one: the base of a possessive, else the word itself.
    /// </summary>
    /// <remarks>
    /// The checker already accepts "Stodola's" once "Stodola" is accepted, so recording the
    /// possessive would put a form in the list that is derived anyway — and the list is a file the
    /// team reads and reviews in a diff.
    /// </remarks>
    public static string WordToRecord(string word) => SpellChecker.PossessiveBaseOf(word) ?? word;

    /// <summary>
    /// Whether a flagged word is now covered by <paramref name="acceptedWord"/>.
    /// </summary>
    /// <remarks>
    /// Case is ignored because the word list is: a word accepted in one casing is accepted in any,
    /// and a user who accepts "Pacejka" does not expect "pacejka" to stay flagged.
    /// </remarks>
    public static bool Covers(string acceptedWord, string? flagged) =>
        flagged is not null
        && string.Equals(WordToRecord(flagged), acceptedWord, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this finding is one the spell checker raised.</summary>
    public static bool IsSpellingFinding(LogMessage? finding) => SpellingMessage.Is(finding?.Summary);

    /// <summary>The word the rule flagged, or null when this is not a spelling finding.</summary>
    /// <remarks>
    /// Read through the shared message format rather than from the finding's <c>Discriminator</c>:
    /// that field exists to make the fingerprint unique, and for a documentation finding it carries
    /// the section as well as the word ("documentation info:tyre"). Using it underlined nothing,
    /// because no such text appears in the source.
    /// </remarks>
    public static string? WordOf(LogMessage? finding) => SpellingMessage.WordFrom(finding?.Summary);

    /// <summary>
    /// A predicate for the findings an accepted word answers, within the classes it applies to.
    /// </summary>
    /// <param name="acceptedWord">From <see cref="WordToRecord"/>.</param>
    /// <param name="modelsInScope">
    /// The classes belonging to the repository whose dictionary accepted it. <b>Required</b>: without
    /// it the word would be cleared everywhere on screen, including in libraries that have not
    /// accepted it and where the next check reports it again.
    /// </param>
    public static Func<LogMessage, bool> ClearedBy(string acceptedWord, IReadOnlySet<string> modelsInScope) =>
        finding => modelsInScope.Contains(finding.ModelName)
                   && IsSpellingFinding(finding)
                   && Covers(acceptedWord, WordOf(finding));

    /// <summary>
    /// The same, for a word accepted in one class through an <c>__MLQT(spelling=…)</c> annotation.
    /// </summary>
    /// <remarks>
    /// The narrower of the two waivers the page offers, and the reason they share this code: the word
    /// and the matching are the same question, only the set of classes differs. Written into the
    /// class's own source, so every other class still reports the word — which is what sits between
    /// suppressing the rule for the class and accepting the word for the whole repository.
    /// </remarks>
    public static Func<LogMessage, bool> ClearedInClass(string acceptedWord, string modelId) =>
        ClearedBy(acceptedWord, new HashSet<string>(StringComparer.Ordinal) { modelId });
}

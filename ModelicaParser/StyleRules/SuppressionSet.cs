using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// The <c>__MLQT</c> suppression directives extracted from a model: which rules are waived at the
/// class level and per component, which words the class accepts as spelled correctly
/// (<c>spelling</c>), plus which classes opt out of formatting/reordering (<c>preserveOrder</c> /
/// <c>format=false</c>). Keyed by the fully qualified model name so it lines up with a
/// <see cref="Finding.ModelId"/>.
///
/// <para><b>A class-level <c>suppress</c> or <c>spelling</c> reaches every class nested inside the one
/// that carries it</b>, so one annotation on a package waives a rule for the whole sub-package — the
/// Dymola-generated <c>_fmu</c> import models were ~65% of a Claytex check's findings, and the
/// alternative was an annotation on every class. A set holds the directives of the classes in one
/// parse tree, so it answers for what is nested <em>in that tree</em>; a class in a file of its own
/// reaches its enclosing packages' sets through <c>ClassSuppressions.Enclosing</c>, and a finding is
/// waived when any of them waives it.</para>
///
/// <para>Two directives stay with the class they are written on. A component-level <c>suppress</c>
/// is about one declaration. <c>preserveOrder</c>/<c>format=false</c> is a decision about how the
/// formatter writes that class, and the formatter still writes a nested class in a file of its own
/// normally — waiving its layout rules there would stop reporting a layout the formatter goes on to
/// change.</para>
/// </summary>
public sealed class SuppressionSet
{
    // Everything waived for the class itself: what `suppress` names, and the layout rules
    // `preserveOrder`/`format=false` waive.
    private readonly Dictionary<string, HashSet<string>> _classLevel;
    // What `suppress` names at the class level, and nothing else: the part that reaches nested classes.
    private readonly Dictionary<string, HashSet<string>> _inherited;
    private readonly Dictionary<(string Model, string Component), HashSet<string>> _componentLevel;
    private readonly HashSet<string> _preserveFormatting;
    private readonly Dictionary<string, HashSet<string>> _spellingWords;
    private readonly Dictionary<string, string> _reasons;

    public static readonly SuppressionSet Empty = new(new(), new(), new(), new(), new(), new());

    internal SuppressionSet(
        Dictionary<string, HashSet<string>> classLevel,
        Dictionary<string, HashSet<string>> inherited,
        Dictionary<(string, string), HashSet<string>> componentLevel,
        HashSet<string> preserveFormatting,
        Dictionary<string, HashSet<string>> spellingWords,
        Dictionary<string, string> reasons)
    {
        _reasons = reasons;
        _classLevel = classLevel;
        _inherited = inherited;
        _componentLevel = componentLevel;
        _preserveFormatting = preserveFormatting;
        _spellingWords = spellingWords;
    }

    public bool IsEmpty =>
        _classLevel.Count == 0 && _inherited.Count == 0 && _componentLevel.Count == 0 && _preserveFormatting.Count == 0
        && _spellingWords.Count == 0;

    /// <summary>
    /// True if this finding is suppressed by a directive on its own class or component, or by a
    /// class-level <c>suppress</c> on a class it is nested in.
    /// </summary>
    public bool IsSuppressed(Finding finding)
    {
        if (_classLevel.TryGetValue(finding.ModelId, out var classTokens) && Matches(classTokens, finding.RuleId))
            return true;

        if (_inherited.Count > 0)
            foreach (var enclosing in EnclosingClassesOf(finding.ModelId))
                if (_inherited.TryGetValue(enclosing, out var tokens) && Matches(tokens, finding.RuleId))
                    return true;

        if (finding.ElementPath is not null &&
            _componentLevel.TryGetValue((finding.ModelId, finding.ElementPath), out var componentTokens) &&
            Matches(componentTokens, finding.RuleId))
            return true;

        return IsAcceptedSpelling(finding);
    }

    /// <summary>
    /// True if this is a spelling finding for a word the class accepts through
    /// <c>__MLQT(spelling="…")</c>.
    ///
    /// <para>Word-scoped rather than rule-scoped, because the alternative — suppressing
    /// <c>MLQT.Spelling.Description</c> for the class — silences every other misspelling in it too.
    /// The word comes from the message rather than the finding's discriminator: the discriminator
    /// carries the section as well as the word for a documentation finding, and its exact shape is
    /// part of the fingerprint that baselines are keyed on.</para>
    /// </summary>
    private bool IsAcceptedSpelling(Finding finding)
    {
        if (finding.RuleId is not (RuleIds.SpellingDescription or RuleIds.SpellingDocumentation))
            return false;

        if (_spellingWords.Count == 0)
            return false;

        var word = SpellingMessage.WordFrom(finding.Message);
        if (word is null)
            return false;

        // The possessive of an accepted word is accepted too, exactly as the spell checker and the
        // repository word list treat one.
        var possessiveBase = SpellChecker.PossessiveBaseOf(word);

        // The class's own words, then every enclosing class's: a word accepted on a package is
        // accepted throughout it.
        foreach (var scope in EnclosingClassesOf(finding.ModelId).Prepend(finding.ModelId))
            if (_spellingWords.TryGetValue(scope, out var words)
                && (words.Contains(word) || (possessiveBase is not null && words.Contains(possessiveBase))))
                return true;

        return false;
    }

    /// <summary>The classes <paramref name="modelId"/> is nested in, innermost first.</summary>
    private static IEnumerable<string> EnclosingClassesOf(string modelId)
    {
        for (var enclosing = ModelicaName.EnclosingPackageOf(modelId);
             enclosing.Length > 0;
             enclosing = ModelicaName.EnclosingPackageOf(enclosing))
            yield return enclosing;
    }

    /// <summary>
    /// The entries of the class-level <c>suppress</c> list written on <paramref name="modelId"/>, as
    /// written — a full rule id, a short one, or <c>*</c>. Empty when it carries none. Not the rules
    /// <c>preserveOrder</c>/<c>format=false</c> waive, which are not in any list the user wrote.
    /// </summary>
    public IReadOnlyCollection<string> SuppressListOf(string modelId) =>
        _inherited.TryGetValue(modelId, out var tokens) ? tokens : [];

    /// <summary>The class-level <c>reason</c> written on <paramref name="modelId"/>, or null.</summary>
    public string? ReasonFor(string modelId) => _reasons.GetValueOrDefault(modelId);

    /// <summary>
    /// Whether a <c>suppress</c> list entry names <paramref name="ruleId"/>: the id itself, the id
    /// without its <c>MLQT.</c> prefix, or the wildcard <c>*</c>.
    /// </summary>
    public static bool Names(string token, string ruleId) => Matches([token], ruleId);

    /// <summary>True if the class opted out of formatting/reordering (<c>preserveOrder</c> / <c>format=false</c>).</summary>
    public bool PreservesFormatting(string modelId) => _preserveFormatting.Contains(modelId);

    /// <summary>True if any class in the extracted definition opted out of formatting/reordering.</summary>
    public bool HasFormattingOptOut => _preserveFormatting.Count > 0;

    // A token matches a rule id if it equals it, equals it minus the "MLQT." prefix
    // (so "Naming.Convention" and "MLQT.Naming.Convention" both work), or is the wildcard "*".
    private static bool Matches(IReadOnlyCollection<string> tokens, string ruleId)
    {
        if (tokens.Contains("*") || tokens.Contains(ruleId))
            return true;

        const string prefix = "MLQT.";
        return ruleId.StartsWith(prefix, StringComparison.Ordinal) && tokens.Contains(ruleId[prefix.Length..]);
    }
}

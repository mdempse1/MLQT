using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// The <c>__MLQT</c> suppression directives extracted from a model: which rules are waived at the
/// class level and per component, which words the class accepts as spelled correctly
/// (<c>spelling</c>), plus which classes opt out of formatting/reordering (<c>preserveOrder</c> /
/// <c>format=false</c>). Keyed by the fully qualified model name so it lines up with a
/// <see cref="Finding.ModelId"/>.
/// </summary>
public sealed class SuppressionSet
{
    private readonly Dictionary<string, HashSet<string>> _classLevel;
    private readonly Dictionary<(string Model, string Component), HashSet<string>> _componentLevel;
    private readonly HashSet<string> _preserveFormatting;
    private readonly Dictionary<string, HashSet<string>> _spellingWords;

    public static readonly SuppressionSet Empty = new(new(), new(), new(), new());

    internal SuppressionSet(
        Dictionary<string, HashSet<string>> classLevel,
        Dictionary<(string, string), HashSet<string>> componentLevel,
        HashSet<string> preserveFormatting,
        Dictionary<string, HashSet<string>> spellingWords)
    {
        _classLevel = classLevel;
        _componentLevel = componentLevel;
        _preserveFormatting = preserveFormatting;
        _spellingWords = spellingWords;
    }

    public bool IsEmpty =>
        _classLevel.Count == 0 && _componentLevel.Count == 0 && _preserveFormatting.Count == 0
        && _spellingWords.Count == 0;

    /// <summary>True if this finding is suppressed by a class- or component-level directive.</summary>
    public bool IsSuppressed(Finding finding)
    {
        if (_classLevel.TryGetValue(finding.ModelId, out var classTokens) && Matches(classTokens, finding.RuleId))
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

        if (!_spellingWords.TryGetValue(finding.ModelId, out var words) || words.Count == 0)
            return false;

        var word = SpellingMessage.WordFrom(finding.Message);
        if (word is null)
            return false;

        // The possessive of an accepted word is accepted too, exactly as the spell checker and the
        // repository word list treat one.
        return words.Contains(word)
            || (SpellChecker.PossessiveBaseOf(word) is { } possessiveBase && words.Contains(possessiveBase));
    }

    /// <summary>True if the class opted out of formatting/reordering (<c>preserveOrder</c> / <c>format=false</c>).</summary>
    public bool PreservesFormatting(string modelId) => _preserveFormatting.Contains(modelId);

    /// <summary>True if any class in the extracted definition opted out of formatting/reordering.</summary>
    public bool HasFormattingOptOut => _preserveFormatting.Count > 0;

    // A token matches a rule id if it equals it, equals it minus the "MLQT." prefix
    // (so "Naming.Convention" and "MLQT.Naming.Convention" both work), or is the wildcard "*".
    private static bool Matches(HashSet<string> tokens, string ruleId)
    {
        if (tokens.Contains("*") || tokens.Contains(ruleId))
            return true;

        const string prefix = "MLQT.";
        return ruleId.StartsWith(prefix, StringComparison.Ordinal) && tokens.Contains(ruleId[prefix.Length..]);
    }
}

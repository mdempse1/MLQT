using System.Reflection;
using WeCantSpell.Hunspell;

namespace ModelicaParser.SpellChecking;

/// <summary>
/// Represents a Hunspell dictionary loaded from file paths.
/// </summary>
public record DictionarySource(string AffixFilePath, string DictionaryFilePath);

/// <summary>
/// Thread-safe spell checker backed by Hunspell dictionaries.
/// Supports multiple language dictionaries, a custom user word list,
/// and per-call context words (e.g. model-scoped component names).
/// </summary>
public class SpellChecker
{
    private readonly List<WordList> _dictionaries;
    private readonly HashSet<string> _customWords;
    private readonly object _customWordsLock = new();

    private SpellChecker(List<WordList> dictionaries, HashSet<string> customWords)
    {
        _dictionaries = dictionaries;
        _customWords = customWords;
    }

    /// <summary>
    /// Returns the language codes for the bundled embedded dictionaries.
    /// </summary>
    public static IReadOnlyList<string> BundledLanguageCodes => ["en_US", "en_GB"];

    /// <summary>
    /// Creates a SpellChecker with the specified language dictionaries and custom words.
    /// Dictionaries are loaded from embedded resources and/or file paths.
    /// </summary>
    /// <param name="languageCodes">Language codes to load from embedded resources (e.g. "en_US", "en_GB"). Defaults to both.</param>
    /// <param name="customWords">Additional custom words to accept as correct.</param>
    /// <param name="additionalDictionaries">Additional Hunspell dictionaries loaded from file paths.</param>
    public static SpellChecker Create(
        IEnumerable<string>? languageCodes = null,
        IEnumerable<string>? customWords = null,
        IEnumerable<DictionarySource>? additionalDictionaries = null)
    {
        var codes = languageCodes?.ToList() ?? ["en_US", "en_GB"];
        var dictionaries = new List<WordList>();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var code in codes)
        {
            var affResourceName = $"ModelicaParser.SpellChecking.Dictionaries.{code}.aff";
            var dicResourceName = $"ModelicaParser.SpellChecking.Dictionaries.{code}.dic";

            using var affStream = assembly.GetManifestResourceStream(affResourceName);
            using var dicStream = assembly.GetManifestResourceStream(dicResourceName);

            if (affStream != null && dicStream != null)
            {
                var wordList = WordList.CreateFromStreams(dicStream, affStream);
                dictionaries.Add(wordList);
            }
        }

        // Load additional dictionaries from file paths
        if (additionalDictionaries != null)
        {
            foreach (var source in additionalDictionaries)
            {
                if (File.Exists(source.AffixFilePath) && File.Exists(source.DictionaryFilePath))
                {
                    var wordList = WordList.CreateFromFiles(source.DictionaryFilePath, source.AffixFilePath);
                    dictionaries.Add(wordList);
                }
            }
        }

        // Build custom words set (case-insensitive)
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (customWords != null)
        {
            foreach (var word in customWords)
            {
                if (!string.IsNullOrWhiteSpace(word))
                    words.Add(word.Trim());
            }
        }

        // Load built-in Modelica terms
        var termsResourceName = "ModelicaParser.SpellChecking.Dictionaries.modelica_terms.txt";
        using var termsStream = assembly.GetManifestResourceStream(termsResourceName);
        if (termsStream != null)
        {
            using var reader = new StreamReader(termsStream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (!string.IsNullOrEmpty(line))
                    words.Add(line);
            }
        }

        return new SpellChecker(dictionaries, words);
    }

    /// <summary>
    /// Checks whether a word is spelled correctly against all loaded dictionaries,
    /// custom words, and optional context words.
    ///
    /// <para>The possessive of an accepted word is accepted too. Hunspell dictionaries carry no
    /// possessive forms, and neither does a hand-written word list, so without this every
    /// "Stodola's" is reported while every "Stodola" beside it is fine — which reads as the word
    /// list not being used at all.</para>
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <param name="contextWords">Optional per-call context words (e.g. component names in scope).</param>
    /// <returns>True if the word is found in any dictionary, custom words, or context words.</returns>
    public bool IsCorrect(string word, IReadOnlySet<string>? contextWords = null)
    {
        if (string.IsNullOrWhiteSpace(word))
            return true;

        if (IsKnown(word, contextWords))
            return true;

        var possessed = PossessiveBaseOf(word);
        return possessed is not null && IsKnown(possessed, contextWords);
    }

    /// <summary>The word itself, against context words, custom words and the dictionaries.</summary>
    private bool IsKnown(string word, IReadOnlySet<string>? contextWords)
    {
        // Check context words first (cheapest check)
        if (contextWords != null && contextWords.Contains(word))
            return true;

        // Check custom words
        lock (_customWordsLock)
        {
            if (_customWords.Contains(word))
                return true;
        }

        // Check each Hunspell dictionary
        foreach (var dict in _dictionaries)
        {
            if (dict.Check(word))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The word a possessive belongs to ("Stodola's" -> "Stodola"), or null if this is not one.
    /// Both the typewriter apostrophe and the typographic one are recognised, because documentation
    /// prose carries either. A trailing bare apostrophe ("Jones'") never reaches here — the tokenizer
    /// trims it — so only the "'s" form is handled.
    ///
    /// <para>Public because anything recording an accepted word needs the same rule: accepting the
    /// possessive would put a form in the list that <see cref="IsCorrect"/> already derives, and the
    /// list is a file the team reads.</para>
    /// </summary>
    public static string? PossessiveBaseOf(string word) =>
        word.Length > 2 && (word[^1] == 's' || word[^1] == 'S') && (word[^2] == '\'' || word[^2] == '\u2019')
            ? word[..^2]
            : null;

    /// <summary>
    /// Returns spelling suggestions for a misspelled word: near matches among the accepted words
    /// first, then whatever the language dictionaries offer.
    ///
    /// <para>The accepted words come first because they are the likelier intent. A term someone took
    /// the trouble to accept for this repository is part of its vocabulary — mistype "Pacejka" as
    /// "Pacjeka" and no English dictionary has anything useful to say, while the repository has the
    /// exact word one transposition away and used to keep it to itself.</para>
    /// </summary>
    public IReadOnlyList<string> Suggest(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return [];

        var accepted = NearbyAcceptedWords(word);

        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in _dictionaries)
        {
            foreach (var suggestion in dict.Suggest(word))
            {
                suggestions.Add(suggestion);
            }
        }

        if (accepted.Count > 0)
        {
            suggestions.ExceptWith(accepted);   // an accepted word is offered once, in its own casing
            return [.. accepted, .. suggestions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];
        }

        return suggestions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Accepted words within a typo's distance of <paramref name="word"/>, closest first. Bounded by
    /// length so a long list of accepted terms costs a length comparison for most of them.
    /// </summary>
    private List<string> NearbyAcceptedWords(string word)
    {
        // One edit for a short word, two for anything longer: enough for the ordinary typo, and tight
        // enough that a repository's vocabulary does not start answering for unrelated words.
        var limit = word.Length <= 4 ? 1 : 2;
        var hits = new List<(string Word, int Distance)>();

        lock (_customWordsLock)
        {
            foreach (var candidate in _customWords)
            {
                if (Math.Abs(candidate.Length - word.Length) > limit)
                    continue;
                if (string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase))
                    continue;   // not a suggestion: the word is already accepted

                var distance = EditDistance(word, candidate, limit);
                if (distance >= 0)
                    hits.Add((candidate, distance));
            }
        }

        return hits
            .OrderBy(h => h.Distance)
            .ThenBy(h => h.Word, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Word)
            .ToList();
    }

    /// <summary>
    /// Case-insensitive edit distance counting a swap of two neighbouring letters as one edit, which
    /// is what "Pacjeka" for "Pacejka" is — and what plain insert/delete/substitute counts as two.
    /// Returns -1 once the distance is past <paramref name="limit"/>, so a long word list is walked
    /// without scoring candidates that cannot qualify.
    /// </summary>
    private static int EditDistance(string a, string b, int limit)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        var beforePrevious = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var best = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var same = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]);
                var cost = same ? 0 : 1;

                var value = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                if (i > 1 && j > 1
                    && char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 2])
                    && char.ToUpperInvariant(a[i - 2]) == char.ToUpperInvariant(b[j - 1]))
                {
                    value = Math.Min(value, beforePrevious[j - 2] + 1);
                }

                current[j] = value;
                best = Math.Min(best, value);
            }

            if (best > limit)
                return -1;   // every path through this row is already too far

            (beforePrevious, previous, current) = (previous, current, beforePrevious);
        }

        var distance = previous[b.Length];
        return distance <= limit ? distance : -1;
    }

    /// <summary>
    /// Adds a word to the in-memory custom word set. Thread-safe.
    /// </summary>
    public void AddCustomWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        lock (_customWordsLock)
        {
            _customWords.Add(word.Trim());
        }
    }

    /// <summary>
    /// Returns a snapshot of the current custom words.
    /// </summary>
    public IReadOnlyCollection<string> CustomWords
    {
        get
        {
            lock (_customWordsLock)
            {
                return _customWords.ToList().AsReadOnly();
            }
        }
    }
}

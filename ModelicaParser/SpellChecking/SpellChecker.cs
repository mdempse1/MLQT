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
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <param name="contextWords">Optional per-call context words (e.g. component names in scope).</param>
    /// <returns>True if the word is found in any dictionary, custom words, or context words.</returns>
    public bool IsCorrect(string word, IReadOnlySet<string>? contextWords = null)
    {
        if (string.IsNullOrWhiteSpace(word))
            return true;

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
    /// Returns spelling suggestions for a misspelled word from all loaded dictionaries.
    /// </summary>
    public IReadOnlyList<string> Suggest(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return [];

        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dict in _dictionaries)
        {
            foreach (var suggestion in dict.Suggest(word))
            {
                suggestions.Add(suggestion);
            }
        }

        return suggestions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
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

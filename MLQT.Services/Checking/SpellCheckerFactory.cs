using ModelicaParser.SpellChecking;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Builds a <see cref="SpellChecker"/> for a chosen set of dictionary languages, mirroring
/// StyleCheckingService's construction: bundled languages (en_US/en_GB) load directly, other codes
/// resolve to imported Hunspell dictionaries, and the user's custom words are always included.
/// This is what lets the spell-check language actually take effect (the service interface only
/// exposes the default-language checker).
/// </summary>
public static class SpellCheckerFactory
{
    /// <param name="customWords">The accepted words for the library being checked, which belong to
    /// its repository. Passed in rather than fetched here so every call site has to say whose words
    /// these are — the checker is only identical between the app and the CLI if they agree on
    /// that.</param>
    public static SpellChecker Build(
        IEnumerable<string>? languages,
        IReadOnlyCollection<string> customWords,
        IDictionaryManagerService dictionaryManager)
    {
        var codes = languages?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (codes is null || codes.Count == 0)
            codes = ["en_US", "en_GB"];

        var bundled = new List<string>();
        var imported = new List<DictionarySource>();
        foreach (var code in codes)
        {
            var source = dictionaryManager.GetImportedDictionaryPaths(code);
            if (source is not null)
                imported.Add(source);
            else
                bundled.Add(code);
        }

        return SpellChecker.Create(
            languageCodes: bundled,
            customWords: customWords,
            additionalDictionaries: imported.Count > 0 ? imported : null);
    }
}

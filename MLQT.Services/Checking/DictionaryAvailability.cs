using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Whether the machine actually has the spell-check dictionaries a repository's settings ask for.
///
/// <para>The languages are named in <c>.mlqt/settings.json</c>, which is committed, but the
/// dictionaries themselves are installed per machine. A build agent, or a colleague's laptop, can
/// easily lack one — and then those words are checked against whatever languages remain, or against
/// nothing at all if that was the only one. Either way the findings are not the ones the settings
/// describe, and nothing about them says so. Every surface asks here so they all say the same thing
/// about the same gap.</para>
/// </summary>
public static class DictionaryAvailability
{
    /// <summary>The requested languages this machine has no dictionary for, in the order asked for.</summary>
    public static IReadOnlyList<string> MissingLanguages(
        IEnumerable<string>? requested, IDictionaryManagerService dictionaryManager)
    {
        var wanted = requested?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (wanted is null || wanted.Count == 0)
            return [];   // no choice made means the bundled dictionaries, which are always present

        var available = dictionaryManager.GetAvailableDictionaries()
            .Select(d => d.LanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return wanted.Where(code => !available.Contains(code)).ToList();
    }

    /// <summary>
    /// A sentence naming what is missing and what it costs, or null when nothing is. Says which way
    /// the results are wrong rather than only that something is absent: with one language gone the
    /// remaining ones judge its words, and with all of them gone every word is a misspelling.
    /// </summary>
    public static string? WarningFor(
        IEnumerable<string>? requested, IDictionaryManagerService dictionaryManager)
    {
        var missing = MissingLanguages(requested, dictionaryManager);
        if (missing.Count == 0)
            return null;

        var names = string.Join(", ", missing);
        var wanted = requested!.Where(c => !string.IsNullOrWhiteSpace(c)).Count();

        return missing.Count == wanted
            ? $"no spell-check dictionary is installed for {names}, which is every language the " +
              "settings ask for; with none loaded every word is reported as misspelled"
            : $"no spell-check dictionary is installed for {names}; those words are checked against " +
              "the remaining languages, so the spelling findings will not match a machine that has them";
    }
}

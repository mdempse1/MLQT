using ModelicaParser.SpellChecking;
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
    /// <summary>
    /// The code of the dictionary here that <paramref name="requested"/> names, or null when there is
    /// none. <c>en-GB</c>, <c>en_gb</c> and <c>en_UK</c> all name the bundled <c>en_GB</c>
    /// (<see cref="SpellChecker.NormaliseLanguageCode"/>), and an imported dictionary is matched
    /// whatever the case. The one answer to "which dictionary is this?": the warning and the checker
    /// both ask it, so a language the warning lets through is one the checker loads (B529).
    /// </summary>
    public static string? Resolve(string requested, IDictionaryManagerService dictionaryManager)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(dictionaryManager);
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        // Both sides normalised: an imported dictionary is named after its file, and Hunspell's own
        // names carry hyphens - en_GB-ise - which a request normalised alone (en_GB_ise) never matched.
        // The code returned is the dictionary's own, which is what its files are found by.
        var code = SpellChecker.NormaliseLanguageCode(requested);
        var available = dictionaryManager.GetAvailableDictionaries().Select(d => d.LanguageCode).ToList();
        return available.FirstOrDefault(a => string.Equals(a, requested.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? available.FirstOrDefault(a =>
                   string.Equals(SpellChecker.NormaliseLanguageCode(a), code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The requested languages this machine has no dictionary for, in the order asked for.</summary>
    public static IReadOnlyList<string> MissingLanguages(
        IEnumerable<string>? requested, IDictionaryManagerService dictionaryManager)
    {
        var wanted = requested?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (wanted is null || wanted.Count == 0)
            return [];   // no choice made means the bundled dictionaries, which are always present

        return wanted.Where(code => Resolve(code, dictionaryManager) is null).ToList();
    }

    /// <summary>
    /// A sentence naming what is missing and what it costs, or null when nothing is. Says which way
    /// the results are wrong rather than only that something is absent: with one language gone the
    /// remaining ones judge its words, and with all of them gone every word is a misspelling. Lists
    /// the dictionaries there are, because a name that matches none is far more often a misspelt
    /// code than a dictionary the machine lacks.
    /// </summary>
    public static string? WarningFor(
        IEnumerable<string>? requested, IDictionaryManagerService dictionaryManager)
    {
        var missing = MissingLanguages(requested, dictionaryManager);
        if (missing.Count == 0)
            return null;

        var names = string.Join(", ", missing);
        var wanted = requested!.Where(c => !string.IsNullOrWhiteSpace(c)).Count();
        var here = $"the dictionaries here are {AvailableCodes(dictionaryManager)}";

        return missing.Count == wanted
            ? $"no spell-check dictionary matches {names}, which is every language the settings ask " +
              $"for, so with none loaded every word is reported as misspelled; {here}"
            : $"no spell-check dictionary matches {names}; those words are checked against the " +
              $"remaining languages, so the spelling findings will not match a machine that has them; {here}";
    }

    /// <summary>The codes of the dictionaries this machine has, as a settings file names them.</summary>
    public static string AvailableCodes(IDictionaryManagerService dictionaryManager)
    {
        ArgumentNullException.ThrowIfNull(dictionaryManager);
        return string.Join(", ", dictionaryManager.GetAvailableDictionaries()
            .Select(d => d.LanguageCode)
            .Order(StringComparer.Ordinal));
    }
}

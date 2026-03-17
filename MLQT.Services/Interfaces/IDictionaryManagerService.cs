using ModelicaParser.SpellChecking;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Describes an available Hunspell dictionary.
/// </summary>
public record DictionaryInfo(string LanguageCode, string DisplayName, bool IsBundled);

/// <summary>
/// Manages available Hunspell dictionaries — both bundled (embedded in ModelicaParser)
/// and user-imported (stored at %LocalAppData%/MLQT/Dictionaries/).
/// </summary>
public interface IDictionaryManagerService
{
    /// <summary>
    /// Returns all available dictionaries (bundled + imported).
    /// </summary>
    IReadOnlyList<DictionaryInfo> GetAvailableDictionaries();

    /// <summary>
    /// Imports a Hunspell dictionary pair (.aff + .dic) into the user profile directory.
    /// Returns the language code derived from the file name, or null on failure.
    /// </summary>
    Task<string?> ImportDictionaryAsync(string affFilePath, string dicFilePath);

    /// <summary>
    /// Removes an imported dictionary by language code.
    /// Returns true if successfully removed.
    /// </summary>
    Task<bool> RemoveImportedDictionaryAsync(string languageCode);

    /// <summary>
    /// Returns file paths for an imported dictionary, or null if it's bundled or not found.
    /// </summary>
    DictionarySource? GetImportedDictionaryPaths(string languageCode);

    /// <summary>
    /// Fired when available dictionaries change (import/remove).
    /// </summary>
    event Action? OnDictionariesChanged;
}

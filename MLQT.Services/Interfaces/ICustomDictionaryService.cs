namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for managing a user's custom spell-checking dictionary.
/// Words are stored at %LocalAppData%/MLQT/custom_dictionary.txt, shared across all repositories.
/// </summary>
public interface ICustomDictionaryService
{
    /// <summary>
    /// Gets all custom words currently loaded.
    /// </summary>
    IReadOnlyCollection<string> CustomWords { get; }

    /// <summary>
    /// Adds a word to the custom dictionary and persists it.
    /// </summary>
    Task AddWordAsync(string word);

    /// <summary>
    /// Removes a word from the custom dictionary and persists it.
    /// </summary>
    Task RemoveWordAsync(string word);

    /// <summary>
    /// Replaces the current dictionary with words imported from a file.
    /// </summary>
    Task ImportAsync(string filePath);

    /// <summary>
    /// Writes the current dictionary to a file.
    /// </summary>
    Task ExportAsync(string filePath);

    /// <summary>
    /// Merges words from a file into the current dictionary.
    /// </summary>
    Task MergeAsync(string filePath);

    /// <summary>
    /// Loads the dictionary from disk. Called once at startup.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Fired when the dictionary changes (add, remove, import, merge).
    /// </summary>
    event Action? OnDictionaryChanged;
}

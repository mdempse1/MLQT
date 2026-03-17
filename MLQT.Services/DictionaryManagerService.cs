using ModelicaParser.SpellChecking;
using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Manages Hunspell dictionaries: lists bundled ones and handles importing/removing
/// user dictionaries stored at %LocalAppData%/MLQT/Dictionaries/.
/// </summary>
public class DictionaryManagerService : IDictionaryManagerService
{
    private static readonly Dictionary<string, string> BundledDictionaries = new()
    {
        ["en_US"] = "English (US)",
        ["en_GB"] = "English (UK)"
    };

    private readonly string _dictionaryDir;
    private readonly Dictionary<string, string> _importedDictionaries = new();

    public event Action? OnDictionariesChanged;

    public DictionaryManagerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dictionaryDir = Path.Combine(appData, "MLQT", "Dictionaries");
        ScanImportedDictionaries();
    }

    /// <summary>
    /// Constructor for testing with a custom directory path.
    /// </summary>
    internal DictionaryManagerService(string dictionaryDir)
    {
        _dictionaryDir = dictionaryDir;
        ScanImportedDictionaries();
    }

    public IReadOnlyList<DictionaryInfo> GetAvailableDictionaries()
    {
        var result = new List<DictionaryInfo>();

        foreach (var kvp in BundledDictionaries)
        {
            result.Add(new DictionaryInfo(kvp.Key, kvp.Value, IsBundled: true));
        }

        foreach (var kvp in _importedDictionaries)
        {
            result.Add(new DictionaryInfo(kvp.Key, kvp.Value, IsBundled: false));
        }

        return result.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<string?> ImportDictionaryAsync(string affFilePath, string dicFilePath)
    {
        if (!File.Exists(affFilePath) || !File.Exists(dicFilePath))
            return null;

        var langCode = Path.GetFileNameWithoutExtension(affFilePath);
        if (string.IsNullOrWhiteSpace(langCode))
            return null;

        Directory.CreateDirectory(_dictionaryDir);

        var destAff = Path.Combine(_dictionaryDir, $"{langCode}.aff");
        var destDic = Path.Combine(_dictionaryDir, $"{langCode}.dic");

        await CopyFileAsync(affFilePath, destAff);
        await CopyFileAsync(dicFilePath, destDic);

        var displayName = FormatDisplayName(langCode);
        _importedDictionaries[langCode] = displayName;

        OnDictionariesChanged?.Invoke();
        return langCode;
    }

    public Task<bool> RemoveImportedDictionaryAsync(string languageCode)
    {
        if (!_importedDictionaries.ContainsKey(languageCode))
            return Task.FromResult(false);

        var affPath = Path.Combine(_dictionaryDir, $"{languageCode}.aff");
        var dicPath = Path.Combine(_dictionaryDir, $"{languageCode}.dic");

        if (File.Exists(affPath)) File.Delete(affPath);
        if (File.Exists(dicPath)) File.Delete(dicPath);

        _importedDictionaries.Remove(languageCode);

        OnDictionariesChanged?.Invoke();
        return Task.FromResult(true);
    }

    public DictionarySource? GetImportedDictionaryPaths(string languageCode)
    {
        if (!_importedDictionaries.ContainsKey(languageCode))
            return null;

        var affPath = Path.Combine(_dictionaryDir, $"{languageCode}.aff");
        var dicPath = Path.Combine(_dictionaryDir, $"{languageCode}.dic");

        if (File.Exists(affPath) && File.Exists(dicPath))
            return new DictionarySource(affPath, dicPath);

        return null;
    }

    private void ScanImportedDictionaries()
    {
        _importedDictionaries.Clear();

        if (!Directory.Exists(_dictionaryDir))
            return;

        foreach (var affFile in Directory.GetFiles(_dictionaryDir, "*.aff"))
        {
            var langCode = Path.GetFileNameWithoutExtension(affFile);
            var dicFile = Path.Combine(_dictionaryDir, $"{langCode}.dic");

            if (File.Exists(dicFile))
            {
                _importedDictionaries[langCode] = FormatDisplayName(langCode);
            }
        }
    }

    private static string FormatDisplayName(string langCode)
    {
        // Convert "de_DE" -> "de_DE" or provide a nicer name for common codes
        return langCode.Replace('_', ' ') switch
        {
            "de DE" => "German",
            "fr FR" => "French",
            "es ES" => "Spanish",
            "it IT" => "Italian",
            "pt BR" => "Portuguese (Brazil)",
            "pt PT" => "Portuguese (Portugal)",
            "nl NL" => "Dutch",
            "sv SE" => "Swedish",
            "da DK" => "Danish",
            "nb NO" => "Norwegian",
            "fi FI" => "Finnish",
            "pl PL" => "Polish",
            "cs CZ" => "Czech",
            "ru RU" => "Russian",
            "ja JP" => "Japanese",
            "zh CN" => "Chinese (Simplified)",
            _ => langCode
        };
    }

    private static async Task CopyFileAsync(string source, string destination)
    {
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        await using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        await sourceStream.CopyToAsync(destStream);
    }
}

using MLQT.Services;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

public class DictionaryManagerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DictionaryManagerService _service;

    public DictionaryManagerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DictMgrTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new DictionaryManagerService(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetAvailableDictionaries_ReturnsBundledByDefault()
    {
        var dictionaries = _service.GetAvailableDictionaries();

        Assert.Contains(dictionaries, d => d.LanguageCode == "en_US" && d.IsBundled);
        Assert.Contains(dictionaries, d => d.LanguageCode == "en_GB" && d.IsBundled);
    }

    [Fact]
    public void GetAvailableDictionaries_SortedByDisplayName()
    {
        var dictionaries = _service.GetAvailableDictionaries();

        var names = dictionaries.Select(d => d.DisplayName).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [Fact]
    public async Task ImportDictionaryAsync_CopiesFilesAndAddsToList()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "de_DE.aff");
        var dicPath = Path.Combine(sourceDir, "de_DE.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");

        var result = await _service.ImportDictionaryAsync(affPath, dicPath);

        Assert.Equal("de_DE", result);
        var dictionaries = _service.GetAvailableDictionaries();
        Assert.Contains(dictionaries, d => d.LanguageCode == "de_DE" && !d.IsBundled);
    }

    [Fact]
    public async Task ImportDictionaryAsync_FiresOnDictionariesChanged()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "fr_FR.aff");
        var dicPath = Path.Combine(sourceDir, "fr_FR.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");

        var eventFired = false;
        _service.OnDictionariesChanged += () => eventFired = true;

        await _service.ImportDictionaryAsync(affPath, dicPath);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task ImportDictionaryAsync_ReturnsNullForMissingFiles()
    {
        var result = await _service.ImportDictionaryAsync("/nonexistent.aff", "/nonexistent.dic");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveImportedDictionaryAsync_RemovesFilesAndUpdatesLists()
    {
        // Import first
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "es_ES.aff");
        var dicPath = Path.Combine(sourceDir, "es_ES.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");
        await _service.ImportDictionaryAsync(affPath, dicPath);

        var removed = await _service.RemoveImportedDictionaryAsync("es_ES");

        Assert.True(removed);
        Assert.DoesNotContain(_service.GetAvailableDictionaries(), d => d.LanguageCode == "es_ES");
    }

    [Fact]
    public async Task RemoveImportedDictionaryAsync_ReturnsFalseForBundled()
    {
        var removed = await _service.RemoveImportedDictionaryAsync("en_US");

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveImportedDictionaryAsync_ReturnsFalseForUnknown()
    {
        var removed = await _service.RemoveImportedDictionaryAsync("xx_XX");

        Assert.False(removed);
    }

    [Fact]
    public async Task GetImportedDictionaryPaths_ReturnsPathsForImported()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "it_IT.aff");
        var dicPath = Path.Combine(sourceDir, "it_IT.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");
        await _service.ImportDictionaryAsync(affPath, dicPath);

        var paths = _service.GetImportedDictionaryPaths("it_IT");

        Assert.NotNull(paths);
        Assert.True(File.Exists(paths.AffixFilePath));
        Assert.True(File.Exists(paths.DictionaryFilePath));
    }

    [Fact]
    public void GetImportedDictionaryPaths_ReturnsNullForBundled()
    {
        var paths = _service.GetImportedDictionaryPaths("en_US");

        Assert.Null(paths);
    }

    [Fact]
    public void GetImportedDictionaryPaths_ReturnsNullForUnknown()
    {
        var paths = _service.GetImportedDictionaryPaths("xx_XX");

        Assert.Null(paths);
    }

    [Fact]
    public async Task ImportDictionaryAsync_FormatsKnownLanguageNames()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "de_DE.aff");
        var dicPath = Path.Combine(sourceDir, "de_DE.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");

        await _service.ImportDictionaryAsync(affPath, dicPath);

        var dict = _service.GetAvailableDictionaries().First(d => d.LanguageCode == "de_DE");
        Assert.Equal("German", dict.DisplayName);
    }

    [Fact]
    public async Task ImportDictionaryAsync_UsesLangCodeForUnknownLanguages()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var affPath = Path.Combine(sourceDir, "xx_YY.aff");
        var dicPath = Path.Combine(sourceDir, "xx_YY.dic");
        await File.WriteAllTextAsync(affPath, "SET UTF-8");
        await File.WriteAllTextAsync(dicPath, "1\ntest");

        await _service.ImportDictionaryAsync(affPath, dicPath);

        var dict = _service.GetAvailableDictionaries().First(d => d.LanguageCode == "xx_YY");
        Assert.Equal("xx_YY", dict.DisplayName);
    }

    [Fact]
    public void ScanImportedDictionaries_PicksUpExistingFiles()
    {
        // Create dictionary files directly in the temp dir (simulating pre-existing imported dicts)
        File.WriteAllText(Path.Combine(_tempDir, "nl_NL.aff"), "SET UTF-8");
        File.WriteAllText(Path.Combine(_tempDir, "nl_NL.dic"), "1\ntest");

        // Create a new service pointing to the same dir - it should pick up the files
        var service2 = new DictionaryManagerService(_tempDir);
        var dictionaries = service2.GetAvailableDictionaries();

        Assert.Contains(dictionaries, d => d.LanguageCode == "nl_NL" && !d.IsBundled && d.DisplayName == "Dutch");
    }

    [Fact]
    public void ScanImportedDictionaries_IgnoresOrphanedAffFiles()
    {
        // Create only .aff file without matching .dic
        File.WriteAllText(Path.Combine(_tempDir, "orphan.aff"), "SET UTF-8");

        var service2 = new DictionaryManagerService(_tempDir);
        var dictionaries = service2.GetAvailableDictionaries();

        Assert.DoesNotContain(dictionaries, d => d.LanguageCode == "orphan");
    }

    [Fact]
    public async Task RemoveImportedDictionaryAsync_FiresOnDictionariesChanged()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sv_SE.aff"), "SET UTF-8");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "sv_SE.dic"), "1\ntest");
        await _service.ImportDictionaryAsync(
            Path.Combine(sourceDir, "sv_SE.aff"),
            Path.Combine(sourceDir, "sv_SE.dic"));

        var eventFired = false;
        _service.OnDictionariesChanged += () => eventFired = true;

        await _service.RemoveImportedDictionaryAsync("sv_SE");

        Assert.True(eventFired);
    }
}

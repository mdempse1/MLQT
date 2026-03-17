namespace MLQT.Services.Tests;

public class CustomDictionaryServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dictPath;
    private readonly CustomDictionaryService _service;

    public CustomDictionaryServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CustomDictTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testDir);
        _dictPath = Path.Combine(_testDir, "custom_dictionary.txt");
        _service = new CustomDictionaryService(_dictPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }

    // ========================================================================
    // LoadAsync
    // ========================================================================

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_LeavesEmptyDictionary()
    {
        await _service.LoadAsync();
        Assert.Empty(_service.CustomWords);
    }

    [Fact]
    public async Task LoadAsync_FileExists_LoadsWords()
    {
        await File.WriteAllLinesAsync(_dictPath, ["banana", "apple", "cherry"]);
        await _service.LoadAsync();

        Assert.Equal(3, _service.CustomWords.Count);
        Assert.Contains("apple", _service.CustomWords);
        Assert.Contains("banana", _service.CustomWords);
        Assert.Contains("cherry", _service.CustomWords);
    }

    [Fact]
    public async Task LoadAsync_SkipsBlankLines()
    {
        await File.WriteAllLinesAsync(_dictPath, ["alpha", "", "  ", "beta"]);
        await _service.LoadAsync();
        Assert.Equal(2, _service.CustomWords.Count);
    }

    [Fact]
    public async Task LoadAsync_TrimsWhitespace()
    {
        await File.WriteAllLinesAsync(_dictPath, ["  padded  ", "\ttabbed\t"]);
        await _service.LoadAsync();
        Assert.Contains("padded", _service.CustomWords);
        Assert.Contains("tabbed", _service.CustomWords);
    }

    [Fact]
    public async Task LoadAsync_ClearsExistingWordsBeforeLoading()
    {
        await _service.AddWordAsync("existing");
        Assert.Single(_service.CustomWords);

        await File.WriteAllLinesAsync(_dictPath, ["replacement"]);
        await _service.LoadAsync();

        Assert.Single(_service.CustomWords);
        Assert.Contains("replacement", _service.CustomWords);
        Assert.DoesNotContain("existing", _service.CustomWords);
    }

    // ========================================================================
    // CustomWords property
    // ========================================================================

    [Fact]
    public async Task CustomWords_ReturnsSortedCaseInsensitive()
    {
        await _service.AddWordAsync("Zebra");
        await _service.AddWordAsync("apple");
        await _service.AddWordAsync("Mango");

        var words = _service.CustomWords.ToList();
        Assert.Equal("apple", words[0]);
        Assert.Equal("Mango", words[1]);
        Assert.Equal("Zebra", words[2]);
    }

    // ========================================================================
    // AddWordAsync
    // ========================================================================

    [Fact]
    public async Task AddWordAsync_AddsWord()
    {
        await _service.AddWordAsync("hello");
        Assert.Contains("hello", _service.CustomWords);
    }

    [Fact]
    public async Task AddWordAsync_PersistsToDisk()
    {
        await _service.AddWordAsync("persist");
        var lines = await File.ReadAllLinesAsync(_dictPath);
        Assert.Contains("persist", lines);
    }

    [Fact]
    public async Task AddWordAsync_TrimsInput()
    {
        await _service.AddWordAsync("  spaced  ");
        Assert.Contains("spaced", _service.CustomWords);
    }

    [Fact]
    public async Task AddWordAsync_NullOrWhitespace_DoesNothing()
    {
        await _service.AddWordAsync(null!);
        await _service.AddWordAsync("");
        await _service.AddWordAsync("   ");
        Assert.Empty(_service.CustomWords);
    }

    [Fact]
    public async Task AddWordAsync_DuplicateWord_DoesNotDuplicate()
    {
        await _service.AddWordAsync("word");
        await _service.AddWordAsync("word");
        Assert.Single(_service.CustomWords);
    }

    [Fact]
    public async Task AddWordAsync_CaseInsensitiveDuplicate_DoesNotDuplicate()
    {
        await _service.AddWordAsync("Word");
        await _service.AddWordAsync("WORD");
        Assert.Single(_service.CustomWords);
    }

    [Fact]
    public async Task AddWordAsync_FiresOnDictionaryChanged()
    {
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.AddWordAsync("trigger");
        Assert.True(fired);
    }

    [Fact]
    public async Task AddWordAsync_DuplicateDoesNotFireEvent()
    {
        await _service.AddWordAsync("dup");
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.AddWordAsync("dup");
        Assert.False(fired);
    }

    [Fact]
    public async Task AddWordAsync_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_testDir, "sub", "dir", "dict.txt");
        var svc = new CustomDictionaryService(nestedPath);
        await svc.AddWordAsync("nested");
        Assert.True(File.Exists(nestedPath));
    }

    // ========================================================================
    // RemoveWordAsync
    // ========================================================================

    [Fact]
    public async Task RemoveWordAsync_RemovesExistingWord()
    {
        await _service.AddWordAsync("remove");
        await _service.RemoveWordAsync("remove");
        Assert.Empty(_service.CustomWords);
    }

    [Fact]
    public async Task RemoveWordAsync_PersistsToDisk()
    {
        await _service.AddWordAsync("keep");
        await _service.AddWordAsync("gone");
        await _service.RemoveWordAsync("gone");
        var lines = await File.ReadAllLinesAsync(_dictPath);
        Assert.Single(lines);
        Assert.Contains("keep", lines);
    }

    [Fact]
    public async Task RemoveWordAsync_NullOrWhitespace_DoesNothing()
    {
        await _service.AddWordAsync("safe");
        await _service.RemoveWordAsync(null!);
        await _service.RemoveWordAsync("");
        await _service.RemoveWordAsync("   ");
        Assert.Single(_service.CustomWords);
    }

    [Fact]
    public async Task RemoveWordAsync_NonExistentWord_DoesNothing()
    {
        await _service.AddWordAsync("only");
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.RemoveWordAsync("nope");
        Assert.False(fired);
        Assert.Single(_service.CustomWords);
    }

    [Fact]
    public async Task RemoveWordAsync_FiresOnDictionaryChanged()
    {
        await _service.AddWordAsync("bye");
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.RemoveWordAsync("bye");
        Assert.True(fired);
    }

    [Fact]
    public async Task RemoveWordAsync_TrimsInput()
    {
        await _service.AddWordAsync("trimmed");
        await _service.RemoveWordAsync("  trimmed  ");
        Assert.Empty(_service.CustomWords);
    }

    // ========================================================================
    // ImportAsync
    // ========================================================================

    [Fact]
    public async Task ImportAsync_ReplacesExistingWords()
    {
        await _service.AddWordAsync("old");

        var importFile = Path.Combine(_testDir, "import.txt");
        await File.WriteAllLinesAsync(importFile, ["new1", "new2"]);
        await _service.ImportAsync(importFile);

        Assert.Equal(2, _service.CustomWords.Count);
        Assert.DoesNotContain("old", _service.CustomWords);
        Assert.Contains("new1", _service.CustomWords);
        Assert.Contains("new2", _service.CustomWords);
    }

    [Fact]
    public async Task ImportAsync_SkipsBlankLines()
    {
        var importFile = Path.Combine(_testDir, "import_blank.txt");
        await File.WriteAllLinesAsync(importFile, ["word1", "", "  ", "word2"]);
        await _service.ImportAsync(importFile);
        Assert.Equal(2, _service.CustomWords.Count);
    }

    [Fact]
    public async Task ImportAsync_PersistsToDictPath()
    {
        var importFile = Path.Combine(_testDir, "import_persist.txt");
        await File.WriteAllLinesAsync(importFile, ["alpha", "beta"]);
        await _service.ImportAsync(importFile);

        var lines = await File.ReadAllLinesAsync(_dictPath);
        Assert.Contains("alpha", lines);
        Assert.Contains("beta", lines);
    }

    [Fact]
    public async Task ImportAsync_FiresOnDictionaryChanged()
    {
        var importFile = Path.Combine(_testDir, "import_event.txt");
        await File.WriteAllLinesAsync(importFile, ["word"]);
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.ImportAsync(importFile);
        Assert.True(fired);
    }

    // ========================================================================
    // ExportAsync
    // ========================================================================

    [Fact]
    public async Task ExportAsync_WritesWordsSorted()
    {
        await _service.AddWordAsync("cherry");
        await _service.AddWordAsync("apple");
        await _service.AddWordAsync("banana");

        var exportFile = Path.Combine(_testDir, "export.txt");
        await _service.ExportAsync(exportFile);

        var lines = await File.ReadAllLinesAsync(exportFile);
        Assert.Equal(3, lines.Length);
        Assert.Equal("apple", lines[0]);
        Assert.Equal("banana", lines[1]);
        Assert.Equal("cherry", lines[2]);
    }

    [Fact]
    public async Task ExportAsync_CreatesDirectoryIfMissing()
    {
        await _service.AddWordAsync("test");
        var exportFile = Path.Combine(_testDir, "nested", "export.txt");
        await _service.ExportAsync(exportFile);
        Assert.True(File.Exists(exportFile));
    }

    [Fact]
    public async Task ExportAsync_EmptyDictionary_WritesEmptyFile()
    {
        var exportFile = Path.Combine(_testDir, "empty_export.txt");
        await _service.ExportAsync(exportFile);
        var lines = await File.ReadAllLinesAsync(exportFile);
        Assert.Empty(lines);
    }

    // ========================================================================
    // MergeAsync
    // ========================================================================

    [Fact]
    public async Task MergeAsync_AddsNewWordsWithoutRemovingExisting()
    {
        await _service.AddWordAsync("existing");

        var mergeFile = Path.Combine(_testDir, "merge.txt");
        await File.WriteAllLinesAsync(mergeFile, ["new1", "new2"]);
        await _service.MergeAsync(mergeFile);

        Assert.Equal(3, _service.CustomWords.Count);
        Assert.Contains("existing", _service.CustomWords);
        Assert.Contains("new1", _service.CustomWords);
        Assert.Contains("new2", _service.CustomWords);
    }

    [Fact]
    public async Task MergeAsync_DuplicatesAreDeduped()
    {
        await _service.AddWordAsync("shared");

        var mergeFile = Path.Combine(_testDir, "merge_dup.txt");
        await File.WriteAllLinesAsync(mergeFile, ["shared", "unique"]);
        await _service.MergeAsync(mergeFile);

        Assert.Equal(2, _service.CustomWords.Count);
    }

    [Fact]
    public async Task MergeAsync_SkipsBlankLines()
    {
        var mergeFile = Path.Combine(_testDir, "merge_blank.txt");
        await File.WriteAllLinesAsync(mergeFile, ["word", "", "  "]);
        await _service.MergeAsync(mergeFile);
        Assert.Single(_service.CustomWords);
    }

    [Fact]
    public async Task MergeAsync_PersistsToDisk()
    {
        await _service.AddWordAsync("first");

        var mergeFile = Path.Combine(_testDir, "merge_persist.txt");
        await File.WriteAllLinesAsync(mergeFile, ["second"]);
        await _service.MergeAsync(mergeFile);

        var lines = await File.ReadAllLinesAsync(_dictPath);
        Assert.Contains("first", lines);
        Assert.Contains("second", lines);
    }

    [Fact]
    public async Task MergeAsync_FiresOnDictionaryChanged()
    {
        var mergeFile = Path.Combine(_testDir, "merge_event.txt");
        await File.WriteAllLinesAsync(mergeFile, ["word"]);
        var fired = false;
        _service.OnDictionaryChanged += () => fired = true;
        await _service.MergeAsync(mergeFile);
        Assert.True(fired);
    }

    // ========================================================================
    // Roundtrip
    // ========================================================================

    [Fact]
    public async Task Roundtrip_AddSaveLoadPreservesWords()
    {
        await _service.AddWordAsync("alpha");
        await _service.AddWordAsync("beta");

        var service2 = new CustomDictionaryService(_dictPath);
        await service2.LoadAsync();

        Assert.Equal(2, service2.CustomWords.Count);
        Assert.Contains("alpha", service2.CustomWords);
        Assert.Contains("beta", service2.CustomWords);
    }
}

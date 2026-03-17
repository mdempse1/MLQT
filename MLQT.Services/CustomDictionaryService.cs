using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <summary>
/// Manages a custom spell-checking dictionary stored at %LocalAppData%/MLQT/custom_dictionary.txt.
/// One word per line, sorted, case-insensitive. Shared across all repositories.
/// </summary>
public class CustomDictionaryService : ICustomDictionaryService
{
    private readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly string _dictionaryPath;

    public event Action? OnDictionaryChanged;

    public CustomDictionaryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _dictionaryPath = Path.Combine(appData, "MLQT", "custom_dictionary.txt");
    }

    /// <summary>
    /// Constructor for testing that allows specifying the dictionary file path.
    /// </summary>
    internal CustomDictionaryService(string dictionaryPath)
    {
        _dictionaryPath = dictionaryPath;
    }

    public IReadOnlyCollection<string> CustomWords
    {
        get
        {
            lock (_lock)
            {
                return _words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            }
        }
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_dictionaryPath))
            return;

        var lines = await File.ReadAllLinesAsync(_dictionaryPath);
        lock (_lock)
        {
            _words.Clear();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    _words.Add(trimmed);
            }
        }
    }

    public async Task AddWordAsync(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        bool added;
        lock (_lock)
        {
            added = _words.Add(word.Trim());
        }

        if (added)
        {
            await SaveAsync();
            OnDictionaryChanged?.Invoke();
        }
    }

    public async Task RemoveWordAsync(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        bool removed;
        lock (_lock)
        {
            removed = _words.Remove(word.Trim());
        }

        if (removed)
        {
            await SaveAsync();
            OnDictionaryChanged?.Invoke();
        }
    }

    public async Task ImportAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        lock (_lock)
        {
            _words.Clear();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    _words.Add(trimmed);
            }
        }

        await SaveAsync();
        OnDictionaryChanged?.Invoke();
    }

    public async Task ExportAsync(string filePath)
    {
        List<string> sorted;
        lock (_lock)
        {
            sorted = _words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(filePath, sorted);
    }

    public async Task MergeAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        lock (_lock)
        {
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    _words.Add(trimmed);
            }
        }

        await SaveAsync();
        OnDictionaryChanged?.Invoke();
    }

    private async Task SaveAsync()
    {
        List<string> sorted;
        lock (_lock)
        {
            sorted = _words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList();
        }

        var directory = Path.GetDirectoryName(_dictionaryPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(_dictionaryPath, sorted);
    }
}

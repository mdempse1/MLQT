using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <inheritdoc cref="ICustomDictionaryService"/>
public class CustomDictionaryService : ICustomDictionaryService
{
    /// <summary>File name of a repository's word list, beside its <c>settings.json</c>.</summary>
    public const string DictionaryFileName = "dictionary.txt";

    private const string MlqtDirectoryName = ".mlqt";
    private const string LegacyFileName = "custom_dictionary.txt";

    // One entry per repository root. Read on first use and kept, because the spell checker asks for
    // a repository's words once per class checked.
    private readonly Dictionary<string, HashSet<string>> _wordsByRoot =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lock = new();
    private readonly string? _legacyPath;

    public event Action<string>? OnDictionaryChanged;

    public CustomDictionaryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacy = Path.Combine(appData, "MLQT", LegacyFileName);
        _legacyPath = File.Exists(legacy) ? legacy : null;
    }

    /// <summary>Constructor for testing, pointing the legacy list somewhere predictable.</summary>
    internal CustomDictionaryService(string? legacyDictionaryPath)
    {
        _legacyPath = legacyDictionaryPath is not null && File.Exists(legacyDictionaryPath)
            ? legacyDictionaryPath
            : null;
    }

    public string? LegacyMachineDictionaryPath => _legacyPath;

    public string PathFor(string repositoryRoot) =>
        Path.Combine(repositoryRoot, MlqtDirectoryName, DictionaryFileName);

    public IReadOnlyCollection<string> WordsFor(string? repositoryRoot)
    {
        if (string.IsNullOrEmpty(repositoryRoot))
            return [];

        lock (_lock)
        {
            if (_wordsByRoot.TryGetValue(repositoryRoot, out var cached))
                return Snapshot(cached);
        }

        // Not seen yet: read it now rather than returning nothing and being quietly wrong. Callers
        // reach this from the checking path, where "no words" and "not loaded yet" look identical in
        // the results and only one of them is true.
        var words = ReadFile(PathFor(repositoryRoot));
        lock (_lock)
        {
            _wordsByRoot[repositoryRoot] = words;
            return Snapshot(words);
        }
    }

    public async Task<IReadOnlyCollection<string>> LoadAsync(string repositoryRoot)
    {
        var words = await Task.Run(() => ReadFile(PathFor(repositoryRoot)));
        lock (_lock)
        {
            _wordsByRoot[repositoryRoot] = words;
            return Snapshot(words);
        }
    }

    public async Task AddWordAsync(string repositoryRoot, string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        var trimmed = word.Trim();
        lock (_lock)
        {
            var words = Cached(repositoryRoot);
            if (!words.Add(trimmed))
                return;
        }

        await SaveAsync(repositoryRoot);
        OnDictionaryChanged?.Invoke(repositoryRoot);
    }

    public async Task RemoveWordAsync(string repositoryRoot, string word)
    {
        lock (_lock)
        {
            var words = Cached(repositoryRoot);
            if (!words.Remove(word))
                return;
        }

        await SaveAsync(repositoryRoot);
        OnDictionaryChanged?.Invoke(repositoryRoot);
    }

    public async Task<int> MergeFromAsync(string repositoryRoot, string sourceFile)
    {
        var incoming = await Task.Run(() => ReadFile(sourceFile));
        int added;

        lock (_lock)
        {
            var words = Cached(repositoryRoot);
            var before = words.Count;
            words.UnionWith(incoming);
            added = words.Count - before;
        }

        if (added > 0)
        {
            await SaveAsync(repositoryRoot);
            OnDictionaryChanged?.Invoke(repositoryRoot);
        }

        return added;
    }

    public async Task ExportAsync(string repositoryRoot, string targetFile)
    {
        var words = WordsFor(repositoryRoot);
        var directory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(targetFile, words);
    }

    /// <summary>Caller must hold the lock.</summary>
    private HashSet<string> Cached(string repositoryRoot)
    {
        if (!_wordsByRoot.TryGetValue(repositoryRoot, out var words))
        {
            words = ReadFile(PathFor(repositoryRoot));
            _wordsByRoot[repositoryRoot] = words;
        }

        return words;
    }

    private static IReadOnlyCollection<string> Snapshot(HashSet<string> words) =>
        words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();

    private static HashSet<string> ReadFile(string path)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return words;

        try
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var word = line.Trim();
                // '#' starts a comment so a team can explain why a word is accepted — the list is a
                // reviewed file in the repository now, not a private scratch pad.
                if (word.Length > 0 && !word.StartsWith('#'))
                    words.Add(word);
            }
        }
        catch (IOException)
        {
            // An unreadable list means no accepted words, which is the safe direction: it reports
            // spellings rather than hiding them.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return words;
    }

    private async Task SaveAsync(string repositoryRoot)
    {
        List<string> sorted;
        lock (_lock)
        {
            sorted = _wordsByRoot.TryGetValue(repositoryRoot, out var words)
                ? words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }

        var path = PathFor(repositoryRoot);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(path, sorted);
    }
}

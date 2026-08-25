using MLQT.Services.Interfaces;

namespace MLQT.Services;

/// <inheritdoc cref="ICustomDictionaryService"/>
public class CustomDictionaryService : ICustomDictionaryService
{
    /// <summary>File name of a repository's word list, beside its <c>settings.json</c>.</summary>
    public const string DictionaryFileName = "dictionary.txt";

    private const string MlqtDirectoryName = ".mlqt";
    private const string LegacyFileName = "custom_dictionary.txt";

    // One entry per repository root, with the file's timestamp when it was read. The timestamp is
    // what makes a list that changed outside the app — pulled from version control, edited by hand —
    // take effect: without it a spell checker built early in the session kept the words it was built
    // with while the settings page showed the file's current contents, so words plainly listed there
    // were still reported as misspelled.
    private readonly Dictionary<string, Entry> _wordsByRoot =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record Entry(HashSet<string> Words, (DateTime When, long Length) Stamp);

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

    public IReadOnlyCollection<string> WordsFor(string? repositoryRoot) =>
        string.IsNullOrEmpty(repositoryRoot) ? [] : Snapshot(Current(repositoryRoot));

    public async Task<IReadOnlyCollection<string>> LoadAsync(string repositoryRoot) =>
        await Task.Run(() => Snapshot(Current(repositoryRoot)));

    /// <summary>
    /// The repository's words, re-read when the file has changed on disk since the last read. A
    /// re-read that changes the list is announced, so anything holding a spell checker built from the
    /// old one drops it — otherwise a word visible in the settings page goes on being reported, with
    /// nothing on screen to explain the difference.
    ///
    /// <para>Reads on first use rather than returning nothing: callers reach this from the checking
    /// path, where "no accepted words" and "not loaded yet" look identical in the results and only one
    /// of them is true.</para>
    /// </summary>
    private HashSet<string> Current(string repositoryRoot)
    {
        var path = PathFor(repositoryRoot);
        var stamp = StampOf(path);

        lock (_lock)
        {
            if (_wordsByRoot.TryGetValue(repositoryRoot, out var cached) && cached.Stamp == stamp)
                return cached.Words;
        }

        var words = ReadFile(path);
        bool changed;
        lock (_lock)
        {
            changed = _wordsByRoot.TryGetValue(repositoryRoot, out var previous)
                      && !previous.Words.SetEquals(words);
            _wordsByRoot[repositoryRoot] = new Entry(words, stamp);
        }

        if (changed)
            OnDictionaryChanged?.Invoke(repositoryRoot);

        return words;
    }

    /// <summary>
    /// When the list was last written and how long it is, or zeroes if there is none. The length is
    /// part of it because the system clock is coarser than a file write: two writes close together can
    /// carry the same timestamp, and missing an edit here is exactly the failure this guards against.
    /// </summary>
    private static (DateTime When, long Length) StampOf(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : (DateTime.MinValue, 0);
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

    /// <summary>
    /// Caller must hold the lock. Honours the file's timestamp like <see cref="Current"/>, so adding a
    /// word does not write a stale list back over an edit made outside the app.
    /// </summary>
    private HashSet<string> Cached(string repositoryRoot)
    {
        var path = PathFor(repositoryRoot);
        var stamp = StampOf(path);

        if (_wordsByRoot.TryGetValue(repositoryRoot, out var cached) && cached.Stamp == stamp)
            return cached.Words;

        var words = ReadFile(path);
        _wordsByRoot[repositoryRoot] = new Entry(words, stamp);
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
            sorted = _wordsByRoot.TryGetValue(repositoryRoot, out var entry)
                ? entry.Words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToList()
                : [];
        }

        var path = PathFor(repositoryRoot);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(path, sorted);

        // Record what we just wrote, so the next read does not mistake our own write for an outside
        // change and announce it.
        lock (_lock)
        {
            if (_wordsByRoot.TryGetValue(repositoryRoot, out var entry))
                _wordsByRoot[repositoryRoot] = entry with { Stamp = StampOf(path) };
        }
    }
}

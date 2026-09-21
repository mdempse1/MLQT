using System.Collections.Concurrent;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaParser.Comparison;
using ModelicaParser.Helpers;
using RevisionControl;
using static MLQT.Services.LoggingService;

namespace MLQT.Services;

/// <inheritdoc cref="IModelChangeClassifier"/>
public class ModelChangeClassifier : IModelChangeClassifier
{
    private readonly IRepositoryService _repositoryService;

    /// <summary>
    /// One entry per changed file, keyed by repository and path. See <see cref="CacheKey"/> for what
    /// makes an entry stale.
    /// </summary>
    private readonly ConcurrentDictionary<string, CachedClassification> _cache = new(StringComparer.Ordinal);

    public ModelChangeClassifier(IRepositoryService repositoryService) =>
        _repositoryService = repositoryService;

    /// <summary>
    /// What the file and the repository were when the classification was computed.
    /// </summary>
    /// <remarks>
    /// The working copy's size and write time cover an edit; the repository's revision covers a pull
    /// or a branch switch, which changes what the committed version is without touching the file on
    /// disk. The status is in here because Git reports a file as Added until it is committed, and the
    /// committed version to compare against arrives the moment it stops being Added.
    /// </remarks>
    private readonly record struct CacheKey(long Length, long WriteTimeUtcTicks, string? Revision, VcsFileStatus Status);

    private sealed record CachedClassification(CacheKey Key, IReadOnlyDictionary<string, ClassChangeKind> Kinds);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ClassChangeKind> Classify(
        Repository repository, IReadOnlyList<VcsWorkingCopyFile> changes)
    {
        var kinds = new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            if (!change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                continue;

            // change.Path is relative to the VCS root and uses forward slashes on Git.
            var absolutePath = Path.Combine(
                repository.VcsRootPath, change.Path.Replace('/', Path.DirectorySeparatorChar));

            foreach (var (fullName, kind) in ClassifyFile(repository, change, absolutePath))
            {
                // A file's classes are its own, so a later file cannot contradict an earlier one.
                // Taking the stronger of the two anyway means a library that does have the same
                // class in two files reports the change rather than losing it to an ordering.
                if (!kinds.TryGetValue(fullName, out var existing) || kind > existing)
                    kinds[fullName] = kind;
            }
        }

        return kinds;
    }

    /// <inheritdoc />
    public void Invalidate(string? repositoryId = null)
    {
        if (repositoryId is null)
        {
            _cache.Clear();
            return;
        }

        var prefix = repositoryId + "";
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                _cache.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// One changed file's models, from the cache when nothing that matters has moved.
    /// </summary>
    private IReadOnlyDictionary<string, ClassChangeKind> ClassifyFile(
        Repository repository, VcsWorkingCopyFile change, string absolutePath)
    {
        var file = new FileInfo(absolutePath);

        // Deleted, or renamed away from here: nothing is left in the tree to mark.
        if (!file.Exists)
            return EmptyKinds;

        var key = new CacheKey(
            file.Length, file.LastWriteTimeUtc.Ticks, repository.CurrentRevision, change.Status);
        var cacheKey = repository.Id + "" + change.Path;

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Key == key)
            return cached.Kinds;

        var kinds = Compare(repository, change, absolutePath);
        _cache[cacheKey] = new CachedClassification(key, kinds);
        return kinds;
    }

    /// <summary>
    /// The comparison itself: the working copy against the committed version, class by class.
    /// </summary>
    private IReadOnlyDictionary<string, ClassChangeKind> Compare(
        Repository repository, VcsWorkingCopyFile change, string absolutePath)
    {
        try
        {
            var working = ModelicaFileEncoding.ReadAllTextOnly(absolutePath);

            // A conflicted working copy holds conflict markers, not Modelica. Reading it would
            // report a parse failure, which is true and useless; "not known" is the honest answer.
            if (change.Status == VcsFileStatus.Conflicted)
                return AllUnknown(working);

            // Nothing was committed, so every class in it is new. Asking the VCS for a version of an
            // untracked file is a round trip whose answer is already known.
            var committed = change.Status is VcsFileStatus.Added or VcsFileStatus.Untracked
                ? null
                : _repositoryService.GetFileContentAtRevision(repository.Id, change.Path);

            return ClassChangeClassifier.Compare(committed, working);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("ModelChangeClassifier",
                $"Could not classify changes in '{change.Path}': {ex.Message}. It is marked as modified without a kind.");
            return EmptyKinds;
        }
    }

    /// <summary>
    /// Every class in the working copy, with nothing claimed about any of them.
    /// </summary>
    private static IReadOnlyDictionary<string, ClassChangeKind> AllUnknown(string working)
    {
        var signatures = ClassSignatures.Of(working);
        var kinds = new Dictionary<string, ClassChangeKind>(signatures.Classes.Count, StringComparer.Ordinal);
        foreach (var fullName in signatures.Classes.Keys)
            kinds[fullName] = ClassChangeKind.Unknown;

        return kinds;
    }

    private static readonly IReadOnlyDictionary<string, ClassChangeKind> EmptyKinds =
        new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);
}

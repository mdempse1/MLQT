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

    /// <summary>
    /// The committed version of each file a class may have come from, parsed once per revision. Kept
    /// apart from <see cref="_cache"/> because a deleted file has no working copy to key on, and it is
    /// exactly the file a moved class has to be looked for in (B350).
    /// </summary>
    private readonly ConcurrentDictionary<string, CommittedVersion> _committed = new(StringComparer.Ordinal);

    public ModelChangeClassifier(IRepositoryService repositoryService)
    {
        _repositoryService = repositoryService;

        // Nothing kept for one project is any use to the next, and every path in it is another
        // project's (B350: nothing in production called Invalidate, so the cache grew across switches).
        _repositoryService.OnProjectChanged += _ => Invalidate();
    }

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

    /// <param name="Added">The working signature of each class that read as Added, so it can be looked
    /// for in the other files' committed versions without parsing the working copy again.</param>
    private sealed record CachedClassification(
        CacheKey Key, IReadOnlyDictionary<string, ClassChangeKind> Kinds, IReadOnlyDictionary<string, ClassSignature> Added);

    private sealed record CommittedVersion(string? Revision, ClassSignatures Signatures);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ClassChangeKind> Classify(
        Repository repository, IReadOnlyList<VcsWorkingCopyFile> changes)
    {
        var kinds = new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);
        var added = new Dictionary<string, ClassSignature>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            if (!change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                continue;

            // change.Path is relative to the VCS root and uses forward slashes on Git.
            var absolutePath = Path.Combine(
                repository.VcsRootPath, change.Path.Replace('/', Path.DirectorySeparatorChar));

            var file = ClassifyFile(repository, change, absolutePath);
            foreach (var (fullName, kind) in file.Kinds)
            {
                // A file's classes are its own, so a later file cannot contradict an earlier one.
                // Taking the stronger of the two anyway means a library that does have the same
                // class in two files reports the change rather than losing it to an ordering.
                if (!kinds.TryGetValue(fullName, out var existing) || kind > existing)
                    kinds[fullName] = kind;
            }

            foreach (var (fullName, signature) in file.Added)
                added.TryAdd(fullName, signature);
        }

        if (added.Count > 0)
            FindMovedClasses(repository, changes, kinds, added);

        return kinds;
    }

    /// <summary>
    /// Compares each class that read as Added with the committed version of any other changed or
    /// deleted file that had it (B350).
    /// </summary>
    /// <remarks>
    /// <para>Each file is compared with its own committed version, so a class moved to another file -
    /// by Split into files, Format All Files, a rename or <c>git mv</c> - found nothing where it now
    /// is and read as Added, which the marker shows as a simulation change and the Cosmetic filter
    /// hides. That is the wrong answer for exactly the restructuring MLQT performs. A class's identity
    /// is its full name, not its file, so it is looked for by name in the files it can have come
    /// from: the other changed files, the deleted ones and a renamed file's old path.</para>
    ///
    /// <para>Only files with a committed version are read, so a change set of new files alone still
    /// asks version control for nothing.</para>
    /// </remarks>
    private void FindMovedClasses(
        Repository repository,
        IReadOnlyList<VcsWorkingCopyFile> changes,
        Dictionary<string, ClassChangeKind> kinds,
        Dictionary<string, ClassSignature> added)
    {
        foreach (var change in changes)
        {
            if (added.Count == 0)
                return;

            if (!change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                || change.Status is VcsFileStatus.Added or VcsFileStatus.Untracked or VcsFileStatus.Conflicted)
                continue;

            var committed = CommittedSignatures(repository, CommittedPath(change));
            if (!committed.Parsed)
                continue;

            foreach (var (fullName, before) in committed.Classes)
            {
                if (!added.Remove(fullName, out var after))
                    continue;

                if (kinds.TryGetValue(fullName, out var kind) && kind == ClassChangeKind.Added)
                    kinds[fullName] = ClassChangeClassifier.Compare(before, after);
            }
        }
    }

    /// <summary>Where a changed file's committed version is: its old path, if it was renamed.</summary>
    private static string CommittedPath(VcsWorkingCopyFile change) =>
        string.IsNullOrEmpty(change.OldPath) ? change.Path : change.OldPath;

    /// <summary>
    /// A file's committed version, parsed - read once per repository revision.
    /// </summary>
    private ClassSignatures CommittedSignatures(Repository repository, string path)
    {
        var cacheKey = repository.Id + "" + path;
        if (_committed.TryGetValue(cacheKey, out var cached) && cached.Revision == repository.CurrentRevision)
            return cached.Signatures;

        var signatures = ClassSignatures.Of(_repositoryService.GetFileContentAtRevision(repository.Id, path));
        _committed[cacheKey] = new CommittedVersion(repository.CurrentRevision, signatures);
        return signatures;
    }

    /// <inheritdoc />
    public void Invalidate(string? repositoryId = null)
    {
        if (repositoryId is null)
        {
            _cache.Clear();
            _committed.Clear();
            return;
        }

        var prefix = repositoryId + "";
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                _cache.TryRemove(key, out _);
        }

        foreach (var key in _committed.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                _committed.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// One changed file's models, from the cache when nothing that matters has moved.
    /// </summary>
    private CachedClassification ClassifyFile(
        Repository repository, VcsWorkingCopyFile change, string absolutePath)
    {
        var file = new FileInfo(absolutePath);

        // Deleted, or renamed away from here: nothing is left in the tree to mark.
        if (!file.Exists)
            return Nothing;

        var key = new CacheKey(
            file.Length, file.LastWriteTimeUtc.Ticks, repository.CurrentRevision, change.Status);
        var cacheKey = repository.Id + "" + change.Path;

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Key == key)
            return cached;

        var (kinds, added) = Compare(repository, change, absolutePath);
        var classification = new CachedClassification(key, kinds, added);
        _cache[cacheKey] = classification;
        return classification;
    }

    /// <summary>
    /// The comparison itself: the working copy against the committed version, class by class.
    /// </summary>
    private (IReadOnlyDictionary<string, ClassChangeKind> Kinds, IReadOnlyDictionary<string, ClassSignature> Added) Compare(
        Repository repository, VcsWorkingCopyFile change, string absolutePath)
    {
        try
        {
            var working = ModelicaFileEncoding.ReadAllTextOnly(absolutePath);

            // A conflicted working copy holds conflict markers, not Modelica. Reading it would
            // report a parse failure, which is true and useless; "not known" is the honest answer.
            if (change.Status == VcsFileStatus.Conflicted)
                return (AllUnknown(working), EmptySignatures);

            // Nothing was committed, so every class in it is new - here, at least; FindMovedClasses
            // asks about the rest. Asking the VCS for a version of an untracked file is a round trip
            // whose answer is already known. A renamed file is compared with its old path's version.
            var committed = change.Status is VcsFileStatus.Added or VcsFileStatus.Untracked
                ? ClassSignatures.Absent
                : CommittedSignatures(repository, CommittedPath(change));

            var workingSignatures = ClassSignatures.Of(working);
            var kinds = ClassChangeClassifier.Compare(committed, workingSignatures);

            Dictionary<string, ClassSignature>? added = null;
            foreach (var (fullName, kind) in kinds)
            {
                if (kind == ClassChangeKind.Added)
                    (added ??= new(StringComparer.Ordinal))[fullName] = workingSignatures.Classes[fullName];
            }

            return (kinds, added ?? EmptySignatures);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("ModelChangeClassifier",
                $"Could not classify changes in '{change.Path}': {ex.Message}. It is marked as modified without a kind.");
            return (EmptyKinds, EmptySignatures);
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

    private static readonly IReadOnlyDictionary<string, ClassSignature> EmptySignatures =
        new Dictionary<string, ClassSignature>(StringComparer.Ordinal);

    private static readonly CachedClassification Nothing = new(default, EmptyKinds, EmptySignatures);
}

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

    /// <summary>
    /// The most changed Modelica files one classification will compare. A larger change set is not
    /// classified at all: every file is still marked as modified, without saying how.
    /// </summary>
    /// <remarks>
    /// Each file costs a read of its committed version from version control - an <c>svn cat</c>
    /// process on SVN - and two parses. Unbounded, 17,372 changed files took eighteen minutes, and a
    /// change set that size is a reformat or a merge that nobody will review class by class. Two
    /// thousand is about twenty seconds at the rate the parallel reads manage.
    /// </remarks>
    public const int DefaultMaxFilesToClassify = 2000;

    /// <summary>A classification of at least this many files says in the log what it is doing.</summary>
    private const int LoggedFrom = 100;

    /// <summary>How often a long classification reports its progress, in files.</summary>
    private const int ProgressEvery = 500;

    private readonly int _maxFilesToClassify;

    /// <summary>
    /// One classification per working copy at a time. Every open tree asks after each notification,
    /// and two asking at once each read every committed version for themselves; the second now waits
    /// and finds the first one's answers in the cache. Per working copy rather than per repository,
    /// for the reason given at <see cref="CheckoutOf"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _checkoutGates = new(StringComparer.Ordinal);

    /// <summary>
    /// The newest request for each repository. A request that a newer one for the same repository has
    /// overtaken is not wanted - the browser that asked drops the answer - so it stops waiting for
    /// the gate, or stops reading if it already has it, and answers "not asked". What it read is
    /// cached for the newer one. Without this every refresh during a burst of file activity queued
    /// behind the last, each holding a thread, and each ran the whole classification in turn.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _latestRequest = new(StringComparer.Ordinal);

    /// <summary>How often a waiting request looks to see whether it has been overtaken.</summary>
    private const int SupersededPollMs = 50;

    /// <summary>
    /// The change count each repository was last told was over the limit, so the log says so once
    /// per change set rather than on every refresh of every tree.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _overLimitLogged = new(StringComparer.Ordinal);

    /// <summary>
    /// How many committed versions are read at once. Each is mostly waiting - for a process to start
    /// on SVN, for the object store on Git - so more than the cores, but bounded so a large change set
    /// does not start hundreds of processes together.
    /// </summary>
    private static readonly int ReadParallelism = Math.Clamp(Environment.ProcessorCount, 4, 16);

    public ModelChangeClassifier(IRepositoryService repositoryService)
        : this(repositoryService, DefaultMaxFilesToClassify)
    {
    }

    internal ModelChangeClassifier(IRepositoryService repositoryService, int maxFilesToClassify)
    {
        _repositoryService = repositoryService;
        _maxFilesToClassify = maxFilesToClassify;

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
        var modelicaChanges = changes
            .Where(change => change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Counted by what costs a read: an added or untracked file has no committed version to ask
        // for, and a conflicted one is not compared, so a pile of new generated files does not switch
        // the comparison off for the handful of edited ones beside it.
        var toRead = modelicaChanges.Count(NeedsCommittedVersion);

        // Absence is "not asked" (IModelChangeClassifier), so every file is still marked modified.
        if (toRead > _maxFilesToClassify)
        {
            if (_overLimitLogged.TryGetValue(repository.Id, out var logged) is false || logged != toRead)
            {
                _overLimitLogged[repository.Id] = toRead;
                Info("ModelChangeClassifier",
                    $"Not classifying the changes in {repository.Name}: {toRead} changed Modelica file(s) "
                    + $"to compare, more than {_maxFilesToClassify}. They are marked as modified without saying how.");
            }
            return EmptyKinds;
        }

        var ticket = _latestRequest.AddOrUpdate(repository.Id, 1, (_, latest) => latest + 1);
        bool Superseded() => _latestRequest.TryGetValue(repository.Id, out var latest) && latest != ticket;

        var gate = _checkoutGates.GetOrAdd(CheckoutOf(repository), _ => new SemaphoreSlim(1, 1));
        while (!gate.Wait(SupersededPollMs))
        {
            if (Superseded())
                return EmptyKinds;
        }

        try
        {
            return Superseded() ? EmptyKinds : ClassifyAll(repository, changes, modelicaChanges, Superseded);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Whether comparing this change needs its committed version read from version control - and so
    /// whether a missing one means the read failed rather than that there is none.
    /// </summary>
    private static bool NeedsCommittedVersion(VcsWorkingCopyFile change) => change.Status switch
    {
        VcsFileStatus.Modified or VcsFileStatus.Deleted => true,
        VcsFileStatus.Renamed => !string.IsNullOrEmpty(change.OldPath),
        _ => false,
    };

    /// <summary>
    /// The working copy a repository is in, as a key: what the gate and both caches are kept by.
    /// </summary>
    /// <remarks>
    /// Not the repository's id. Two libraries checked out in one tree are two repositories with one
    /// working copy (B330), and each is given the whole working copy's changes - so keyed by id, each
    /// read every committed version for itself. Remembered against the id so
    /// <see cref="Invalidate"/>, which is given an id, finds what to drop.
    /// </remarks>
    private string CheckoutOf(Repository repository)
    {
        var root = string.IsNullOrEmpty(repository.VcsRootPath)
            ? repository.Id
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository.VcsRootPath));

        // The same folder whatever the case it was written in, where the file system says so.
        if (OperatingSystem.IsWindows())
            root = root.ToUpperInvariant();

        _checkouts[repository.Id] = root;
        return root;
    }

    /// <summary>Each repository's checkout key, as <see cref="CheckoutOf"/> last worked it out.</summary>
    private readonly ConcurrentDictionary<string, string> _checkouts = new(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, ClassChangeKind> ClassifyAll(
        Repository repository, IReadOnlyList<VcsWorkingCopyFile> changes, List<VcsWorkingCopyFile> modelicaChanges,
        Func<bool> superseded)
    {
        var logged = modelicaChanges.Count >= LoggedFrom;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        if (logged)
            Info("ModelChangeClassifier",
                $">>> STARTING: Classifying {modelicaChanges.Count} changed Modelica file(s) in {repository.Name}");

        // Each file on its own, several at once: nearly all of the time is reading committed
        // versions, one round trip each. The answers are merged afterwards in the order version
        // control gave them, so the result does not depend on which read finished first.
        var files = new CachedClassification[modelicaChanges.Count];
        var done = 0;
        Parallel.For(0, modelicaChanges.Count, new ParallelOptions { MaxDegreeOfParallelism = ReadParallelism }, (i, loop) =>
        {
            // Overtaken: what has been read stays cached for the newer request, the rest is its job.
            if (superseded())
            {
                loop.Stop();
                return;
            }

            var change = modelicaChanges[i];

            // change.Path is relative to the VCS root and uses forward slashes on Git.
            var absolutePath = Path.Combine(
                repository.VcsRootPath, change.Path.Replace('/', Path.DirectorySeparatorChar));

            files[i] = ClassifyFile(repository, change, absolutePath);

            var count = Interlocked.Increment(ref done);
            if (logged && count % ProgressEvery == 0)
                Info("ModelChangeClassifier",
                    $"Classified {count}/{modelicaChanges.Count} changed file(s) in {repository.Name}");
        });

        if (superseded())
        {
            if (logged)
                Info("ModelChangeClassifier",
                    $"<<< COMPLETED: Classifying changes in {repository.Name} stopped after {done} file(s): "
                    + $"a newer request took over ({stopwatch.ElapsedMilliseconds}ms)");
            return EmptyKinds;
        }

        var kinds = new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);
        var added = new Dictionary<string, ClassSignature>(StringComparer.Ordinal);

        foreach (var file in files)
        {
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

        if (logged)
            Info("ModelChangeClassifier",
                $"<<< COMPLETED: Classifying {modelicaChanges.Count} changed Modelica file(s) in {repository.Name} "
                + $"({stopwatch.ElapsedMilliseconds}ms)");

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
        // Read the committed versions the loop below may want several at once, as Classify does.
        // Most are cached already - a modified file's was read to classify it - so this is mainly
        // the deleted files and a renamed file's old path.
        var candidates = changes
            .Where(change => change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                && change.Status is not (VcsFileStatus.Added or VcsFileStatus.Untracked or VcsFileStatus.Conflicted))
            .DistinctBy(CommittedPath, StringComparer.Ordinal)
            .ToList();
        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = ReadParallelism },
            change => CommittedSignatures(repository, change));

        foreach (var change in changes)
        {
            if (added.Count == 0)
                return;

            if (!change.Path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase)
                || change.Status is VcsFileStatus.Added or VcsFileStatus.Untracked or VcsFileStatus.Conflicted)
                continue;

            // Unreadable - a failed read, or one that would not parse - has no classes to look in.
            var (committed, _) = CommittedSignatures(repository, change);
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
    /// A changed file's committed version, parsed - read once per repository revision.
    /// </summary>
    /// <remarks>
    /// <b>A failed read is not "there is no committed version".</b> Version control answers null for
    /// both, and taken as absent, every class of a modified file read as Added - the simulation
    /// marker - and stayed so until the revision changed, because the answer was cached. Where
    /// <see cref="NeedsCommittedVersion"/> says there must be one, a null is a failure: it reads as
    /// <see cref="ClassSignatures.Unreadable"/>, the plain "not compared" marker, is not cached, and
    /// is asked for again next time.
    /// </remarks>
    private (ClassSignatures Signatures, bool ReadFailed) CommittedSignatures(
        Repository repository, VcsWorkingCopyFile change)
    {
        var path = CommittedPath(change);
        var cacheKey = CheckoutOf(repository) +"" + path;
        if (_committed.TryGetValue(cacheKey, out var cached) && cached.Revision == repository.CurrentRevision)
            return (cached.Signatures, false);

        var text = _repositoryService.GetFileContentAtRevision(repository.Id, path);
        if (text is null && NeedsCommittedVersion(change))
        {
            Warn("ModelChangeClassifier",
                $"Could not read the committed version of '{path}' in {repository.Name}; its classes are "
                + "marked as modified without a kind, and it will be read again on the next refresh.");
            return (ClassSignatures.Unreadable, true);
        }

        var signatures = ClassSignatures.Of(text);
        _committed[cacheKey] = new CommittedVersion(repository.CurrentRevision, signatures);
        return (signatures, false);
    }

    /// <inheritdoc />
    public void Invalidate(string? repositoryId = null)
    {
        if (repositoryId is null)
        {
            _cache.Clear();
            _committed.Clear();

            // And what is kept about each repository and checkout, so a session that switches
            // between projects does not accumulate every one it has seen. The gates stay: a
            // classification may be holding one now, and there is one small object per checkout.
            _checkouts.Clear();
            _overLimitLogged.Clear();

            // Which also tells a classification still running for the old project that it has been
            // overtaken - its ticket no longer matches - so it stops reading.
            _latestRequest.Clear();
            return;
        }

        // Everything kept for its checkout, which a repository sharing it was using too: they are
        // the same files.
        var prefix = (_checkouts.TryGetValue(repositoryId, out var checkout) ? checkout : repositoryId) +"";
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
        var cacheKey = CheckoutOf(repository) +"" + change.Path;

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Key == key)
            return cached;

        var (kinds, added, readFailed) = Compare(repository, change, absolutePath);
        var classification = new CachedClassification(key, kinds, added);

        // Not kept when the committed version could not be read: the next refresh tries again.
        if (!readFailed)
            _cache[cacheKey] = classification;
        return classification;
    }

    /// <summary>
    /// The comparison itself: the working copy against the committed version, class by class.
    /// </summary>
    private (IReadOnlyDictionary<string, ClassChangeKind> Kinds, IReadOnlyDictionary<string, ClassSignature> Added, bool ReadFailed) Compare(
        Repository repository, VcsWorkingCopyFile change, string absolutePath)
    {
        try
        {
            var working = ModelicaFileEncoding.ReadAllTextOnly(absolutePath);

            // A conflicted working copy holds conflict markers, not Modelica. Reading it would
            // report a parse failure, which is true and useless; "not known" is the honest answer.
            if (change.Status == VcsFileStatus.Conflicted)
                return (AllUnknown(working), EmptySignatures, false);

            // Nothing was committed, so every class in it is new - here, at least; FindMovedClasses
            // asks about the rest. Asking the VCS for a version of an untracked file is a round trip
            // whose answer is already known. A renamed file is compared with its old path's version.
            var (committed, readFailed) = change.Status is VcsFileStatus.Added or VcsFileStatus.Untracked
                ? (ClassSignatures.Absent, false)
                : CommittedSignatures(repository, change);

            var workingSignatures = ClassSignatures.Of(working);
            var kinds = ClassChangeClassifier.Compare(committed, workingSignatures);

            Dictionary<string, ClassSignature>? added = null;
            foreach (var (fullName, kind) in kinds)
            {
                if (kind == ClassChangeKind.Added)
                    (added ??= new(StringComparer.Ordinal))[fullName] = workingSignatures.Classes[fullName];
            }

            return (kinds, added ?? EmptySignatures, readFailed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn("ModelChangeClassifier",
                $"Could not classify changes in '{change.Path}': {ex.Message}. It is marked as modified without a kind.");
            return (EmptyKinds, EmptySignatures, true);
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

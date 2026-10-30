using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaGraph;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Loads the libraries a caller names and, if asked, what they depend on (B503,
/// <c>.claude/skills/mcp-library-loading/SKILL.md</c>, *Finding dependencies* and *When the simulator's
/// session decides*).
/// </summary>
/// <remarks>
/// <para><b>Explicit first.</b> Every path the caller passes is loaded before anything is searched for, so a
/// search cannot get in first with another version. An explicit library replaces a copy an earlier search
/// found; two explicit copies of one library need <c>replace</c>, because then the caller has to choose.
/// A library is known by the name its files declare, a library the host supplied counts as explicit, and a
/// folder of libraries is held to the same rules before anything in it is loaded.</para>
/// <para><b>Then the search</b>, for each <c>uses</c> not yet satisfied, transitively: the folder beside the
/// library that needs it, its repository's root, the simulation tool's library folders, then
/// MODELICAPATH. The first exact version found wins, the largest build of it; failing that, the newest
/// copy declaring <c>conversion(noneFromVersion=...)</c> for the version asked for; failing that, nothing,
/// and every copy seen is reported. A search never replaces a library already loaded - except that a load
/// naming another tool install than the last search used unloads what searches found, and searches again
/// (B504).</para>
/// </remarks>
internal sealed class DependencyLoader
{
    private const string Explicit = "explicit";
    private const string Search = "search";

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly SessionState _session;
    private readonly Func<string, string?> _environment;
    private readonly List<(string Folder, string FoundBy)> _extraFolders = [];

    private readonly List<LoadedLibraryItem> _loaded = [];
    private readonly List<ReplacedLibraryItem> _replaced = [];
    private readonly List<MissingDependency> _notFound = [];
    private readonly List<string> _warnings = [];
    private readonly List<string> _errors = [];
    private readonly List<LoadRepositoryResult> _repositoriesLoaded = [];

    /// <summary>
    /// Loads a directory that holds no library of its own - a working copy, or a folder of libraries - as
    /// <c>mlqt_load</c> does, so its settings and changes are known; with false, only discovers
    /// its libraries. Set by the tool, so that there is one implementation of it (B503).
    /// </summary>
    public Func<string, bool, Task<LoadRepositoryResult>>? LoadRepository { get; init; }

    /// <summary>Discover a repository's libraries without loading them.</summary>
    public bool DiscoverOnly { get; init; }

    public DependencyLoader(ILibraryDataService libraries, IRepositoryService repositories, SessionState session,
        Func<string, string?> environment)
    {
        _libraries = libraries;
        _repositories = repositories;
        _session = session;
        _environment = environment;
    }

    /// <summary>Folders a startup script added to its ModelicaPath, searched after the tool's own.</summary>
    public void AddSearchFolders(IEnumerable<string> folders, string foundBy) =>
        _extraFolders.AddRange(folders.Select(f => (f, foundBy)));

    public async Task<LoadResult> LoadAsync(IReadOnlyList<string> paths, bool withDependencies, bool replace, ToolFolders tool)
    {
        var before = _libraries.Libraries.ToList();
        var reportedAlready = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in paths)
            await LoadExplicitAsync(path, replace);

        if (withDependencies)
        {
            var unloaded = SearchAgainIfTheInstallChanged(tool.Install);
            reportedAlready.UnionWith(unloaded.Select(u => u.Id));
            await LoadDependenciesAsync(tool);
            ReportWhatTheNewSearchFound(unloaded);
        }

        ReportReplaced(before, reportedAlready);
        ForgetAnalysisIfTheLibrariesChanged(before);

        // A tool was named but nothing of it was found: say so, since the search was narrower than asked.
        if (withDependencies && tool.Note is { } note && tool.Folders.Count == 0)
            _warnings.Add(note);

        return new LoadResult(_loaded, _replaced, _notFound, _warnings, _errors, tool.Install, tool.InstallReason, tool.Folders,
            Repositories: _repositoriesLoaded.Count > 0 ? _repositoriesLoaded : null);
    }

    /// <summary>
    /// Marks the dependency and resource analysis as not run when this load changed which libraries are
    /// loaded, so the next tool needing it runs it again. The edges were built over the libraries loaded
    /// then: a copy replaced by an explicit one left them pointing at classes no longer loaded, and a
    /// library added after analysis had none at all, while the session still said analysed (B506).
    /// </summary>
    private void ForgetAnalysisIfTheLibrariesChanged(List<LoadedLibrary> before)
    {
        var now = _libraries.Libraries.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        if (now.SetEquals(before.Select(l => l.Id)))
            return;
        _session.DependenciesAnalyzed = false;
        _session.ResourcesAnalyzed = false;
    }

    /// <summary>
    /// When <paramref name="install"/> is not the install the session's last search used, unloads every
    /// library a search found, so the search about to run decides them all again from the new one. A
    /// search never replaces a loaded library, so without this a second load naming the install that is
    /// running only filled the gaps, and kept the old install's copies of the rest (B504). The libraries
    /// the caller named stay. Returns what was unloaded.
    /// </summary>
    private List<(string Id, string Name, string? Version, string Path)> SearchAgainIfTheInstallChanged(string? install)
    {
        var unloaded = new List<(string Id, string Name, string? Version, string Path)>();
        if (install is null)
            return unloaded;
        var previous = _session.SearchInstall;
        _session.SearchInstall = install;
        if (previous is null || SamePath(previous, install))
            return unloaded;

        foreach (var library in _libraries.Libraries.Where(l => OriginOf(l) == Search).ToList())
        {
            unloaded.Add((library.Id, library.Name, library.Version, library.SourcePath));
            _libraries.RemoveLibrary(library.Id);
            _session.LibraryOrigins.Remove(library.Id);
        }
        if (unloaded.Count > 0)
        {
            _session.DependenciesAnalyzed = false;
            _session.ResourcesAnalyzed = false;
            _warnings.Add($"The tool install changed from '{previous}' to '{install}', so the {unloaded.Count} " +
                          "libraries the earlier search found were unloaded and searched for again. If other " +
                          "work is using this MLQT server, its dependencies changed with them.");
        }
        return unloaded;
    }

    // Each library unloaded for a new install: replaced when the search found it somewhere else, said
    // when it found nothing; one found again at the same path is as it was.
    private void ReportWhatTheNewSearchFound(List<(string Id, string Name, string? Version, string Path)> unloaded)
    {
        foreach (var (_, name, version, path) in unloaded)
        {
            var now = _libraries.Libraries.FirstOrDefault(l => l.Name == name);
            if (now is null)
                _warnings.Add($"{name} {version} from '{path}' was unloaded for the new install, and no copy of it was found there.");
            else if (!SamePath(now.SourcePath, path))
                _replaced.Add(new ReplacedLibraryItem(name, version, path, now.Version, now.SourcePath));
        }
    }

    /// <summary>
    /// Every library loaded before this call that another copy has taken the place of, whichever way it
    /// went: unloaded here for an explicit path, or retired by the library service's precedence rule as
    /// the new copy registered - readable source over a supplied library, or an installed build of
    /// another release over a supplied one, which this code never sees happen. Asked of the session
    /// itself, before and after, so that no route in can replace a library without saying so; a
    /// supplied library above all, which only the host can supply again.
    /// </summary>
    private void ReportReplaced(List<LoadedLibrary> before, HashSet<string> reportedAlready) =>
        _replaced.AddRange(ReplacedBetween(before, _libraries.Libraries.ToList(), reportedAlready));

    /// <summary>
    /// The libraries of <paramref name="before"/> that another copy has taken the place of in
    /// <paramref name="after"/>: gone, with a library of the same name now loaded from another path. One
    /// loaded again from where it was - a reload gives it a new id - is not replaced. The one definition of
    /// "replaced", for <c>mlqt_load</c> and <c>mlqt_session</c>'s reload alike.
    /// </summary>
    internal static IReadOnlyList<ReplacedLibraryItem> ReplacedBetween(
        IReadOnlyList<LoadedLibrary> before, IReadOnlyList<LoadedLibrary> after, ISet<string>? reportedAlready = null) =>
        before
            .Where(b => reportedAlready?.Contains(b.Id) != true && after.All(a => a.Id != b.Id))
            .Select(old => (Old: old, Now: after.FirstOrDefault(a => a.Name == old.Name)))
            .Where(p => p.Now is not null && !SamePath(p.Now.SourcePath, p.Old.SourcePath))
            .Select(p => new ReplacedLibraryItem(p.Old.Name, p.Old.Version, p.Old.SourcePath, p.Now!.Version, p.Now.SourcePath))
            .ToList();

    /// <summary>
    /// The refusal for a second copy of a library the caller already named. MLQT holds one copy of each
    /// library and a class id does not say whose, so the copy loaded may be another task's, or an earlier
    /// conversation's: this said only "pass replace=true", and a task that did would have moved the other's
    /// edits onto its own copy without either knowing (B505). It says how old the copy is, and what
    /// replacing it does, so that the caller can tell whether it is its own.
    /// </summary>
    private string AnotherCopy(string name, LoadedLibrary existing, string path) =>
        $"'{name}' is already loaded from '{existing.SourcePath}', loaded {_session.LoadedAgo(existing.Id)}, and " +
        $"'{path}' is another copy. MLQT holds one copy of a library, and a class id does not say whose: if " +
        "this conversation did not load that copy, it may be another task's using this MLQT server, or an " +
        $"earlier conversation's. Replacing it unloads it, and every later MLQT edit to {name}'s classes - that " +
        "other work's included - goes to this copy. Pass replace=true only if this conversation loaded the " +
        "other copy, or the user says so; otherwise ask the user. mlqt_session shows what is loaded and when.";

    private static string Superseded(string name, string path, string source) =>
        $"'{name}' at '{path}' is encrypted, and a readable copy is already loaded from '{source}', which is used instead.";

    // The copy that kept its place is itself read-only: supplied by the host, or another encrypted build.
    private static string OutrankedByReadOnly(string name, string path, LoadedLibrary keeper) =>
        $"'{name}' at '{path}' was not loaded: the {(keeper.ReadOnlySource == ReadOnlySourceKind.Supplied ? "copy the host supplied" : "copy")} " +
        $"loaded from '{keeper.SourcePath}' takes precedence over it and is used instead.";

    private string OriginOf(LoadedLibrary library) => _session.OriginOf(library.Id);

    private async Task LoadExplicitAsync(string path, bool replace)
    {
        var root = LibraryRoot(path);
        if (root is null)
        {
            _errors.Add($"'{path}' is not a library folder, a package.mo or a .mo file.");
            return;
        }

        // A directory with no package.mo of its own is a working copy or a folder of libraries.
        if (LoadRepository is { } loadRepository && Directory.Exists(root) &&
            !File.Exists(Path.Combine(root, "package.mo")) && !File.Exists(Path.Combine(root, "package.moe")))
        {
            await LoadRepositoryAsync(loadRepository, root, replace);
            return;
        }

        // The name the library declares, not its folder's: a folder called after a loaded library may hold
        // another one, which was refused as a copy of it - or, with replace, unloaded it - and a copy in a
        // folder not called after it passed, and a supplied library was retired by it unasked.
        var name = LibraryDiscovery.ReadLibraryName(root) ?? NameFromPath(root);
        var existing = _libraries.Libraries.FirstOrDefault(l => l.Name == name);
        if (existing is not null)
        {
            if (SamePath(existing.SourcePath, root))
            {
                _session.LibraryOrigins[existing.Id] = Explicit;
                _loaded.Add(Item(existing, Explicit, "already loaded", null, null));
                return;
            }
            // An encrypted build beside readable source is not another copy to choose between: the source
            // wins whole (B268), so it is said, not refused - a refusal offers replace, which would swap
            // the source for a build with none.
            if (!existing.IsReadOnly && EncryptedLibraryDetector.IsEncryptedLibraryRoot(root))
            {
                _warnings.Add(Superseded(name, path, existing.SourcePath));
                return;
            }
            // Nor is an encrypted build of the release the host supplied, replace or not.
            if (SuppliedDescribesThisBuild(existing, root) is { } release)
            {
                _warnings.Add(SuppliedForThisBuild(name, path, existing, release));
                return;
            }
            if (OriginOf(existing) == Explicit && !replace)
            {
                _errors.Add(AnotherCopy(name, existing, path));
                return;
            }
        }

        // The copy loaded stays until this one has: a load that fails leaves the session as it was.
        var library = await LoadInPlaceOfAsync(root, existing);
        if (library is null)
        {
            await RestoreIfGoneAsync(existing, path);
            return;
        }

        // A name that could not be read before the load - a package.mo that does not parse - is known
        // only now. Without this the copy was loaded beside the first, silently, and every class id still
        // answered with the first: a second task's edits went to the first task's files (B505).
        if (existing is null &&
            _libraries.Libraries.FirstOrDefault(l => l.Id != library.Id && l.Name == library.Name) is { } twin)
        {
            if (OriginOf(twin) == Explicit && !replace)
            {
                _libraries.RemoveLibrary(library.Id);
                _errors.Add(AnotherCopy(library.Name, twin, path));
                return;
            }
            existing = twin;
        }

        if (existing is not null && IsLoaded(existing))
            Unload(existing);
        _session.LibraryOrigins[library.Id] = Explicit;
        _loaded.Add(Item(library, Explicit, "given", null, null));
    }

    /// <summary>
    /// Loads <paramref name="root"/> to take the place of <paramref name="existing"/>; the caller has
    /// already decided it is wanted.
    /// </summary>
    /// <remarks>
    /// A copy on disk is unloaded first - two readable copies of one library cannot be loaded side by
    /// side, their classes having the same ids - and loaded again by the caller if this one fails. A
    /// supplied library cannot be loaded again, so it stays until this one has loaded: readable source,
    /// and an encrypted build of another release, retire it as they register. An encrypted build of the
    /// same release never gets here (<see cref="SuppliedDescribesThisBuild"/>).
    /// </remarks>
    private async Task<LoadedLibrary?> LoadInPlaceOfAsync(string root, LoadedLibrary? existing)
    {
        if (existing is not null && EncryptedLibraryDetector.Detect(root) is { HasDocumentation: false })
        {
            // Nothing could be recovered from it, so taking the existing copy out for it would leave neither.
            _warnings.Add($"'{existing.Name}' at '{root}' is an encrypted library that ships no usable documentation, " +
                          $"so it was not loaded and the copy from '{existing.SourcePath}' was kept.");
            return null;
        }

        if (existing is not null && existing.ReadOnlySource != ReadOnlySourceKind.Supplied)
        {
            Unload(existing);
            return Usable(await LoadRawAsync(root), root);
        }

        return Usable(await LoadRawAsync(root), root);
    }

    /// <summary>
    /// The release, when <paramref name="existing"/> is a library the host supplied and
    /// <paramref name="root"/> an encrypted build of the same release; otherwise null. The supplied copy
    /// is the vendor's own description of that release, and wins over what MLQT can rebuild from the
    /// build's help pages - <c>replace</c> or not. Where either version is unknown nothing tells the two
    /// apart, and the supplied copy is kept, as the library service keeps it. A build of another release
    /// is what is installed, and may take the supplied copy's place (<c>LibraryPrecedence</c>).
    /// </summary>
    private static string? SuppliedDescribesThisBuild(LoadedLibrary existing, string root)
    {
        if (existing.ReadOnlySource != ReadOnlySourceKind.Supplied || EncryptedLibraryDetector.Detect(root) is not { } build)
            return null;
        if (existing.Version is { Length: > 0 } supplied && build.Version is { Length: > 0 } installed &&
            !string.Equals(supplied, installed, StringComparison.Ordinal))
            return null;
        return existing.Version is { Length: > 0 } version ? version : build.Version ?? "its version is not known";
    }

    private static string SuppliedForThisBuild(string name, string path, LoadedLibrary supplied, string release) =>
        $"'{name}' at '{path}' is an encrypted build of the release the host supplied ({release}), so the supplied " +
        $"copy from '{supplied.SourcePath}' is kept and used instead: it is the vendor's own description of that " +
        "release, which MLQT can only approximate from the build's help pages. replace does not change this.";

    /// <summary>
    /// Puts <paramref name="existing"/> back when it was unloaded for a copy that then failed to load. A
    /// supplied library cannot be put back - only the host has its text - so its loss is an error.
    /// </summary>
    private async Task RestoreIfGoneAsync(LoadedLibrary? existing, string path)
    {
        if (existing is null || IsLoaded(existing))
            return;

        if (existing.ReadOnlySource == ReadOnlySourceKind.Supplied)
        {
            _errors.Add($"'{existing.Name}' was supplied from memory by the host, and was unloaded to make way for " +
                        $"'{path}', which then could not be loaded. Only the host that supplied it can supply it again.");
            return;
        }

        try
        {
            if (existing.RepositoryId is { } repositoryId && _repositories.GetRepository(repositoryId) is not null)
            {
                await _repositories.RefreshRepositoryAsync(repositoryId);
                return;
            }
            var back = await _libraries.AddLibraryFromPathAsync(existing.SourcePath);
            _session.LibraryOrigins[back.Id] = Explicit;
        }
        catch (ArgumentException)
        {
            _errors.Add($"'{existing.Name}' was unloaded to make way for '{path}', which then could not be loaded, " +
                        $"and '{existing.SourcePath}' could not be loaded again. Load it with mlqt_load.");
        }
    }

    private bool IsLoaded(LoadedLibrary library) => _libraries.Libraries.Any(l => l.Id == library.Id);

    private void Unload(LoadedLibrary library)
    {
        _libraries.RemoveLibrary(library.Id);
        _session.LibraryOrigins.Remove(library.Id);
    }

    // Each library the repository holds is one the caller named, as if its path had been given.
    private async Task LoadRepositoryAsync(Func<string, bool, Task<LoadRepositoryResult>> loadRepository, string root, bool replace)
    {
        if (DiscoverOnly)
        {
            var discovered = await loadRepository(root, false);
            _repositoriesLoaded.Add(discovered);
            if (!discovered.Success)
                _errors.Add(discovered.ErrorMessage ?? $"'{root}' could not be loaded as a repository.");
            return;
        }

        // Discovered first, by the names the libraries declare, so that a copy of a library loaded
        // elsewhere is refused as it would be by path - before the library service's precedence rule has
        // retired a supplied library for it, or the repository's copy has been loaded beside the first.
        var look = await loadRepository(root, false);
        if (!look.Success)
        {
            _repositoriesLoaded.Add(look);
            _errors.Add(look.ErrorMessage ?? $"'{root}' could not be loaded as a repository.");
            return;
        }
        var conflicts = new List<(DiscoveredLibrarySummary Discovered, LoadedLibrary Existing)>();
        // The same files loaded already, but not as this repository's - mlqt_create_library made the
        // library, or its folder was loaded by path. Nothing to refuse and nothing to choose: the
        // repository takes it over. Left alone, the repository loaded a second copy over the same class
        // ids, and unloading either took the other's classes with it.
        var adopted = new List<(DiscoveredLibrarySummary Discovered, LoadedLibrary Existing)>();
        foreach (var discovered in look.DiscoveredLibraries)
        {
            // Every loaded copy of that name, not the first: a copy of the same files must not hide one
            // elsewhere, which is a conflict whichever is found first.
            foreach (var existing in _libraries.Libraries
                         .Where(l => l.Name == discovered.LibraryName && l.RepositoryId != look.RepositoryId)
                         .ToList())
            {
                // Another repository cannot hold these files: discovery looks one folder deep, and the
                // same folder by another spelling is the same repository, which the query above leaves out.
                if (SamePath(existing.SourcePath, discovered.FullPath))
                {
                    adopted.Add((discovered, existing));
                    continue;
                }
                // As for a path: an encrypted build beside readable source is left to the source, not chosen between.
                if (!existing.IsReadOnly && EncryptedLibraryDetector.IsEncryptedLibraryRoot(discovered.FullPath))
                    continue;
                // And a build of the release the host supplied is left to the supplied copy, which the library
                // service keeps in its place as the repository loads.
                if (SuppliedDescribesThisBuild(existing, discovered.FullPath) is { } release)
                {
                    _warnings.Add(SuppliedForThisBuild(existing.Name, discovered.FullPath, existing, release));
                    continue;
                }
                conflicts.Add((discovered, existing));
            }
        }
        var refused = conflicts.Where(c => OriginOf(c.Existing) == Explicit && !replace).ToList();
        if (refused.Count > 0)
        {
            _repositoriesLoaded.Add(look);
            foreach (var (discovered, existing) in refused)
                _errors.Add(AnotherCopy(existing.Name, existing, discovered.FullPath));
            _errors.Add($"Nothing in '{root}' was loaded, because of the libraries above.");
            return;
        }

        // As in LoadInPlaceOfAsync: a copy on disk goes first, a supplied one only once the repository's has
        // loaded - or, where it outranks the installed build of its own release, once that was turned away.
        foreach (var (_, existing) in conflicts.Where(c => c.Existing.ReadOnlySource != ReadOnlySourceKind.Supplied))
            Unload(existing);
        foreach (var (_, existing) in adopted)
            Unload(existing);

        var result = await loadRepository(root, true);
        _repositoriesLoaded.Add(result);
        if (!result.Success)
        {
            _errors.Add(result.ErrorMessage ?? $"'{root}' could not be loaded as a repository.");
            foreach (var (discovered, existing) in conflicts.Concat(adopted))
                await RestoreIfGoneAsync(existing, discovered.FullPath);
            return;
        }

        foreach (var (discovered, existing) in adopted)
        {
            if (_libraries.Libraries.Any(l => l.RepositoryId == result.RepositoryId && l.Name == existing.Name))
                _warnings.Add($"'{existing.Name}' was already loaded from '{discovered.FullPath}'; it is now loaded as part of repository '{result.Name}', once.");
            else
                await RestoreIfGoneAsync(existing, discovered.FullPath);
        }

        foreach (var (discovered, existing) in conflicts)
        {
            var mine = _libraries.Libraries.FirstOrDefault(l => l.RepositoryId == result.RepositoryId && l.Name == existing.Name);
            if (mine is null && IsLoaded(existing))
            {
                Unload(existing);
                await _repositories.LoadLibrariesAsync(result.RepositoryId!,
                    look.DiscoveredLibraries.Where(d => d == discovered).Select(d => d.RelativePath));
                mine = _libraries.Libraries.FirstOrDefault(l => l.RepositoryId == result.RepositoryId && l.Name == existing.Name);
            }
            if (mine is null)
                await RestoreIfGoneAsync(existing, discovered.FullPath);
            else if (IsLoaded(existing))
                Unload(existing);
        }

        var count = _loaded.Count;
        foreach (var library in _libraries.Libraries.Where(l => l.RepositoryId == result.RepositoryId))
        {
            _session.LibraryOrigins[library.Id] = Explicit;
            _loaded.Add(Item(library, Explicit, $"in repository '{result.Name}'", null, null));
        }
        // A folder with no library in it, an empty one or a mistyped one, loaded nothing and said nothing.
        if (_loaded.Count == count && result.DiscoveredLibraries.Count == 0)
            _errors.Add($"No Modelica classes were found at '{root}'. Point mlqt_load at a library folder holding a " +
                        "package.mo, a single .mo file, or a working copy whose libraries live in subfolders.");
    }

    private async Task LoadDependenciesAsync(ToolFolders tool)
    {
        var modelicaPath = (_environment("MODELICAPATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var asked = new HashSet<string>(StringComparer.Ordinal);

        // Breadth first, so a library's own dependencies are looked for beside it once it is loaded.
        var queue = new Queue<LoadedLibrary>(_libraries.Libraries);
        while (queue.Count > 0)
        {
            var library = queue.Dequeue();
            foreach (var (dependency, versionText) in UsesOf(library))
            {
                if (!asked.Add($"{dependency}\u0000{versionText}"))
                    continue;
                var wanted = ModelicaVersion.Parse(versionText);
                if (_libraries.Libraries.FirstOrDefault(l => l.Name == dependency) is { } present)
                {
                    NoteVersion(present, dependency, wanted, library.Name);
                    continue;
                }

                var folders = new List<(string Folder, string FoundBy)>();
                foreach (var sibling in SiblingFolders(library))
                    folders.Add(sibling);
                folders.AddRange(tool.Folders.Select(f => (f, "the tool's library folder")));
                folders.AddRange(_extraFolders);
                folders.AddRange(modelicaPath.Select(f => (f, "MODELICAPATH")));

                var (chosen, foundBy, rule, seen) = LibraryChoice.Choose(dependency, wanted, folders);
                if (chosen is null)
                {
                    _notFound.Add(new MissingDependency(dependency, versionText, library.Name,
                        seen.Select(c => new CandidateItem(c.Path, c.Version?.Text)).ToList()));
                    continue;
                }

                var loaded = await TryLoadAsync(chosen.Path);
                if (loaded is null)
                    continue;
                _session.LibraryOrigins[loaded.Id] = Search;
                _loaded.Add(Item(loaded, Search, foundBy!, rule, library.Name));
                queue.Enqueue(loaded);
            }
        }
    }

    private void NoteVersion(LoadedLibrary present, string name, ModelicaVersion? wanted, string requiredBy)
    {
        if (wanted is null || ModelicaVersion.Parse(present.Version) is not { } have || have.SameReleaseAs(wanted))
            return;
        _warnings.Add(OriginOf(present) == Explicit
            ? $"{requiredBy} uses {name} {wanted.Text}, but {name} {have.Text} was loaded explicitly and is kept: it is what was asked for, so it is what the simulator uses."
            : $"{requiredBy} uses {name} {wanted.Text}, but {name} {have.Text} is already loaded. It was not replaced; load the one you want explicitly.");
    }

    // The folder that holds the library, then its repository's root.
    private IEnumerable<(string Folder, string FoundBy)> SiblingFolders(LoadedLibrary library)
    {
        var root = LibraryRoot(library.SourcePath) ?? library.SourcePath;
        if (Path.GetDirectoryName(root.TrimEnd('/', '\\')) is { } parent)
            yield return (parent, $"beside {library.Name}");
        if (library.RepositoryId is { } repositoryId &&
            _repositories.Repositories.FirstOrDefault(r => r.Id == repositoryId)?.LocalPath is { } repositoryRoot)
            yield return (repositoryRoot, $"in {library.Name}'s repository");
    }

    private IEnumerable<(string Name, string? Version)> UsesOf(LoadedLibrary library)
    {
        foreach (var topId in library.TopLevelModelIds)
        {
            if (_libraries.GetModelById(topId)?.Uses is not { } uses)
                continue;
            foreach (var (name, version) in uses)
                yield return (name, string.IsNullOrEmpty(version) ? null : version);
        }
    }

    private async Task<LoadedLibrary?> TryLoadAsync(string path) => Usable(await LoadRawAsync(path), path);

    // The library service's load, or null with the reason said; a library it turned away is returned.
    private async Task<LoadedLibrary?> LoadRawAsync(string path)
    {
        if (path.EndsWith(".moe", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            _warnings.Add($"'{path}' is an encrypted library in a single file. MLQT reads an encrypted library " +
                          "only as a folder holding a package.moe, from its generated documentation, so this one " +
                          "was not loaded; references into it will not resolve.");
            return null;
        }
        try
        {
            return await _libraries.AddLibraryFromPathAsync(path);
        }
        catch (ArgumentException)
        {
            _errors.Add($"'{path}' could not be loaded: not a library folder, a package.mo or a .mo file.");
            return null;
        }
    }

    // The library loaded, or null with the reason said when it was turned away or holds no classes.
    private LoadedLibrary? Usable(LoadedLibrary? library, string path)
    {
        if (library is null)
            return null;
        if (library.SupersededBy is { } source)
        {
            _warnings.Add(_libraries.Libraries.FirstOrDefault(l => l.SourcePath == source) is { IsReadOnly: true } keeper
                ? OutrankedByReadOnly(library.Name, path, keeper)
                : Superseded(library.Name, path, source));
            return null;
        }
        if (library.ModelIds.Count == 0)
        {
            _libraries.RemoveLibrary(library.Id);
            if (library.SourceType == LibrarySourceType.EncryptedDirectory)
                _warnings.Add($"'{library.Name}' at '{path}' is an encrypted library that ships no usable documentation, " +
                              "so none of its classes could be recovered; references into it will not resolve.");
            else
                _errors.Add($"No Modelica classes were found at '{path}'.");
            return null;
        }
        return library;
    }

    private static LoadedLibraryItem Item(LoadedLibrary library, string origin, string foundBy, string? rule, string? requiredBy) =>
        new(library.Name, library.Version, library.SourcePath, origin, foundBy, rule, requiredBy,
            library.SourceType == LibrarySourceType.EncryptedDirectory);

    /// <summary>The folder or file a path names as a library: a <c>package.mo</c> means its folder.</summary>
    internal static string? LibraryRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var full = Path.GetFullPath(path).TrimEnd('/', '\\');
        if (File.Exists(full))
            return Path.GetFileName(full) is "package.mo" or "package.moe" ? Path.GetDirectoryName(full) : full;
        return Directory.Exists(full) ? full : null;
    }

    /// <summary>A library's name from its folder or file: <c>Modelica 4.1.0+maint.om</c> is <c>Modelica</c>.</summary>
    internal static string NameFromPath(string root)
    {
        var leaf = Path.GetFileName(root);
        if (leaf.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            leaf = leaf[..^3];
        var space = leaf.IndexOf(' ');
        return space > 0 ? leaf[..space] : leaf;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

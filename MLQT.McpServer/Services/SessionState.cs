namespace MLQT.McpServer.Services;

/// <summary>
/// Tracks whether the opt-in, expensive analysis passes have been run this session, so query tools
/// can tell an empty result ("no dependencies") apart from "you haven't analyzed yet".
/// </summary>
public sealed class SessionState
{
    /// <summary>
    /// The repositories a loaded class may belong to, for the formatting rules an edit is written
    /// with (<see cref="Helpers.EditFormatting"/>). Here rather than on each edit tool because every
    /// write path is already handed the session; null leaves edits written as composed.
    /// </summary>
    /// <remarks>
    /// <b>Settable, and set by the server's call filter</b> when it is null
    /// (<see cref="Attach"/>). A host that registers a <see cref="SessionState"/> of its own wins over
    /// MLQT's TryAdd, and this cannot be set from outside the assembly, so such a session had no
    /// repositories and every class written through it went unformatted, while mlqt_format - which asks
    /// the repository service itself - formatted it.
    /// </remarks>
    internal MLQT.Services.Interfaces.IRepositoryService? Repositories { get; set; }

    /// <summary>
    /// Gives the session the server's repository service if it has none: every call passes here before
    /// its tool runs, so whoever registered the session, an edit is formatted as its repository says.
    /// </summary>
    internal void Attach(MLQT.Services.Interfaces.IRepositoryService? repositories)
    {
        if (Repositories is null && repositories is not null)
            Repositories = repositories;
    }

    public bool DependenciesAnalyzed { get; set; }
    public bool ResourcesAnalyzed { get; set; }

    /// <summary>
    /// How each library came in, by its id: <c>"explicit"</c> when the caller named its path to
    /// <c>mlqt_load</c>, <c>"search"</c> when <c>mlqt_load</c> found it for a <c>uses</c> annotation. An
    /// explicit library is what the user's simulator loaded, so it replaces a searched copy, and a search
    /// never replaces anything (<c>.claude/skills/mcp-library-loading/SKILL.md</c>, B503).
    /// </summary>
    internal Dictionary<string, string> LibraryOrigins { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How the library came in. One with no record was not loaded by <c>mlqt_load</c>'s search - a
    /// repository, a reload or a host supplied it - and counts as explicit, as the loader treats it.
    /// </summary>
    internal string OriginOf(string libraryId) =>
        LibraryOrigins.TryGetValue(libraryId, out var origin) ? origin : "explicit";

    /// <summary>
    /// The simulation tool install whose library folders the last dependency search used, or null when
    /// none has. A later <c>mlqt_load</c> naming another install searches again from it, in place of what
    /// the earlier search found: the newest install on a machine need not be the one running (B504).
    /// </summary>
    internal string? SearchInstall { get; set; }

    /// <summary>
    /// The explicit libraries a startup script found by name in the tool install's own library folder -
    /// <c>import Modelica</c>, <c>openModelFile("Modelica")</c> - by the full path they were loaded from.
    /// The script named the library, but the install chose the copy, so a load naming another install
    /// replaces them as it does what a search found. Counted as explicit, they stayed: a reload with the
    /// 2026x install kept the MSL and DymolaCommands the first load had taken from a 2027x beta (a 2026-11
    /// session). By path, not id, because <c>mlqt_session</c>'s reload loads a library again under a new id.
    /// </summary>
    internal HashSet<string> FoundInTheInstall { get; } =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Whether <paramref name="sourcePath"/> is a library <see cref="FoundInTheInstall"/>.</summary>
    internal bool IsFoundInTheInstall(string sourcePath) => FoundInTheInstall.Contains(InstallKey(sourcePath));

    /// <summary>The form of a library's path <see cref="FoundInTheInstall"/> holds.</summary>
    internal static string InstallKey(string sourcePath) => Path.GetFullPath(sourcePath).TrimEnd('/', '\\');

    // ---- When each library came in (B505) -------------------------------------------------------
    //
    // A server outlives a conversation - a client keeps it across /clear and new sessions - and can be
    // shared by tasks running at once, and MCP gives it no conversation id to tell them apart. What it
    // can do is say how old what it holds is, so an agent that did not load a library can see it did not.

    private readonly Dictionary<string, DateTimeOffset> _loadedAt = new(StringComparer.Ordinal);

    /// <summary>The clock load times are read from. A test sets it.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Records the first time each of <paramref name="libraryIds"/> is seen, and forgets any no longer
    /// loaded. Called around every tool call, so a library is timed by the call that loaded it, whatever
    /// loaded it - a load tool, a repository, a host supplying it.
    /// </summary>
    internal void NoteLibraries(IEnumerable<string> libraryIds)
    {
        var ids = libraryIds.ToHashSet(StringComparer.Ordinal);
        lock (_loadedAt)
        {
            var now = Clock();
            foreach (var id in ids)
                _loadedAt.TryAdd(id, now);
            foreach (var gone in _loadedAt.Keys.Where(k => !ids.Contains(k)).ToList())
                _loadedAt.Remove(gone);
        }
    }

    /// <summary>When the library was first seen loaded; now, if this is the first time it is asked about.</summary>
    internal DateTimeOffset LoadedAt(string libraryId)
    {
        lock (_loadedAt)
        {
            if (!_loadedAt.TryGetValue(libraryId, out var at))
                _loadedAt[libraryId] = at = Clock();
            return at;
        }
    }

    /// <summary>How long ago the library came in, for a sentence: "34 minutes ago".</summary>
    internal string LoadedAgo(string libraryId)
    {
        var age = Clock() - LoadedAt(libraryId);
        return age.TotalMinutes < 1 ? "less than a minute ago"
            : age.TotalHours < 2 ? $"{(int)age.TotalMinutes} minute{((int)age.TotalMinutes == 1 ? "" : "s")} ago"
            : $"{(int)age.TotalHours} hours ago";
    }

    /// <summary>Forgets everything this session knew about what is loaded: <c>mlqt_session</c>'s reset.</summary>
    internal void Reset()
    {
        LibraryOrigins.Clear();
        FoundInTheInstall.Clear();
        SearchInstall = null;
        DependenciesAnalyzed = false;
        ResourcesAnalyzed = false;
        lock (_loadedAt)
            _loadedAt.Clear();
    }
}

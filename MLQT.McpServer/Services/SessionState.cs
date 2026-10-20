namespace MLQT.McpServer.Services;

/// <summary>
/// Tracks whether the opt-in, expensive analysis passes have been run this session, so query tools
/// can tell an empty result ("no dependencies") apart from "you haven't analyzed yet".
/// </summary>
public sealed class SessionState
{
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
    /// The simulation tool install whose library folders the last dependency search used, or null when
    /// none has. A later <c>mlqt_load</c> naming another install searches again from it, in place of what
    /// the earlier search found: the newest install on a machine need not be the one running (B504).
    /// </summary>
    internal string? SearchInstall { get; set; }

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
        SearchInstall = null;
        DependenciesAnalyzed = false;
        ResourcesAnalyzed = false;
        lock (_loadedAt)
            _loadedAt.Clear();
    }
}

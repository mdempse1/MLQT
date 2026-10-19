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
    /// never replaces anything (<c>Design/mcp-tool-consolidation.md</c>, B503).
    /// </summary>
    internal Dictionary<string, string> LibraryOrigins { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The simulation tool install whose library folders the last dependency search used, or null when
    /// none has. A later <c>mlqt_load</c> naming another install searches again from it, in place of what
    /// the earlier search found: the newest install on a machine need not be the one running (B504).
    /// </summary>
    internal string? SearchInstall { get; set; }
}

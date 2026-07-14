namespace MLQT.McpServer.Services;

/// <summary>
/// Tracks whether the opt-in, expensive analysis passes have been run this session, so query tools
/// can tell an empty result ("no dependencies") apart from "you haven't analyzed yet".
/// </summary>
public sealed class SessionState
{
    public bool DependenciesAnalyzed { get; set; }
    public bool ResourcesAnalyzed { get; set; }
}

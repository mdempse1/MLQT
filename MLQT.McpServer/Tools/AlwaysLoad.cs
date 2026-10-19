namespace MLQT.McpServer.Tools;

/// <summary>
/// The tool <c>_meta</c> key that asks a client to load a tool's definition up front even when it defers
/// MCP tools until they are searched for, as Claude Code's Tool Search does. Kept to the tools an agent
/// needs before it knows to search: loading, and finding (B503).
/// </summary>
internal static class AlwaysLoad
{
    public const string Key = "anthropic/alwaysLoad";
}

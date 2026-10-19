namespace MLQT.McpServer;

/// <summary>
/// Which set of tools the server registers (B503). <see cref="Current"/> is the set every release has
/// shipped; <see cref="Consolidated"/> is the 16-tool set of <c>Design/mcp-tool-consolidation.md</c>.
/// </summary>
/// <remarks>
/// This exists only to measure the two sets against each other, and is deleted with the current set at
/// the cut-over rather than kept as a compatibility mode.
/// </remarks>
public enum McpToolset
{
    /// <summary>The tools every release so far has shipped, and those step 1 added beside them.</summary>
    Current,

    /// <summary>The 16 tools the current set is being consolidated into.</summary>
    Consolidated,
}

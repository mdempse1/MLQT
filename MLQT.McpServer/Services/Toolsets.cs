namespace MLQT.McpServer.Services;

/// <summary>
/// Which tools each <see cref="McpToolset"/> registers, by name. A name, not a class: the two sets share
/// tools, and a shared tool can sit in a class beside one that only the current set has.
/// </summary>
internal static class Toolsets
{
    /// <summary>The 16 tools of the consolidated set (<c>Design/mcp-tool-consolidation.md</c>).</summary>
    internal static readonly IReadOnlySet<string> Consolidated = new HashSet<string>(StringComparer.Ordinal)
    {
        "mlqt_load",
        "mlqt_session",
        "mlqt_create_library",
        "mlqt_find",
        "mlqt_browse",
        "mlqt_get_class",
        "mlqt_get_diagram_image",
        "mlqt_write_class",
        "mlqt_edit_class",
        "mlqt_refactor_class",
        "mlqt_format",
        "mlqt_check",
        "mlqt_list_findings",
        "mlqt_style_settings",
        "mlqt_dependencies",
        "mlqt_get_guidance",
    };

    /// <summary>
    /// The consolidated tools the current set does not have: the facades over its tools. Every other
    /// consolidated tool is in both, so the current set is every tool but these.
    /// </summary>
    internal static readonly IReadOnlySet<string> ConsolidatedOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        "mlqt_session",
        "mlqt_browse",
        "mlqt_get_class",
        "mlqt_refactor_class",
        "mlqt_check",
        "mlqt_style_settings",
        "mlqt_dependencies",
    };

    /// <summary>Whether <paramref name="toolset"/> registers the tool named <paramref name="name"/>.</summary>
    internal static bool Includes(McpToolset toolset, string name) => toolset switch
    {
        McpToolset.Consolidated => Consolidated.Contains(name),
        _ => !ConsolidatedOnly.Contains(name),
    };

    /// <summary>
    /// The set <c>MLQT_MCP_TOOLSET</c> names: <c>consolidated</c>, or <c>current</c> or nothing for the
    /// current set. Read without <c>Enum.Parse</c>, whose text an obfuscated build would not have. An
    /// unknown value is null, for the caller to report.
    /// </summary>
    internal static McpToolset? FromEnvironment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "current" => McpToolset.Current,
        "consolidated" => McpToolset.Consolidated,
        _ => null,
    };
}

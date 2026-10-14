using MLQT.McpServer.Services;
using ModelContextProtocol.Server;

namespace MLQT.McpServer;

/// <summary>
/// What a host may change about the server <see cref="MlqtMcpServerExtensions.AddMlqtMcpServer"/>
/// builds. Everything else is the host's own business: the transport, logging, settings storage, and
/// any further tools or filters, which go on the services or on the builder it returns.
/// </summary>
public sealed class MlqtMcpServerOptions
{
    /// <summary>
    /// Where each tool call is recorded, or null to record nothing. MLQT's own server passes one, which
    /// reads its marker file when it is constructed; with null nothing is constructed and no file read.
    /// </summary>
    public ToolUsageLogger? ToolUsageLogger { get; init; }

    /// <summary>
    /// Runs after MLQT has set its own server options, so a host can replace
    /// <see cref="McpServerOptions.ServerInstructions"/> - which a host whose tools differ from MLQT's
    /// usually should, since MLQT's describe loading and editing - or change anything else. Claude Code
    /// keeps only about the first 2,048 characters of a server's instructions and MLQT's use 1,892 of
    /// them, so text appended to them is the text that gets dropped.
    /// </summary>
    public Action<McpServerOptions>? ConfigureServer { get; init; }
}

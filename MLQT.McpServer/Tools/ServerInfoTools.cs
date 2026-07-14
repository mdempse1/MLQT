using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Basic server introspection tools. Also serves as the Phase 0 end-to-end smoke test that
/// dependency injection and the MCP stdio transport are wired correctly.
/// </summary>
[McpServerToolType]
public sealed class ServerInfoTools
{
    private readonly ILibraryDataService _libraryData;

    public ServerInfoTools(ILibraryDataService libraryData) => _libraryData = libraryData;

    [McpServerTool(Name = "server_info")]
    [Description("Returns basic MLQT MCP server status: how many libraries are currently loaded " +
                 "and their names. Use this to confirm the server is running and whether a library " +
                 "has been loaded yet (most other tools require a loaded library).")]
    public object GetServerInfo()
    {
        var libraries = _libraryData.Libraries;
        return new
        {
            server = "MLQT MCP Server",
            librariesLoaded = libraries.Count,
            libraryNames = libraries.Select(l => l.Name).ToArray(),
        };
    }
}

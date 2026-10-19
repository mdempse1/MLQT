using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// <c>mlqt_load</c>: load libraries by path, and what they depend on. The dependency search is new (B503
/// step 1): without it an agent loads only the library it was pointed at, and then reads the MSL straight
/// from a simulator's install folder, as the 2026-10-18 sessions did.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class LoadTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly SessionState _session;

    public LoadTools(ILibraryDataService libraries, IRepositoryService repositories, SessionState session)
    {
        _libraries = libraries;
        _repositories = repositories;
        _session = session;
    }

    /// <summary>Where the desktop app keeps its settings, read for the tool's install path. A test sets it.</summary>
    internal string? DesktopSettingsDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MLQT");

    /// <summary>The environment read for MODELICAPATH and OpenModelica's folders. A test sets it.</summary>
    internal Func<string, string?> Environment_ { get; set; } = Environment.GetEnvironmentVariable;

    [McpServerTool(Name = "mlqt_load")]
    [Description("Load Modelica libraries into MLQT by path, and what they depend on. Pass every library the " +
                 "user's simulator has loaded: they are loaded first, as given, and win over any other copy - " +
                 "a library already loaded by an earlier search is replaced. With OpenModelica, get them from " +
                 "getLoadedLibraries(); with Dymola, which cannot say, from the user's startup script. Then, with " +
                 "withDependencies (default true), each library their uses annotations name and nothing has " +
                 "loaded is searched for: beside the library that needs it, in its repository, in the simulation " +
                 "tool's library folders (tool: 'dymola' or 'openmodelica'; tool_install picks which install), " +
                 "then on MODELICAPATH. The exact version wins, else the newest copy declaring " +
                 "noneFromVersion for it, else nothing is loaded and every copy seen is listed. The answer " +
                 "names each library, its version, path and how it was found.")]
    public async Task<object> Load(
        [Description("Library folders, package.mo files or single .mo files, in the order to load them.")]
        IReadOnlyList<string> paths,
        [Description("Also load what these libraries' uses annotations name. Default true.")] bool withDependencies = true,
        [Description("Whose library folders to search: 'dymola', 'openmodelica' or 'none' (default).")] string? tool = null,
        [Description("The tool's install folder or executable, when it is not the one in MLQT's settings or the newest.")]
        string? toolInstall = null,
        [Description("Load a path even when another copy of that library was loaded explicitly. Default false.")]
        bool replace = false)
    {
        if (paths is null || paths.Count == 0)
            return new ToolError("paths must name at least one library folder, package.mo or .mo file.");
        if (tool is not null && tool.Trim().ToLowerInvariant() is not ("" or "none" or "dymola" or "openmodelica"))
            return new ToolError($"tool must be 'dymola', 'openmodelica' or 'none', not '{tool}'.");

        var folders = ToolLibraryFolders.For(tool, toolInstall, DesktopSettingsDirectory, Environment_);
        var loader = new DependencyLoader(_libraries, _repositories, _session, Environment_);
        return await loader.LoadAsync(paths, withDependencies, replace, folders);
    }
}

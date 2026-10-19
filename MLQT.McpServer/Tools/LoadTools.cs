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
                 "getLoadedLibraries(); with Dymola, which cannot say, pass the user's startup script as script and " +
                 "the libraries it opens are loaded (it is read, not run; lines it cannot work out are listed). Then, with " +
                 "withDependencies (default true), each library their uses annotations name and nothing has " +
                 "loaded is searched for: beside the library that needs it, in its repository, in the simulation " +
                 "tool's library folders (tool: 'dymola' or 'openmodelica'; tool_install picks which install), " +
                 "then on MODELICAPATH. The exact version wins, else the newest copy declaring " +
                 "noneFromVersion for it, else nothing is loaded and every copy seen is listed. The answer " +
                 "names each library, its version, path and how it was found.")]
    public async Task<object> Load(
        [Description("Library folders, package.mo files or single .mo files, in the order to load them.")]
        IReadOnlyList<string>? paths = null,
        [Description("Also load what these libraries' uses annotations name. Default true.")] bool withDependencies = true,
        [Description("Whose library folders to search: 'dymola', 'openmodelica' or 'none' (default).")] string? tool = null,
        [Description("The tool's install folder or executable, when it is not the one in MLQT's settings or the newest.")]
        string? toolInstall = null,
        [Description("Load a path even when another copy of that library was loaded explicitly. Default false.")]
        bool replace = false,
        [Description("A Dymola startup script (.mos) to read, not run: the libraries it opens are loaded as if " +
                     "named in paths, before them.")]
        string? script = null,
        [Description("For script: the folder Dymola starts in, for the script's relative paths. Default: the " +
                     "script's own folder.")]
        string? startDirectory = null)
    {
        if ((paths is null || paths.Count == 0) && string.IsNullOrWhiteSpace(script))
            return new ToolError("Name at least one library in paths, or a startup script in script.");
        if (tool is not null && tool.Trim().ToLowerInvariant() is not ("" or "none" or "dymola" or "openmodelica"))
            return new ToolError($"tool must be 'dymola', 'openmodelica' or 'none', not '{tool}'.");

        var folders = ToolLibraryFolders.For(tool, toolInstall, DesktopSettingsDirectory, Environment_);
        var loader = new DependencyLoader(_libraries, _repositories, _session, Environment_);

        var all = new List<string>();
        ScriptReadingResult? reading = null;
        if (!string.IsNullOrWhiteSpace(script))
        {
            if (!File.Exists(script))
                return new ToolError($"The startup script '{script}' does not exist.");
            // Dymola's ModelicaPath before the script runs: its own library folder, then MODELICAPATH.
            var modelicaPath = folders.Folders.Concat((Environment_("MODELICAPATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var read = new StartupScriptReader(Environment_, modelicaPath).Read(script, startDirectory);
            reading = new ScriptReadingResult(read.Script,
                read.Loads.Select(l => new ScriptLoadItem(l.Path, l.Line, l.Via)).ToList(),
                read.Ignored.Select(l => new ScriptLineItem(l.File, l.Line, l.Text, l.Reason)).ToList(),
                read.Unresolved.Select(l => new ScriptLineItem(l.File, l.Line, l.Text, l.Reason)).ToList(),
                read.CurrentDirectory, read.ModelicaPathSet, read.ModelicaPathAdded);
            all.AddRange(read.Loads.Select(l => l.Path));
            loader.AddSearchFolders(read.ModelicaPathAdded, "the startup script's ModelicaPath");
            if (read.ModelicaPathSet is { } set)
                loader.AddSearchFolders(set.Split(Path.PathSeparator), "the startup script's ModelicaPath");
        }
        if (paths is not null)
            all.AddRange(paths);

        var result = await loader.LoadAsync(all, withDependencies, replace, folders);
        return result with { Script = reading };
    }
}

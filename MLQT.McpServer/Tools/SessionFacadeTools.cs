using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Three tools of the consolidated set (B503): <c>mlqt_session</c>, <c>mlqt_browse</c> and
/// <c>mlqt_style_settings</c>. Each is a facade over the current tools it replaces, which it calls rather
/// than repeats, so the two sets cannot drift while they are measured against each other.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class SessionFacadeTools
{
    private readonly SessionTools _sessionTools;
    private readonly ClassQueryTools _classes;
    private readonly StyleTools _style;

    public SessionFacadeTools(ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, ICodeReviewService codeReview, ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager, SessionState session)
    {
        _sessionTools = new SessionTools(libraries, repositories, resources, session, codeReview);
        _classes = new ClassQueryTools(libraries);
        _style = new StyleTools(libraries, codeReview, repositories, customDictionary, dictionaryManager, session);
    }

    [McpServerTool(Name = "mlqt_session", ReadOnly = true, OpenWorld = false)]
    [Description("The Modelica libraries MLQT has loaded, and changing that. action 'status' (the default): each library loaded, " +
                 "its version, class count and the dependencies it declares that are not loaded, and each " +
                 "repository with its branch and revision - an empty list means nothing is loaded yet, so call " +
                 "mlqt_load. Each library says when it was loaded: this server may have outlived the conversation " +
                 "that loaded it, or serve another task. 'unload': remove the library named by library (its " +
                 "name, e.g. 'Modelica', or id). 'reset': unload everything and forget the findings, to start " +
                 "new work from empty. " +
                 "'reload': re-read from disk what changed outside MLQT - target is a .mo file, or a library " +
                 "or repository by name or id, or nothing for everything.")]
    public async Task<object> Session(
        [Description("'status' (default), 'unload', 'reload' or 'reset'.")] string action = "status",
        [Description("For 'unload': the library's name or id.")] string? library = null,
        [Description("For 'reload': a .mo file, or a library or repository name or id. Omit for everything.")]
        string? target = null,
        [Description("For 'status': list every declared dependency, loaded or not. Default false.")] bool detailed = false)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case null or "" or "status":
                return new SessionStatus(_sessionTools.ListLibraries(detailed), _sessionTools.ListRepositories());
            case "unload":
                return string.IsNullOrWhiteSpace(library)
                    ? new ToolError("'unload' needs library: the name (e.g. 'Modelica') or id of the library to unload.")
                    : _sessionTools.UnloadLibrary(library);
            case "reload":
                return await _sessionTools.Reload(target);
            case "reset":
                return _sessionTools.Reset();
            default:
                return new ToolError($"action must be 'status', 'unload', 'reload' or 'reset', not '{action}'.");
        }
    }

    [McpServerTool(Name = "mlqt_browse", ReadOnly = true, OpenWorld = false)]
    [Description("The package tree of the loaded Modelica libraries, from root (a class id) or from every library's " +
                 "top, depth levels down: each class's id, kind and description, and how many children it " +
                 "has. A large tree is cut at maxNodes, and says so; browse a sub-package to see more.")]
    public object Browse(
        [Description("Optional: the class to start from, e.g. 'Modelica.Blocks'. Omit for every library's top.")]
        string? root = null,
        [Description("How many levels to expand (default 1, max 8).")] int depth = 1,
        [Description("Most classes to return (default 200).")] int maxNodes = ClassQueryTools.DefaultTreeNodes)
        => _classes.GetPackageTree(root, depth, maxNodes);

    [McpServerTool(Name = "mlqt_style_settings", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("A repository's Modelica style-checking rules and spell-check languages, from its .mlqt/settings.json. " +
                 "With no settings, returns them. With settings, merges them in and writes the file - pass only " +
                 "the rules you are changing; the rest are kept. With one repository loaded, repositoryId is " +
                 "optional. A library loaded with no repository reports the built-in defaults, and has no file " +
                 "to write.")]
    public async Task<object> StyleSettings(
        [Description("Optional: the rules to change, and spellCheckLanguages. Omit to read the settings.")]
        StyleSettingsInput? settings = null,
        [Description("Optional repository id or name. Omit when a single repository is loaded.")]
        string? repositoryId = null)
        => settings is null
            ? _style.GetStyleSettings(repositoryId)
            : await _style.SetStyleSettings(settings, repositoryId);
}

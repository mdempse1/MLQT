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
                 "repository with its branch, its revision and formatsWrites (whether what you write there is " +
                 "formatted; if not, run mlqt_format when done) - an empty list means nothing is loaded yet, so call " +
                 "mlqt_load. Each library says when it was loaded: this server may have outlived the conversation " +
                 "that loaded it, or serve another task. 'unload': remove the library named by library (its " +
                 "name, e.g. 'Modelica', or id). 'reset': unload everything and forget the findings, to start " +
                 "new work from empty. " +
                 "'reload': re-read from disk what changed outside MLQT - target is a .mo file, or a library " +
                 "or repository by name or id, or nothing for everything; any library another copy took the " +
                 "place of is listed under replaced.")]
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
                 "top, depth levels down, each package's children in its package.order: each class's id, name, " +
                 "kind and how many children it has. The starting classes are always listed; below them at most " +
                 "maxNodes classes come back in all, and fewer when their ids are long, so the reply stays a size " +
                 "a client takes. When the tree is bigger, truncated is true and note names the packages not " +
                 "expanded - browse one of them as root. To find a class by name or purpose, mlqt_find is " +
                 "quicker than browsing deep.")]
    public object Browse(
        [Description("Optional: the class to start from, e.g. 'Modelica.Blocks'. Omit to start from every loaded " +
                     "library's top-level class; depth then expands each of them.")]
        string? root = null,
        [Description("How many levels below the start to expand (default 1, max 8). Each level can multiply the " +
                     "classes returned, so go deep from a narrow root rather than from every library.")] int depth = 1,
        [Description("Most classes to return, every level counted (default 200, max 500).")] int maxNodes = ClassQueryTools.DefaultTreeNodes)
        => _classes.GetPackageTree(root, depth, maxNodes);

    [McpServerTool(Name = "mlqt_style_settings", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("A repository's Modelica style-checking rules, spell-check languages and accepted spellings (its " +
                 ".mlqt/settings.json and .mlqt/dictionary.txt). With nothing to change, returns them. With settings, " +
                 "merges them in and writes the file - pass only the rules you are changing; the rest are kept. " +
                 "acceptSpellings adds words the spell check accepts in every class of the repository - names, " +
                 "symbols, file extensions, domain terms used throughout - and removeSpellings takes them out; " +
                 "for a word right in one class only, use mlqt_edit_class's accept_spelling. With one repository " +
                 "loaded, repositoryId is optional. A library loaded with no repository reports the built-in " +
                 "defaults, and has no files to write.")]
    public async Task<object> StyleSettings(
        [Description("Optional: the rules to change, and spellCheckLanguages. Omit to read the settings.")]
        StyleSettingsInput? settings = null,
        [Description("Optional repository id or name. Omit when a single repository is loaded.")]
        string? repositoryId = null,
        [Description("Optional: words to accept in every class of the repository, one word each.")]
        IReadOnlyList<string>? acceptSpellings = null,
        [Description("Optional: words to stop accepting.")]
        IReadOnlyList<string>? removeSpellings = null)
        => settings is null && (acceptSpellings?.Count ?? 0) == 0 && (removeSpellings?.Count ?? 0) == 0
            ? _style.GetStyleSettings(repositoryId)
            : await _style.SetStyleSettings(settings, repositoryId, acceptSpellings, removeSpellings);
}

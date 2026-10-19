using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// <c>mlqt_find</c>: one tool for finding classes, by name, by documentation text, by interface shape,
/// or by a line of their code. The first three are the existing search tools, called rather than
/// repeated; <c>code</c> is new (B503 step 1), and it is the reason this tool exists before the others
/// are merged into it.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class FindTools
{
    private readonly ILibraryDataService _libraries;
    private readonly ClassQueryTools _classes;
    private readonly SearchTools _search;

    public FindTools(ILibraryDataService libraries)
    {
        _libraries = libraries;
        _classes = new ClassQueryTools(libraries);
        _search = new SearchTools(libraries);
    }

    [McpServerTool(Name = "mlqt_find", ReadOnly = true, OpenWorld = false)]
    [Description("Find Modelica classes in the libraries loaded into MLQT. match picks how: " +
                 "'name' (default) - class ids containing query, or with no query a page of every class, " +
                 "filtered by library and classType; 'text' - description or documentation prose containing " +
                 "query; 'interface' - by shape (classType, hasExperiment, minParameters, minConnectors, and " +
                 "query as part of the id); 'code' - every line of class source containing query, e.g. " +
                 "'Placement', 'connect(motor.', 'parameter Real T' - with the class it is in and its file line. " +
                 "Use code instead of grep or your own file search: it covers every loaded library, MSL included, " +
                 "and names the innermost class for each line. For code, regex=true reads query as a regular " +
                 "expression and ignoreCase=true ignores case; library limits it to one library. " +
                 "limit and offset page the results.")]
    public object Find(
        [Description("What to look for. For 'name' and 'interface', part of a class id; for 'text', prose; for 'code', text in a line of source.")]
        string? query = null,
        [Description("'name' (default), 'text', 'interface' or 'code'.")] string match = "name",
        [Description("Optional: one library, by name (e.g. 'Modelica') or id. For 'name' with no query, and for 'code'.")]
        string? library = null,
        [Description("Optional class type, e.g. 'model', 'block', 'connector'. For 'name' with no query, and 'interface'.")]
        string? classType = null,
        [Description("For 'interface': require (true) or exclude (false) an experiment() annotation.")] bool? hasExperiment = null,
        [Description("For 'interface': the least number of parameters.")] int minParameters = 0,
        [Description("For 'interface': the least number of connectors.")] int minConnectors = 0,
        [Description("For 'code': read query as a regular expression. Default false.")] bool regex = false,
        [Description("For 'code': ignore case. Default false, since Modelica is case-sensitive.")] bool ignoreCase = false,
        [Description("Most results to return (default 50).")] int limit = 50,
        [Description("Results to skip, for the next page (default 0). For 'name' with no query, and 'code'.")] int offset = 0)
    {
        switch (match?.Trim().ToLowerInvariant())
        {
            case null or "" or "name":
                return string.IsNullOrWhiteSpace(query)
                    ? _classes.ListClasses(library, classType, limit, offset)
                    : _classes.SearchClasses(query, limit);
            case "text":
                return _search.SearchText(query ?? string.Empty, limit);
            case "interface":
                return _search.SearchByInterface(classType, query, hasExperiment, minParameters, minConnectors, limit);
            case "code":
                if (ToolDiagnostics.RequireLibrary(_libraries, "searching code") is { } noLib)
                    return noLib;
                return CodeSearch.Run(_libraries, query ?? string.Empty, regex, ignoreCase, library,
                    Math.Clamp(limit, 1, CodeSearch.MaxLimit), Math.Max(offset, 0));
            default:
                return new Dtos.ToolError($"match must be 'name', 'text', 'interface' or 'code', not '{match}'.");
        }
    }
}

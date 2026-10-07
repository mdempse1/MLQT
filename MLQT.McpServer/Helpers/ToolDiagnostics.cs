using MLQT.McpServer.Dtos;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Produces state-aware <see cref="ToolError"/> messages. The server knows whether a library is
/// loaded and whether the opt-in analysis has run, so a failing tool can tell the caller the exact
/// next step (load a library, run mlqt_analyze_dependencies, fix the id) rather than a generic message.
/// </summary>
internal static class ToolDiagnostics
{
    /// <summary>
    /// What to say when nothing is loaded, whatever the tool. The likeliest reason is not that the
    /// agent forgot to load anything: it loaded the library into a simulator's server beside this one
    /// (OpenModelica's load_library) and took that to have loaded it here. So this says that it did
    /// not, and how to get the path the simulator already knows.
    /// </summary>
    public static string NothingLoaded(string whatFor) =>
        $"Nothing is loaded into MLQT, and {whatFor} needs a loaded library. MLQT is a separate server with its own " +
        "session: a library loaded into a simulator (e.g. OpenModelica's load_library) is not loaded here. " +
        "Load each library you need with mlqt_load_library, giving its directory, its package.mo or a single " +
        ".mo file (mlqt_load_repository for a Git/SVN working copy), and its dependencies too - usually the " +
        "Modelica Standard Library. If a simulator already has the library loaded, ask it for the path: in " +
        "OpenModelica, getLoadedLibraries() lists each loaded library with its directory.";

    /// <summary>Why a class id could not be resolved, tailored to whether anything is loaded.</summary>
    public static ToolError ClassNotFound(ILibraryDataService libraries, string classId)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(NothingLoaded($"resolving class '{classId}'"));

        return new ToolError(
            $"No class with id '{classId}' in the loaded libraries. Class ids are fully-qualified dotted " +
            "names (e.g. 'Modelica.Blocks.Continuous.Integrator'); use mlqt_search_classes to find it, or " +
            "mlqt_list_classes / mlqt_get_package_tree to browse.");
    }

    /// <summary>Guard for tools that need a loaded library. Returns null when OK to proceed.</summary>
    public static ToolError? RequireLibrary(ILibraryDataService libraries, string whatFor)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(NothingLoaded(whatFor));
        return null;
    }

    /// <summary>Message for tools that need mlqt_analyze_dependencies to have run. Adapts to whether a
    /// library is even loaded yet.</summary>
    public static ToolError NotAnalyzed(ILibraryDataService libraries, string whatFor)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(NothingLoaded(whatFor) + " Then run mlqt_analyze_dependencies.");

        return new ToolError(
            $"Dependencies have not been analyzed yet. Run mlqt_analyze_dependencies first (it builds the " +
            $"dependency graph and external-resource index), then {whatFor}.");
    }
}

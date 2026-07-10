using MLQT.McpServer.Dtos;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Produces state-aware <see cref="ToolError"/> messages. The server knows whether a library is
/// loaded and whether the opt-in analysis has run, so a failing tool can tell the caller the exact
/// next step (load a library, run analyze_dependencies, fix the id) rather than a generic message.
/// </summary>
internal static class ToolDiagnostics
{
    /// <summary>Why a class id could not be resolved, tailored to whether anything is loaded.</summary>
    public static ToolError ClassNotFound(ILibraryDataService libraries, string classId)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(
                $"No library is loaded, so class '{classId}' cannot be resolved. Load one first with " +
                "load_repository (a Git/SVN working copy or a directory of libraries) or load_library " +
                "(a single library directory or .mo file), then retry.");

        return new ToolError(
            $"No class with id '{classId}' in the loaded libraries. Class ids are fully-qualified dotted " +
            "names (e.g. 'Modelica.Blocks.Continuous.Integrator'); use search_classes to find it, or " +
            "list_classes / get_package_tree to browse.");
    }

    /// <summary>Guard for tools that need a loaded library. Returns null when OK to proceed.</summary>
    public static ToolError? RequireLibrary(ILibraryDataService libraries, string whatFor)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(
                $"No library is loaded. Load one with load_repository or load_library before {whatFor}.");
        return null;
    }

    /// <summary>Message for tools that need analyze_dependencies to have run. Adapts to whether a
    /// library is even loaded yet.</summary>
    public static ToolError NotAnalyzed(ILibraryDataService libraries, string whatFor)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(
                $"No library is loaded. Load one (load_repository / load_library) and then run " +
                $"analyze_dependencies before {whatFor}.");

        return new ToolError(
            $"Dependencies have not been analyzed yet. Run analyze_dependencies first (it builds the " +
            $"dependency graph and external-resource index), then {whatFor}.");
    }
}

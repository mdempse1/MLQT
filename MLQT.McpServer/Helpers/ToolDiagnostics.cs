using ModelicaParser.Helpers;
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

    /// <summary>How to look for a class whose id is not known, said the same way wherever it is said.</summary>
    private const string SearchTools =
        "Find it by name with mlqt_search_classes, by its description or documentation with " +
        "mlqt_search_text, or by shape with mlqt_search_by_interface.";

    /// <summary>The most near-matches a not-found message lists, so it stays a message.</summary>
    internal const int MaxSuggestions = 5;

    /// <summary>
    /// Why a class id could not be resolved, and the next step for each reason - which are different
    /// steps, so one message for all of them sent an agent to search a library that was never loaded:
    /// <list type="bullet">
    /// <item>nothing is loaded into MLQT at all;</item>
    /// <item>the library the id starts with is not loaded, though others are;</item>
    /// <item>the library is loaded and has no such class - in which case the deepest package of the id
    /// that does exist is named, since that is where to look.</item>
    /// </list>
    /// Each also offers the classes the id most likely meant: the same id in another case (Modelica
    /// names are case-sensitive), and classes elsewhere with the same last name, which is what an id
    /// missing its leading packages (<c>Continuous.Integrator</c>) finds.
    /// </summary>
    public static ToolError ClassNotFound(ILibraryDataService libraries, string classId)
    {
        if (libraries.Libraries.Count == 0)
            return new ToolError(NothingLoaded($"resolving class '{classId}'"));

        var (otherCase, sameLeaf) = NearMatches(libraries, classId);
        var root = ModelicaName.RootLibraryOf(classId);

        // The same id in another case is a typo, not a library to load: say only that.
        if (otherCase is not null)
            return new ToolError(
                $"No class '{classId}'. Did you mean '{otherCase}'? Modelica names are case-sensitive.");

        if (libraries.GetModelById(root) is null)
        {
            var loaded = libraries.Libraries.Select(l => l.Name).Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
            var loadedList = loaded.Count <= 10
                ? string.Join(", ", loaded)
                : string.Join(", ", loaded.Take(10)) + $" and {loaded.Count - 10} more";
            var howToLoad =
                "load it with mlqt_load_library (its directory or package.mo) - a library a simulator has " +
                "loaded is not loaded here; in OpenModelica, getLoadedLibraries() gives its path.";

            // A class of that name exists: the id most likely lost its leading packages, and that class
            // is the answer. A library called after the id's first segment is the less likely reading.
            if (sameLeaf.Count > 0)
                return new ToolError(
                    $"No class '{classId}' is loaded into MLQT. {SameLeafSentence(sameLeaf, classId)} " +
                    "Class ids are fully-qualified dotted names, starting with the library's name. If you " +
                    $"meant a library '{root}', which is not loaded (loaded: {loadedList}), {howToLoad}");

            var noRoot = ModelicaName.IsSimple(classId)
                ? $"No class or library '{classId}' is loaded into MLQT. Class ids are fully-qualified dotted " +
                  "names (e.g. 'Modelica.Blocks.Continuous.Integrator'), starting with the library's name."
                : $"Library '{root}' is not loaded into MLQT, so '{classId}' cannot be found.";

            return new ToolError(
                $"{noRoot} Loaded: {loadedList}. If '{root}' is a library, {howToLoad} {SearchTools}");
        }

        var nearest = ModelicaName.EnclosingNamesOf(classId).FirstOrDefault(n => libraries.GetModelById(n) is not null);
        var where = nearest is null || nearest == root
            ? $"Library '{root}' is loaded, but has no class '{classId}'."
            : $"Library '{root}' is loaded and has '{nearest}', but no class '{classId}' in it - " +
              $"mlqt_get_package_tree with root_class_id '{nearest}' lists what is there.";
        var elsewhere = sameLeaf.Count > 0 ? " " + SameLeafSentence(sameLeaf, classId) : "";

        return new ToolError($"{where}{elsewhere} {SearchTools}");
    }

    /// <summary>
    /// The classes <paramref name="classId"/> most likely meant: the loaded class with the same id in
    /// another case, if there is one, and the loaded classes with the same last name, sorted.
    /// </summary>
    private static (string? OtherCase, List<string> SameLeaf) NearMatches(ILibraryDataService libraries, string classId)
    {
        var leaf = ModelicaName.LeafOf(classId);
        string? otherCase = null;
        var sameLeaf = new List<string>();

        foreach (var model in libraries.GetAllModels())
        {
            if (string.Equals(model.Id, classId, StringComparison.OrdinalIgnoreCase))
                otherCase = model.Id;
            else if (string.Equals(ModelicaName.LeafOf(model.Id), leaf, StringComparison.Ordinal))
                sameLeaf.Add(model.Id);
        }

        sameLeaf.Sort(StringComparer.Ordinal);
        return (otherCase, sameLeaf);
    }

    private static string SameLeafSentence(List<string> sameLeaf, string classId)
    {
        var listed = string.Join(", ", sameLeaf.Take(MaxSuggestions).Select(id => $"'{id}'"));
        var more = sameLeaf.Count > MaxSuggestions ? $" and {sameLeaf.Count - MaxSuggestions} more" : "";
        return $"Classes named '{ModelicaName.LeafOf(classId)}' that do exist: {listed}{more}.";
    }

    /// <summary>
    /// For a tool given several class ids: the diagnosis of the first that does not resolve, after the
    /// list of all of them, so the agent is told what to do and not only which ids failed.
    /// </summary>
    public static ToolError ClassesNotFound(ILibraryDataService libraries, IReadOnlyList<string> missing)
    {
        var first = ClassNotFound(libraries, missing[0]);
        return missing.Count == 1
            ? first
            : new ToolError(
                $"{missing.Count} class ids do not resolve: {string.Join(", ", missing.Select(id => $"'{id}'"))}. " +
                $"The first: {first.Error}");
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

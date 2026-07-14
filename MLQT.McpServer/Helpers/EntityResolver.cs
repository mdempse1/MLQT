using MLQT.McpServer.Dtos;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Resolves a loaded library or an added repository from a value that may be EITHER its opaque id
/// (a GUID) OR its human-readable name. "id" is overloaded in this server — class ids are dotted
/// names, but library/repository ids are GUIDs — so accepting the name too (and listing the valid
/// choices on failure) keeps the tools forgiving.
/// </summary>
internal static class EntityResolver
{
    public static (LoadedLibrary? library, ToolError? error) ResolveLibrary(
        ILibraryDataService libraries, string idOrName)
    {
        var loaded = libraries.Libraries;
        if (loaded.Count == 0)
            return (null, new ToolError("No libraries are loaded. Load one with load_repository or load_library."));

        var byId = loaded.FirstOrDefault(l => l.Id == idOrName);
        if (byId is not null)
            return (byId, null);

        var byName = loaded.Where(l => string.Equals(l.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1)
            return (byName[0], null);
        if (byName.Count > 1)
            return (null, new ToolError(
                $"Multiple loaded libraries are named '{idOrName}'. Use the specific id from list_libraries: " +
                string.Join(", ", byName.Select(l => l.Id)) + "."));

        return (null, new ToolError(
            $"No loaded library matches '{idOrName}' by id or name. Loaded libraries: " +
            string.Join(", ", loaded.Select(l => $"'{l.Name}' (id {l.Id})")) +
            ". Pass the name or the id exactly as shown by list_libraries."));
    }

    public static (Repository? repository, ToolError? error) ResolveRepository(
        IRepositoryService repositories, string idOrName)
    {
        var all = repositories.Repositories;
        if (all.Count == 0)
            return (null, new ToolError("No repositories have been added. Use load_repository first."));

        var byId = repositories.GetRepository(idOrName);
        if (byId is not null)
            return (byId, null);

        var byName = all.Where(r => string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1)
            return (byName[0], null);
        if (byName.Count > 1)
            return (null, new ToolError(
                $"Multiple repositories are named '{idOrName}'. Use the specific id from list_repositories: " +
                string.Join(", ", byName.Select(r => r.Id)) + "."));

        return (null, new ToolError(
            $"No repository matches '{idOrName}' by id or name. Repositories: " +
            string.Join(", ", all.Select(r => $"'{r.Name}' (id {r.Id})")) +
            ". Pass the name or the id exactly as shown by list_repositories."));
    }
}

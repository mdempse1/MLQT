using ModelicaGraph;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The settings a loaded class or library is governed by: its repository's
/// <c>.mlqt/settings.json</c>, or null when it came from no repository (a library loaded with
/// <c>mlqt_load_library</c>).
///
/// <para>One answer for every tool. The checker's copy lived in <c>StyleTools</c>, and the format
/// tools, which never asked at all, rewrote a class the repository's name list excluded from
/// formatting (B313) — the sibling shape this repository keeps meeting.</para>
/// </summary>
public static class RepositorySettings
{
    public static StyleCheckingSettings? ForClass(
        ILibraryDataService libraries, IRepositoryService repositories, string classId)
        => libraries.GetOwningLibrary(classId) is { } library ? ForLibrary(repositories, library) : null;

    public static StyleCheckingSettings? ForLibrary(IRepositoryService repositories, LoadedLibrary library)
        => library.RepositoryId is { } id ? repositories.GetRepository(id)?.StyleSettings : null;
}

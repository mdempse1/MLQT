using ModelicaGraph;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The settings a loaded class or library is governed by: its repository's
/// <c>.mlqt/settings.json</c>, or null when it came from no repository (a library loaded with
/// <c>mlqt_load</c>).
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

    /// <summary>
    /// Whether a class written under <paramref name="settings"/> is formatted as it is written: only
    /// when they apply the formatting rules. A library from no repository has none, and
    /// <c>ApplyFormattingRules</c> is off unless a repository turns it on, so mostly a write is
    /// <em>not</em> formatted - which nothing said, and an agent that read "edits format themselves"
    /// took ordering findings on every class it wrote for something MLQT would fix.
    /// </summary>
    /// <remarks>The one answer: <see cref="EditFormatting"/> formats by it, and what mlqt_load,
    /// mlqt_session and mlqt_check tell an agent is read from it.</remarks>
    internal static bool FormatsWrites(StyleCheckingSettings? settings) => settings is { ApplyFormattingRules: true };

    /// <summary>Why a class's writes are not formatted, as a phrase, or null when they are.</summary>
    internal static string? WhyWritesAreNotFormatted(StyleCheckingSettings? settings) =>
        settings is null ? "it is in no repository, so it has no formatting rules"
        : !FormatsWrites(settings) ? "its repository's settings have ApplyFormattingRules off"
        : null;
}

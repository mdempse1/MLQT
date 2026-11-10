using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.Analysis;

namespace MLQT.Services.Checking;

/// <summary>
/// Which repository a loaded class belongs to, and therefore which settings apply to it.
/// </summary>
/// <remarks>
/// <para>The first piece of <c>MainLayout</c>'s analysis pipeline to move out (phase 7a-4). These
/// three answers decide what every re-analysis pass does — which rules a class is checked against,
/// which repository's findings get cleared, whether dependency analysis has to run first — and
/// while they were private methods on a layout component reading two injected services, nothing
/// could ask any of them without rendering the application shell.</para>
///
/// <para>They take the loaded libraries and repositories rather than the services that hold them,
/// which is what makes them answerable in a test and is the shape the rest of the pipeline should
/// follow as it moves.</para>
/// </remarks>
public static class ModelScope
{
    /// <summary>
    /// Every loaded class mapped to the id of the repository it came from.
    /// </summary>
    /// <remarks>
    /// Classes from a library with no repository — a bare directory or a single file the user
    /// opened — are absent rather than mapped to an empty id, so a caller iterating this never
    /// attributes them to a repository that does not exist.
    /// </remarks>
    public static Dictionary<string, string> ModelToRepository(IEnumerable<LoadedLibrary> libraries)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            if (library.RepositoryId is not { Length: > 0 } repositoryId)
                continue;

            foreach (var modelId in library.ModelIds)
                result[modelId] = repositoryId;
        }
        return result;
    }

    /// <summary>
    /// Every loaded class mapped to the style settings it is checked against.
    /// </summary>
    /// <remarks>
    /// <para>Unlike <see cref="ModelToRepository"/> this maps every class of the user's own code,
    /// because every such class is checked against something: a library with no repository, or one
    /// whose repository has never had settings saved, falls back to the defaults. A class of theirs
    /// missing from this map would be checked against nothing at all, which reads as a clean class
    /// rather than an unchecked one.</para>
    ///
    /// <para><b>A class loaded only for reference is left out</b> — a reference-only repository's, or
    /// a library from <b>Settings → Reference Libraries</b> (<see cref="ReferenceOnlyScope"/>). It is
    /// neither checked nor measured, as on every other path. The combined startup pass, which reads
    /// this map, checked a reference-only repository against the rules in its own
    /// <c>.mlqt/settings.json</c> and put a vendor's findings in the Code Review list (2026-11-10),
    /// while the background check, the incremental check and the graph analyses all skipped it. A
    /// class the user also has in a library of their own keeps that library's settings.</para>
    /// </remarks>
    public static Dictionary<string, StyleCheckingSettings> ModelToStyleSettings(
        IEnumerable<LoadedLibrary> libraries,
        Func<string, Repository?> repositoryById)
    {
        var result = new Dictionary<string, StyleCheckingSettings>(StringComparer.Ordinal);
        var defaultSettings = new StyleCheckingSettings();

        foreach (var library in libraries)
        {
            var repository = string.IsNullOrEmpty(library.RepositoryId) ? null : repositoryById(library.RepositoryId);
            if (library.IsReferenceOnly || repository is { IsReferenceOnly: true })
                continue;

            var settings = repository?.StyleSettings ?? defaultSettings;

            foreach (var modelId in library.ModelIds)
                result[modelId] = settings;
        }
        return result;
    }

    /// <summary>
    /// Whether any repository has a rule enabled that needs the dependency edges.
    /// </summary>
    /// <remarks>
    /// Asked before a style check so the graph analyses are not silently skipped. Getting this
    /// wrong does not fail: it makes the GUI report fewer findings than the CLI for the same
    /// library, which is how the difference was originally noticed rather than reported. A
    /// reference-only repository's settings are not asked: nothing of its is checked, so its rules
    /// cannot need the edges.
    /// </remarks>
    public static bool RequiresDependencyAnalysis(IEnumerable<Repository> repositories) =>
        repositories
            .Where(r => !r.IsReferenceOnly)
            .Select(r => r.StyleSettings)
            .Where(s => s is not null)
            .Any(s => GraphAnalysisRunner.RequiresDependencyAnalysis(s!));
}

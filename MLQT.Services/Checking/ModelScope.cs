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
    /// Unlike <see cref="ModelToRepository"/> this maps every class, because every class is checked
    /// against something: a library with no repository, or one whose repository has never had
    /// settings saved, falls back to the defaults. A class missing from this map would be checked
    /// against nothing at all, which reads as a clean class rather than an unchecked one.
    /// </remarks>
    public static Dictionary<string, StyleCheckingSettings> ModelToStyleSettings(
        IEnumerable<LoadedLibrary> libraries,
        Func<string, Repository?> repositoryById)
    {
        var result = new Dictionary<string, StyleCheckingSettings>(StringComparer.Ordinal);
        var defaultSettings = new StyleCheckingSettings();

        foreach (var library in libraries)
        {
            var settings = defaultSettings;
            if (!string.IsNullOrEmpty(library.RepositoryId)
                && repositoryById(library.RepositoryId)?.StyleSettings is { } repositorySettings)
            {
                settings = repositorySettings;
            }

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
    /// library, which is how the difference was originally noticed rather than reported.
    /// </remarks>
    public static bool RequiresDependencyAnalysis(IEnumerable<Repository> repositories) =>
        repositories
            .Select(r => r.StyleSettings)
            .Where(s => s is not null)
            .Any(s => GraphAnalysisRunner.RequiresDependencyAnalysis(s!));
}

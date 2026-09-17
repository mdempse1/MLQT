using MLQT.Services.DataTypes;

namespace MLQT.Services.Helpers;

/// <summary>
/// Which history file a library's metrics snapshots belong in, and which repository owns a scope.
/// </summary>
/// <remarks>
/// <para>Backlog B119, the remainder of B20 for <c>MetricsDashboard</c>. Writing and reading the
/// history was already <see cref="MetricsHistoryStore"/>'s job; deciding <i>where</i> was inline in
/// the page, and it is the half with consequences. A snapshot in the wrong file either dirties a
/// checkout the user marked hands-off, or writes a trend about a vendor's library into the user's own
/// history where it reads as their code getting worse.</para>
///
/// <para>Takes the libraries and a lookup rather than the services, so the decisions can be tested
/// without a graph, a repository or a filesystem behind them.</para>
/// </remarks>
public static class MetricsStorage
{
    /// <summary>Where one library's snapshots go.</summary>
    /// <param name="Path">The history file.</param>
    /// <param name="Shared">
    /// True when it is a repository's committed file, which the whole team sees; false for the
    /// per-user one. The dashboard tells the user which it wrote, because the two have very different
    /// consequences — one appears in their next commit.
    /// </param>
    public readonly record struct Destination(string Path, bool Shared);

    /// <summary>The history file for a library backed by <paramref name="repositoryLocalPath"/>.</summary>
    /// <remarks>
    /// A library with no repository behind it — loaded from a file or a directory — has nowhere
    /// shared to put a snapshot, so it goes in the per-user history.
    /// </remarks>
    public static Destination DestinationFor(string? repositoryLocalPath) =>
        string.IsNullOrEmpty(repositoryLocalPath)
            ? new Destination(MetricsHistoryStore.DefaultPath, Shared: false)
            : new Destination(MetricsHistoryStore.RepoPath(repositoryLocalPath), Shared: true);

    /// <summary>
    /// The libraries whose snapshots go to each destination, skipping the ones nothing is measured for.
    /// </summary>
    /// <param name="libraries">Every loaded library.</param>
    /// <param name="repositoryLocalPathOf">A library's repository's local path, or null when it has none.</param>
    /// <param name="isReferenceOnly">
    /// Whether a library is loaded only for reference. Nothing is measured for one and nothing is
    /// written to its repository: a snapshot there would dirty a checkout the user marked hands-off
    /// with numbers about code that is not theirs to improve. Asked of the <i>library</i>, because one
    /// loaded from Settings &gt; Reference Libraries has no repository at all — those were landing in
    /// the per-user history, which on a machine with a tool's library folder configured meant a trend
    /// describing a vendor's library.
    /// </param>
    public static List<(Destination Destination, List<LoadedLibrary> Libraries)> GroupByDestination(
        IEnumerable<LoadedLibrary> libraries,
        Func<LoadedLibrary, string?> repositoryLocalPathOf,
        Func<LoadedLibrary, bool> isReferenceOnly)
    {
        var groups = new List<(Destination Destination, List<LoadedLibrary> Libraries)>();
        var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in libraries)
        {
            if (isReferenceOnly(library))
                continue;

            var destination = DestinationFor(repositoryLocalPathOf(library));

            if (!byPath.TryGetValue(destination.Path, out var index))
            {
                byPath[destination.Path] = index = groups.Count;
                groups.Add((destination, []));
            }

            groups[index].Libraries.Add(library);
        }

        return groups;
    }

    /// <summary>
    /// The id of the repository a dashboard scope belongs to, or null when no single one does.
    /// </summary>
    /// <param name="scope">A class id, or empty for "all libraries".</param>
    /// <param name="libraries">Every loaded library.</param>
    /// <remarks>
    /// The empty scope resolves only when exactly one repository is loaded. With several it spans
    /// them all, and there is no one repository whose revision a snapshot could honestly claim to
    /// describe — so it is written per library instead.
    /// </remarks>
    public static string? OwningRepositoryId(string? scope, IEnumerable<LoadedLibrary> libraries)
    {
        var loaded = libraries as IReadOnlyList<LoadedLibrary> ?? libraries.ToList();

        if (!string.IsNullOrEmpty(scope))
        {
            var owner = loaded.FirstOrDefault(l => l.ModelIds.Contains(scope))?.RepositoryId;
            return string.IsNullOrEmpty(owner) ? null : owner;
        }

        var repositoryIds = loaded
            .Select(l => l.RepositoryId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return repositoryIds.Count == 1 ? repositoryIds[0] : null;
    }

    /// <summary>
    /// The history files to read for the trend: every loaded repository's, plus the per-user one.
    /// </summary>
    /// <remarks>
    /// A reference-only repository may carry its owner's own history file. Its points describe classes
    /// this report does not measure, so merging them would move the trend — and at the "all libraries"
    /// scope they would be aggregated into our own points by timestamp.
    /// </remarks>
    public static List<string> HistoryFilesToRead(IEnumerable<Repository> repositories)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repository in repositories)
        {
            if (string.IsNullOrEmpty(repository.LocalPath) || repository.IsReferenceOnly)
                continue;

            var path = MetricsHistoryStore.RepoPath(repository.LocalPath);
            if (seen.Add(path))
                paths.Add(path);
        }

        if (seen.Add(MetricsHistoryStore.DefaultPath))
            paths.Add(MetricsHistoryStore.DefaultPath);

        return paths;
    }
}

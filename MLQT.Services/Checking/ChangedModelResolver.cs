using RevisionControl;
using RevisionControl.Interfaces;

namespace MLQT.Services.Checking;

public sealed record ChangedModelResult(
    bool Ok,
    IReadOnlySet<string> ChangedModelIds,
    int ChangedFileCount,
    string? Error);

/// <summary>
/// Determines which models a change touched, for the baseline ratchet's touched-debt escalation.
/// Selects the VCS the same way <c>RepositoryService</c> does (no factory exists), diffs the working
/// state against a ref, and maps the changed <c>.mo</c> files back to model ids via the model→file map.
/// </summary>
public static class ChangedModelResolver
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static ChangedModelResult Resolve(
        string libraryPath, string sinceRevision, IReadOnlyDictionary<string, string> modelToFile)
    {
        var (vcs, root) = VcsLocator.Find(libraryPath);
        if (vcs is null || root is null)
            return Failed($"'{libraryPath}' is not inside a Git or SVN working copy");

        // Fail loudly on an unresolvable ref rather than silently treating nothing as changed.
        if (vcs.ResolveRevision(root, sinceRevision) is null)
            return Failed($"could not resolve revision '{sinceRevision}' in {root}");

        IReadOnlyList<string>? changedPaths;
        try
        {
            changedPaths = vcs.GetChangedFilePathsSince(root, sinceRevision);
        }
        catch (Exception ex)
        {
            return Failed($"could not diff against '{sinceRevision}': {ex.Message}");
        }

        // A diff that could not be taken is not a diff with nothing in it. Treating the two alike
        // meant a failure in CI escalated no touched debt, credited no fixed entry, and passed the
        // build looking exactly like a run with nothing to say.
        if (changedPaths is null)
        {
            return Failed(
                $"could not diff against '{sinceRevision}' in {root}. The ref must exist locally and " +
                "share history with the working copy - a shallow CI checkout has neither " +
                "(actions/checkout needs fetch-depth: 0). See the log for what the VCS reported");
        }

        var changedMoFiles = changedPaths
            .Where(p => p.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // file(normalized) -> model ids
        var fileToModels = new Dictionary<string, List<string>>(PathComparer);
        foreach (var (modelId, file) in modelToFile)
        {
            var key = NormalizePath(file);
            if (!fileToModels.TryGetValue(key, out var list))
                fileToModels[key] = list = new List<string>();
            list.Add(modelId);
        }

        var changedModels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in changedMoFiles)
        {
            if (fileToModels.TryGetValue(NormalizePath(path), out var models))
                changedModels.UnionWith(models);
        }

        return new ChangedModelResult(true, changedModels, changedMoFiles.Count, null);
    }

    private static ChangedModelResult Failed(string error) => new(false, Empty, 0, error);

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
}

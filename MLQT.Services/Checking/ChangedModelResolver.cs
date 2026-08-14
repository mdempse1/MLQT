using RevisionControl;
using RevisionControl.Interfaces;

namespace MLQT.Services.Checking;

public sealed record ChangedModelResult(bool Ok, IReadOnlySet<string> ChangedModelIds, string? Error);

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
        // Pick the VCS: first system whose working-copy root contains the library path.
        var systems = new IRevisionControlSystem[]
        {
            new GitRevisionControlSystem(),
            new SvnRevisionControlSystem()
        };

        IRevisionControlSystem? vcs = null;
        string? root = null;
        foreach (var system in systems)
        {
            var candidate = system.FindRepositoryRoot(libraryPath);
            if (candidate is not null && system.IsValidRepository(candidate))
            {
                vcs = system;
                root = candidate;
                break;
            }
        }

        if (vcs is null || root is null)
            return new ChangedModelResult(false, Empty, $"'{libraryPath}' is not inside a Git or SVN working copy");

        IReadOnlyList<string> changedPaths;
        try
        {
            changedPaths = vcs.GetChangedFilePathsSince(root, sinceRevision);
        }
        catch (Exception ex)
        {
            return new ChangedModelResult(false, Empty, $"could not diff against '{sinceRevision}': {ex.Message}");
        }

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
        foreach (var path in changedPaths)
        {
            if (!path.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                continue;
            if (fileToModels.TryGetValue(NormalizePath(path), out var models))
                changedModels.UnionWith(models);
        }

        return new ChangedModelResult(true, changedModels, null);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();
}

using ModelicaGraph;

namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a version control repository containing Modelica libraries.
/// </summary>
public class Repository
{
    /// <summary>
    /// Unique identifier for this repository instance.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User-friendly display name for the repository.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Remote URL or path (for Git/SVN repos) or original local path.
    /// For cloned repos, this is the origin URL.
    /// </summary>
    public string RemotePath { get; set; } = "";

    /// <summary>
    /// Local checkout path where the Modelica libraries are stored.
    /// May be a subdirectory of the VCS working copy root.
    /// </summary>
    public string LocalPath { get; set; } = "";

    /// <summary>
    /// Root of the VCS working copy. For repos where LocalPath is a subdirectory
    /// of a larger VCS repository, this is the actual VCS root. For Local VCS type
    /// or when LocalPath is itself the VCS root, this equals LocalPath.
    /// All VCS operations (branch switch, commit, merge, etc.) operate at this path.
    /// </summary>
    public string VcsRootPath { get; set; } = "";

    /// <summary>
    /// The Modelica library path relative to the VCS root, for display purposes.
    /// Null when LocalPath equals VcsRootPath (the common case).
    /// </summary>
    public string? RelativeLibraryPath =>
        string.Equals(VcsRootPath, LocalPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : Path.GetRelativePath(VcsRootPath, LocalPath);

    /// <summary>
    /// Type of version control system.
    /// </summary>
    public RepositoryVcsType VcsType { get; set; } = RepositoryVcsType.Local;

    /// <summary>
    /// Current revision identifier (commit hash for Git, revision number for SVN).
    /// </summary>
    public string? CurrentRevision { get; set; }

    /// <summary>
    /// Current branch name (e.g., "main", "trunk", "branches/release-1.0").
    /// Null if not on a branch (e.g., detached HEAD in Git) or for Local VCS type.
    /// </summary>
    public string? CurrentBranch { get; set; }

    /// <summary>
    /// Description of the current revision (commit message, etc.).
    /// </summary>
    public string? RevisionDescription { get; set; }

    /// <summary>
    /// IDs of libraries that belong to this repository.
    /// Maps to LoadedLibrary.Id values.
    /// </summary>
    public List<string> LibraryIds { get; set; } = new();

    /// <summary>
    /// Discovered library paths within the repository.
    /// Key is relative path from repo root, value is library name.
    /// </summary>
    public Dictionary<string, string> DiscoveredLibraries { get; set; } = new();

    /// <summary>
    /// Whether the repository is currently loaded and active.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Last error message if loading failed.
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Timestamp of last successful load/refresh.
    /// </summary>
    public DateTime? LastLoadedAt { get; set; }

    /// <summary>
    /// Style checking settings for this repository
    /// </summary>
    public StyleCheckingSettings? StyleSettings { get; set; }

    /// <summary>
    /// True when the repository's .mlqt/settings.json could not be written
    /// (e.g. due to permissions). The repository still functions using global settings.
    /// </summary>
    public bool IsSettingsReadOnly { get; set; }
}

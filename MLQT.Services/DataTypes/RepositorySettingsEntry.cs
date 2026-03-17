namespace MLQT.Services.DataTypes;

/// <summary>
/// Serializable settings model for persisting a single repository configuration.
/// Does NOT include runtime data like loaded libraries.
/// </summary>
public class RepositorySettingsEntry
{
    /// <summary>
    /// Unique identifier matching Repository.Id.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// User-friendly display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Remote URL or path.
    /// </summary>
    public string RemotePath { get; set; } = "";

    /// <summary>
    /// Local checkout path where the Modelica libraries are stored.
    /// May be a subdirectory of the VCS working copy root.
    /// </summary>
    public string LocalPath { get; set; } = "";

    /// <summary>
    /// Root of the VCS working copy. Equals LocalPath when LocalPath is itself the VCS root.
    /// </summary>
    public string VcsRootPath { get; set; } = "";

    /// <summary>
    /// Type of VCS (stored as string for serialization).
    /// Values: "Local", "Git", "SVN"
    /// </summary>
    public string VcsType { get; set; } = "Local";

    /// <summary>
    /// Preferred branch/tag to checkout (optional).
    /// </summary>
    public string? PreferredRevision { get; set; }

    /// <summary>
    /// Whether to auto-load this repository on startup.
    /// </summary>
    public bool AutoLoad { get; set; } = true;

    /// <summary>
    /// Relative paths to libraries within the repo to load.
    /// If empty, all discovered libraries are loaded.
    /// </summary>
    public List<string> LibraryPaths { get; set; } = new();
}

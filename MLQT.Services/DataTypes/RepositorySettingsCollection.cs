namespace MLQT.Services.DataTypes;

/// <summary>
/// Container for all repository settings, organized by project profiles.
/// </summary>
public class RepositorySettingsCollection
{
    /// <summary>
    /// Legacy flat list of repositories. Kept for backward-compatible deserialization.
    /// After migration to project profiles, this list is empty and Projects is the source of truth.
    /// </summary>
    public List<RepositorySettingsEntry> Repositories { get; set; } = new();

    /// <summary>
    /// Default workspace directory for new checkouts.
    /// </summary>
    public string? DefaultWorkspaceDirectory { get; set; }

    /// <summary>
    /// Named project profiles, each containing a set of repository configurations.
    /// </summary>
    public List<ProjectProfile> Projects { get; set; } = new();

    /// <summary>
    /// ID of the currently active project profile.
    /// </summary>
    public string? ActiveProjectId { get; set; }
}

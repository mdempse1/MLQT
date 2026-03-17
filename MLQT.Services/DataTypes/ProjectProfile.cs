namespace MLQT.Services.DataTypes;

/// <summary>
/// A named project profile containing a set of repository configurations.
/// Users can define multiple projects to switch between different sets of libraries.
/// </summary>
public class ProjectProfile
{
    /// <summary>
    /// Unique identifier for this project profile.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User-friendly display name for the project.
    /// </summary>
    public string Name { get; set; } = "Default";

    /// <summary>
    /// Repository settings entries belonging to this project.
    /// </summary>
    public List<RepositorySettingsEntry> Repositories { get; set; } = new();
}

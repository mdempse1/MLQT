using MudBlazor;
using RevisionControl;

namespace MLQT.Shared.Helpers;

/// <summary>
/// Shared helper methods for mapping VCS file status to MudBlazor display elements.
/// Used by LibraryBrowser, ChangeReview, and CodeReview components.
/// </summary>
public static class VcsStatusHelper
{
    /// <summary>
    /// Gets the MudBlazor icon string for a given VCS file status.
    /// </summary>
    /// <param name="status">The VCS file status to get an icon for.</param>
    /// <returns>A MudBlazor icon string.</returns>
    public static string GetStatusIcon(VcsFileStatus status)
    {
        return status switch
        {
            VcsFileStatus.Added => Icons.Material.Filled.Add,
            VcsFileStatus.Modified => Icons.Material.Filled.Edit,
            VcsFileStatus.Deleted => Icons.Material.Filled.Delete,
            VcsFileStatus.Renamed => Icons.Material.Filled.DriveFileRenameOutline,
            VcsFileStatus.Untracked => Icons.Material.Filled.FiberNew,
            VcsFileStatus.Conflicted => Icons.Material.Filled.Warning,
            _ => Icons.Material.Filled.QuestionMark
        };
    }

    /// <summary>
    /// Gets the MudBlazor Color for a given VCS file status.
    /// </summary>
    /// <param name="status">The VCS file status to get a color for.</param>
    /// <returns>A MudBlazor Color value.</returns>
    public static Color GetStatusColor(VcsFileStatus status)
    {
        return status switch
        {
            VcsFileStatus.Added => Color.Success,
            VcsFileStatus.Modified => Color.Warning,
            VcsFileStatus.Deleted => Color.Warning,
            VcsFileStatus.Renamed => Color.Warning,
            VcsFileStatus.Untracked => Color.Success,
            VcsFileStatus.Conflicted => Color.Error,
            _ => Color.Default
        };
    }

    /// <summary>
    /// Gets the single-character abbreviation for a given VCS file status.
    /// </summary>
    /// <param name="status">The VCS file status to get text for.</param>
    /// <returns>A single-character status abbreviation.</returns>
    public static string GetStatusText(VcsFileStatus status)
    {
        return status switch
        {
            VcsFileStatus.Added => "A",
            VcsFileStatus.Modified => "M",
            VcsFileStatus.Deleted => "D",
            VcsFileStatus.Renamed => "R",
            VcsFileStatus.Untracked => "N",
            VcsFileStatus.Conflicted => "!",
            _ => "?"
        };
    }
}

namespace MLQT.Shared.Components;

/// <summary>
/// View modes for the DiffViewer component.
/// </summary>
public enum DiffViewMode
{
    /// <summary>
    /// Side by side view showing only changed sections with context lines.
    /// </summary>
    SideBySide,

    /// <summary>
    /// Unified diff view showing changes inline with +/- prefixes.
    /// </summary>
    Unified,

    /// <summary>
    /// Side by side view showing complete files.
    /// </summary>
    SideBySideFull
}

namespace MLQT.Services.DataTypes;

/// <summary>What <see cref="Interfaces.IRepositoryService.ReloadRepositorySettings"/> found.</summary>
public enum SettingsReloadOutcome
{
    /// <summary>No repository has that id.</summary>
    NotFound,

    /// <summary>The file is as it was last read or written; the settings in use stand.</summary>
    Unchanged,

    /// <summary>The file changed, or went, and the repository now has its settings (or the defaults).</summary>
    Changed,

    /// <summary>The file could not be read or parsed; the settings in use were kept.</summary>
    Unreadable,
}

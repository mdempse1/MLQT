namespace MLQT.Services.DataTypes;

/// <summary>
/// Types of warnings for external resource references.
/// </summary>
public enum ResourceWarningType
{
    /// <summary>
    /// The referenced resource file does not exist on disk.
    /// </summary>
    MissingFile,

    /// <summary>
    /// The resource reference uses an absolute path, which is not portable.
    /// </summary>
    AbsolutePath
}

namespace MLQT.Services.DataTypes;

/// <summary>
/// Type of source for a Modelica library.
/// </summary>
public enum LibrarySourceType
{
    File,
    Directory,
    Zip,
    Git,
    SVN
}

namespace MLQT.Services.DataTypes;

/// <summary>
/// Type of version control system for a repository.
/// </summary>
public enum RepositoryVcsType
{
    /// <summary>Local directory (no VCS)</summary>
    Local,
    /// <summary>Git repository</summary>
    Git,
    /// <summary>Subversion repository</summary>
    SVN
}

namespace RevisionControl;

/// <summary>
/// Represents a branch in a repository.
/// </summary>
public class VcsBranchInfo
{
    /// <summary>
    /// The name of the branch.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Whether this is the currently checked out branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Whether this is a remote tracking branch.
    /// </summary>
    public bool IsRemote { get; set; }

    /// <summary>
    /// Whether this is a tag rather than a branch.
    /// </summary>
    /// <remarks>
    /// <para>Tags are listed beside branches because a user switching to "the version we shipped"
    /// is doing the same thing either way, and other clients offer both in one place. What happens
    /// afterwards differs, and the UI has to say so: <b>checking out a tag leaves Git with a detached
    /// HEAD</b>, where commits belong to no branch. SVN has no such state - a tag there is a
    /// directory like any other - so nothing sets this for SVN, whose tags already arrive as
    /// ordinary <c>tags/*</c> entries (B193).</para>
    /// </remarks>
    public bool IsTag { get; set; }

    /// <summary>
    /// The last commit on this branch (may be null if not available).
    /// </summary>
    public string? LastCommit { get; set; }
}

namespace RevisionControl;

/// <summary>
/// Represents a single log entry (commit/revision) from a version control system.
/// </summary>
public class VcsLogEntry
{
    private DateTimeOffset? _committerDate;

    /// <summary>
    /// The revision identifier (commit hash for Git, revision number for SVN).
    /// </summary>
    public string Revision { get; set; } = "";

    /// <summary>
    /// Short version of the revision (e.g., first 7 characters of Git hash).
    /// </summary>
    public string ShortRevision { get; set; } = "";

    /// <summary>
    /// The author/committer name.
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// The author's email address (may be empty for SVN).
    /// </summary>
    public string AuthorEmail { get; set; } = "";

    /// <summary>
    /// The date and time of the commit. For Git, the author date: when the change was first written,
    /// which a rebase, cherry-pick or amend keeps.
    /// </summary>
    public DateTimeOffset Date { get; set; }

    /// <summary>
    /// When the commit was made on its branch. For Git, the committer date, which a rebase,
    /// cherry-pick or amend sets anew, so it can be weeks after <see cref="Date"/>; it is the date
    /// the history is ordered by. SVN has one date, so this is <see cref="Date"/> unless set.
    /// </summary>
    public DateTimeOffset CommitterDate
    {
        get => _committerDate ?? Date;
        set => _committerDate = value;
    }

    /// <summary>
    /// The full commit message.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// The first line of the commit message (summary).
    /// </summary>
    public string MessageShort { get; set; } = "";

    /// <summary>
    /// The branch name associated with this commit (if known).
    /// For Git, this is the branch the commit was made on.
    /// For SVN, this is extracted from the URL (trunk, branches/*, tags/*).
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    /// The parent commit SHAs (Git only). Empty for SVN and root commits.
    /// </summary>
    public List<string> ParentRevisions { get; set; } = [];
}

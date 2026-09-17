namespace MLQT.Shared.Dialogs;

/// <summary>
/// The commit message a merge dialog offers once the merge has gone through.
/// </summary>
/// <remarks>
/// <para>A default, not a decision — the user gets it in an editable box. It still matters: it is
/// what most merge commits in a repository will end up saying, and a message that omits the revision
/// range makes an SVN history much harder to read back.</para>
///
/// <para>Extracted from the Git and SVN merge dialogs in 7a-3, which had a copy each. They differed
/// in exactly the way that duplication produces: the SVN one had learned about revision ranges and
/// the Git one had not, and the half they shared — the fallback chain for a branch or target name
/// nobody supplied — was written out twice.</para>
/// </remarks>
public static class MergeCommitMessage
{
    /// <summary>
    /// Builds the message.
    /// </summary>
    /// <param name="sourceBranch">The branch the VCS reports it merged from; preferred when present.</param>
    /// <param name="selectedBranch">What the user picked, used when the VCS reported nothing.</param>
    /// <param name="currentBranch">The branch being merged into.</param>
    /// <param name="startRevision">SVN only, and only when the range is known.</param>
    /// <param name="endRevision">SVN only.</param>
    /// <remarks>
    /// Revisions are passed rather than looked up, so "is this SVN?" stays in the dialog that already
    /// knows. Git callers pass neither and get the plain form.
    /// </remarks>
    public static string Build(
        string? sourceBranch,
        string? selectedBranch,
        string? currentBranch,
        long? startRevision = null,
        long? endRevision = null)
    {
        // The VCS's own answer first: after a merge it knows what it merged, and the user's
        // selection may have been a shorthand the VCS resolved to something fuller.
        var branch = sourceBranch ?? selectedBranch ?? "unknown";

        // "working copy" rather than "unknown" - an SVN working copy genuinely has no branch name,
        // so this is the ordinary case there and not a failure to find one.
        var target = currentBranch ?? "working copy";

        if (startRevision.HasValue && endRevision.HasValue)
            return $"Merge r{startRevision}:{endRevision} from '{branch}' into {target}";

        if (endRevision.HasValue)
            return $"Merge '{branch}' (up to r{endRevision}) into {target}";

        return $"Merge '{branch}' into {target}";
    }
}

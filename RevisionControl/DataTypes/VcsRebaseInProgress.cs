namespace RevisionControl;

/// <summary>
/// A Git rebase that stopped part-way and is waiting to be continued or aborted (B382).
/// </summary>
/// <remarks>
/// <para>A rebase stops at the first commit that conflicts, and stays stopped until someone runs
/// <c>git rebase --continue</c> or <c>--abort</c> - however long that is, and whatever closed in the
/// meantime. HEAD is detached until then, so Commit, Merge and Push are all refused (B327), and the
/// only way on is to finish the rebase or undo it.</para>
///
/// <para>So the dialog that started it cannot be the only thing that knows about it: this is what
/// anything opened later asks.</para>
/// </remarks>
/// <param name="Branch">The branch being rebased, or null where git did not record one.</param>
/// <param name="ConflictedFiles">Full paths of the files still in conflict - empty once they have all
/// been resolved, or where the rebase stopped for another reason, which is when it may be
/// continued.</param>
public sealed record VcsRebaseInProgress(string? Branch, IReadOnlyList<string> ConflictedFiles);

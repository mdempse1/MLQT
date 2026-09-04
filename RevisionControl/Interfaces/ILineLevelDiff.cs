namespace RevisionControl.Interfaces;

/// <summary>
/// A revision-control system that can say which <b>lines</b> a change touched, not just which files.
///
/// <para>Separate from <see cref="IRevisionControlSystem"/> rather than a member of it, because only
/// one system has it and only one caller needs it. A pull-request review comment has to sit on a line
/// inside the diff, which is a Git-forge idea; SVN has nothing to attach one to, so
/// <c>SvnRevisionControlSystem</c> does not implement this and the resolver's "review needs Git"
/// refusal falls out of the type rather than out of a check against a concrete class. That also makes
/// the refusal, and the failing-diff path behind it, something a test can reach.</para>
/// </summary>
public interface ILineLevelDiff
{
    /// <summary>
    /// The lines each file gained or had rewritten between <paramref name="sinceRevision"/> and the
    /// current working state, keyed by absolute path.
    ///
    /// <para><b>Null means the diff could not be taken</b> — the ref does not exist locally, or shares
    /// no history with HEAD. It must not read as "nothing changed": a review built from an empty diff
    /// comments on nothing and looks exactly like a clean one.</para>
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlySet<int>>? GetChangedLinesSince(
        string repositoryPath, string sinceRevision);
}

using RevisionControl.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// The lines a change touched, and the repository root they are relative to. <see cref="Ok"/> false
/// carries the reason: a caller asking for a review has to be told why it cannot have one, because
/// an empty diff and an unworkable one produce the same (empty) review otherwise.
/// </summary>
public sealed record ChangedLineResult(
    bool Ok,
    string? RepositoryRoot,
    IReadOnlyDictionary<string, IReadOnlySet<int>> LinesByFile,
    string? Error)
{
    /// <summary>Whether a comment placed on this line of this file would land inside the diff.</summary>
    public bool Covers(string absoluteFilePath, int line) =>
        LinesByFile.TryGetValue(absoluteFilePath, out var lines) && lines.Contains(line);

    /// <summary>The path as a forge names it: relative to the repository root, forward slashes.</summary>
    public string? RepositoryRelativePath(string absoluteFilePath)
    {
        if (RepositoryRoot is null)
            return null;

        var relative = Path.GetRelativePath(RepositoryRoot, absoluteFilePath);
        return Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)
            ? null   // outside the repository: no path a forge could resolve
            : relative.Replace('\\', '/');
    }
}

/// <summary>
/// Works out which lines a change added or rewrote, for review comments that have to land inside a
/// pull request's diff.
///
/// <para>Git only, and deliberately so rather than by omission: pull requests are a git-forge idea,
/// and SVN has no equivalent to place a comment on. <see cref="ChangedModelResolver"/> answers the
/// coarser question — which <em>models</em> a change touched — for both systems, and that is what
/// the baseline ratchet uses.</para>
/// </summary>
public static class ChangedLineResolver
{
    public static ChangedLineResult Resolve(string libraryPath, string sinceRevision) =>
        Resolve(libraryPath, sinceRevision, systems: []);

    /// <summary>
    /// The same, over systems the caller already holds — and the seam a test reaches through. The
    /// two branches worth exercising are the ones a real repository will not perform to order: a
    /// working copy that is not Git, and a diff that cannot be taken.
    /// </summary>
    /// <param name="systems">Tried in order. Empty means the ordinary set, which
    /// <see cref="VcsLocator"/> owns — listed here as well, this file would have kept its own idea of
    /// what a working copy can be while the neighbouring <see cref="ChangedModelResolver"/> asked.</param>
    public static ChangedLineResult Resolve(
        string libraryPath, string sinceRevision, params IRevisionControlSystem[] systems)
    {
        var (vcs, root) = systems.Length > 0
            ? VcsLocator.Find(libraryPath, systems)
            : VcsLocator.Find(libraryPath);

        // ILineLevelDiff rather than the concrete Git class: what this needs is the line-level diff,
        // and saying so in the type is what makes "SVN cannot do this" a fact about the system rather
        // than a check somebody remembered to write.
        if (vcs is not ILineLevelDiff diff || root is null)
        {
            return Failed(vcs is null
                ? $"'{libraryPath}' is not inside a Git working copy"
                : "review output needs Git: a pull request is a Git-forge feature, and SVN has " +
                  "nothing to attach a line comment to");
        }

        var lines = diff.GetChangedLinesSince(root, sinceRevision);
        if (lines is null)
        {
            // Nearly always the same cause, and not an obvious one: the ref has to exist locally and
            // share history with HEAD, and a CI checkout has neither by default because it is shallow
            // and fetches one branch. Worth naming, or this reads as a bug in the diff.
            return Failed(
                $"could not diff against '{sinceRevision}' in {root}. The ref must exist locally and " +
                "share history with HEAD - a shallow CI checkout has neither (actions/checkout needs " +
                "fetch-depth: 0)");
        }

        return new ChangedLineResult(true, root, lines, null);
    }

    private static ChangedLineResult Failed(string error) =>
        new(false, null, EmptyLines, error);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> EmptyLines =
        new Dictionary<string, IReadOnlySet<int>>();
}

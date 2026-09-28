using ModelicaGraph;
using ModelicaParser.Helpers;
using RevisionControl;

namespace MLQT.Services.Helpers;

/// <summary>
/// Whether a file can be reverted to take back the formatting MLQT applied to it, and nothing else —
/// the question <b>Exclude from auto-formatting</b> asks before it undoes the formatting a class has
/// already had (B302).
///
/// <para><b>A revert is a VCS operation, not a formatting one.</b> It restores the committed file
/// whatever made it differ, so asking only "has this file changed?" — which is what the button did —
/// threw away every uncommitted edit in it: hand edits to the other classes in a <c>package.mo</c>,
/// and, for a file the repository has never seen, the whole file. Git's revert deletes a new file and
/// SVN's deletes an unversioned one, so excluding a class the user had written that morning deleted
/// it with nothing to recover it from.</para>
///
/// <para><b>So the file is reverted only when the formatter would have produced it from the committed
/// version</b> — the working copy is exactly what formatting the committed text gives, with this
/// repository's settings. Then the revert takes back formatting and nothing else, which is what the
/// button promises. Anything else is left alone, and the class is excluded from here on: formatting
/// already applied stays until the user undoes it, which is recoverable, and an edit thrown away is
/// not.</para>
/// </summary>
public static class FormattingRevert
{
    /// <summary>What was decided, and in the negative case a sentence saying why.</summary>
    /// <param name="Revert">True when reverting the file would take back formatting and nothing else.</param>
    /// <param name="Reason">Null when <paramref name="Revert"/> is true; otherwise why the file was left alone.</param>
    public sealed record Decision(bool Revert, string? Reason)
    {
        public static Decision Yes { get; } = new(true, null);

        public static Decision No(string reason) => new(false, reason);
    }

    /// <summary>
    /// Whether <paramref name="workingCopy"/> differs from <paramref name="committed"/> only by what
    /// the formatter did to it.
    /// </summary>
    /// <param name="status">The file's working-copy status. Only <see cref="VcsFileStatus.Modified"/>
    /// can be reverted to a committed version; every other status either has no committed version to
    /// go back to (added, untracked — a revert deletes the file) or is not a formatting change.</param>
    /// <param name="committed">The file's text at the last commit, or null when there is none.</param>
    /// <param name="workingCopy">The file's text on disk now.</param>
    /// <param name="withinParent">The package the file's classes live in, for a file whose committed
    /// text carries no within clause of its own.</param>
    /// <param name="settings">The repository's settings: whether formatting runs at all, and how.</param>
    /// <param name="rootClassId">The id of the file's outermost class, for the declaration-order lookup.</param>
    /// <param name="isSimpleType">The same lookup the formatter was given, so both render alike.</param>
    public static Decision Decide(
        VcsFileStatus? status,
        string? committed,
        string workingCopy,
        string? withinParent,
        StyleCheckingSettings settings,
        string? rootClassId = null,
        Func<string, string, bool>? isSimpleType = null)
    {
        ArgumentNullException.ThrowIfNull(workingCopy);
        ArgumentNullException.ThrowIfNull(settings);

        if (status != VcsFileStatus.Modified)
            return Decision.No(status is VcsFileStatus.Added or VcsFileStatus.Untracked
                ? "the file has never been committed, so reverting it would delete it"
                : "the file has no committed version to go back to");

        if (committed is null)
            return Decision.No("the file has no committed version to go back to");

        // Nothing was formatted in a repository that does not format, so whatever differs is the
        // user's own work.
        if (!settings.ApplyFormattingRules)
            return Decision.No("formatting is switched off for this repository, so its changes are your own");

        // Line endings and the final newline are the write's business, not the formatter's (B236,
        // B251), and a revert restores them anyway.
        var working = Normalise(workingCopy);
        if (string.Equals(working, Normalise(committed), StringComparison.Ordinal))
            return Decision.Yes;

        string formatted;
        try
        {
            formatted = ModelicaPackageSaver.RenderFileSource(
                committed, withinParent, settings.ToFormattingOptions(), out var errors,
                rootClassId, isSimpleType);

            // The formatter leaves a file it cannot parse alone, so a committed version that does
            // not parse was not what the working copy was formatted from.
            if (errors.Count > 0)
                return Decision.No("the committed version does not parse, so its changes cannot be told apart");
        }
        catch (Exception)
        {
            return Decision.No("the committed version could not be formatted to compare against");
        }

        return string.Equals(working, Normalise(formatted), StringComparison.Ordinal)
            ? Decision.Yes
            : Decision.No("the file has other uncommitted changes, which a revert would discard");
    }

    private static string Normalise(string text) =>
        ModelicaFileEncoding.EnsureFinalNewline(ModelicaParserHelper.NormalizeLineEndings(text));
}

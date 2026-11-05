using MLQT.Services.DataTypes;
using ModelicaParser.Comparison;
using RevisionControl;

namespace MLQT.Services.Interfaces;

/// <summary>
/// What kind of change each model in a working copy carries: one that can change what is simulated,
/// or one that cannot (B191).
/// </summary>
/// <remarks>
/// <para><b>The one place the question is asked of a repository.</b> The comparison itself belongs to
/// <see cref="ClassChangeClassifier"/>, which knows about Modelica and nothing about repositories;
/// this is the layer that finds the committed version of each changed file and turns the answer into
/// something keyed by <c>ModelNode.Id</c>. Any surface that wants to mark or filter by the kind of a
/// change asks here, so no two of them can reach different conclusions about the same edit.</para>
///
/// <para><b>Reading a committed version costs a round trip and a parse</b>, so results are cached per
/// file against what the working copy and the repository were when they were computed. The cache
/// answers itself; callers do not have to know it is there.</para>
/// </remarks>
public interface IModelChangeClassifier
{
    /// <summary>
    /// Classifies every model in the files <paramref name="changes"/> names.
    /// </summary>
    /// <remarks>
    /// <para>Only <c>.mo</c> files are looked at, and only ones still on disk: a deleted file has no
    /// model left in the tree to mark. A conflicted file is reported as
    /// <see cref="ClassChangeKind.Unknown"/> without being read — its working copy holds conflict
    /// markers rather than Modelica, and saying so is better than reporting a parse failure.</para>
    ///
    /// <para>Blocking, and meant to be called from a background thread: it reads from the version
    /// control system and parses. The library browser calls it inside the same
    /// <c>Task.Run</c> that already fetches the working copy's status - and draws its tree before
    /// asking, because this can take a long time.</para>
    ///
    /// <para><b>A very large change set is not classified at all</b>: more than two thousand changed
    /// Modelica files returns an empty answer, which by the rule below means "not asked", so every
    /// file is still marked modified. Smaller sets have their committed versions read several at a
    /// time, and two callers asking about one repository at once share one run rather than each
    /// reading every file.</para>
    /// </remarks>
    /// <param name="repository">The repository the changes belong to.</param>
    /// <param name="changes">The working copy's changed files, relative to the VCS root.</param>
    /// <returns>
    /// The kind of change per model, keyed by full Modelica name — the same string
    /// <c>ModelNode.Id</c> carries. <b>Absence means the question was not asked</b>, not that
    /// nothing changed: a class in a modified file that was not itself touched is present and
    /// <see cref="ClassChangeKind.Unchanged"/>. A caller that conflated the two would show a
    /// package in an unclassifiable repository as unmodified rather than as unknown.
    /// <para><b>Or <see cref="Overtaken"/></b>, compared by reference: a newer request for the same
    /// repository arrived while this one waited or read, so this one stopped and its answer is no
    /// answer at all. Applied as one, it took away every kind the caller already had.</para>
    /// </returns>
    IReadOnlyDictionary<string, ClassChangeKind> Classify(
        Repository repository, IReadOnlyList<VcsWorkingCopyFile> changes);

    /// <summary>
    /// What <see cref="Classify"/> returns for a request a newer one overtook. Empty, so a caller
    /// that does not look for it reads "not asked" - but one that does, which is every caller that
    /// keeps an earlier answer, keeps it rather than replacing it with nothing.
    /// </summary>
    static IReadOnlyDictionary<string, ClassChangeKind> Overtaken { get; } =
        new Dictionary<string, ClassChangeKind>(StringComparer.Ordinal);

    /// <summary>
    /// Discards what has been cached for a repository, or for all of them when given null.
    /// </summary>
    /// <remarks>
    /// Rarely needed: the cache keys on the working file's size and timestamp and on the
    /// repository's revision, so an edit, a pull and a commit all invalidate themselves. This is for
    /// the cases where none of those changed but the answer did — a project being unloaded, and
    /// tests.
    /// </remarks>
    void Invalidate(string? repositoryId = null);
}

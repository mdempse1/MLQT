using RevisionControl;
using System.IO;

namespace MLQT.Shared.Dialogs;

public partial class RevisionDiffDialog
{
    [Inject] private IRepositoryService RepositoryService { get; set; } = null!;

    [CascadingParameter]
    private IMudDialogInstance? MudDialog { get; set; }

    [Parameter]
    public string RepositoryId { get; set; } = "";

    [Parameter]
    public string FilePath { get; set; } = "";

    [Parameter]
    public string Revision { get; set; } = "";

    [Parameter]
    public string ShortRevision { get; set; } = "";

    [Parameter]
    public VcsChangeType ChangeType { get; set; }

    private string? _originalContent;
    private string? _modifiedContent;
    private string? _errorMessage;
    private bool _isLoading = true;

    /// <summary>
    /// The revision on the left, or null when nothing came before this one.
    /// </summary>
    private string? _previousRevision;

    /// <summary>
    /// Whether this is an SVN repository, which changes what an empty diff most likely means.
    /// </summary>
    private bool _isSvn;

    /// <summary>
    /// What to say when the two sides are identical.
    /// </summary>
    /// <remarks>
    /// <para><b>A file can be listed as changed by a revision that changed none of its lines</b>, and
    /// in SVN that is not unusual: a merge records <c>svn:mergeinfo</c> on everything it touched, so
    /// a merge commit lists every directory and file it came through. The reported case was exactly
    /// that - r39803 of a real repository, where the file's text is identical at r39802.</para>
    ///
    /// <para>Saying only "did not change this file" is true and leaves the user looking at a list
    /// that says it did, so the reason is worth the sentence (B265).</para>
    /// </remarks>
    private string NoChangesMessage => _isSvn
        ? $"Revision {ShortRevision} changed no lines in this file. In SVN a file is listed as "
          + "modified when only its properties changed - svn:mergeinfo after a merge, for example."
        : $"Revision {ShortRevision} changed no lines in this file.";

    /// <summary>The left-hand pane's label, which has to say what it is showing.</summary>
    private string PreviousLabel =>
        _previousRevision is null ? "Before" : $"Revision {Shorten(_previousRevision)}";

    /// <summary>
    /// A revision identifier short enough to label a pane with. A Git SHA is cut to the seven
    /// characters the rest of the UI uses; an SVN revision number is already short.
    /// </summary>
    internal static string Shorten(string revision) =>
        revision.Length > 7 ? revision[..7] : revision;

    /// <summary>
    /// Which content goes on which side: the revision before the commit on the left, the commit
    /// itself on the right, so what is shown is what the commit changed (B202).
    /// </summary>
    /// <remarks>
    /// <para><b>It used to compare against the working copy</b>, which is a different question and
    /// not the one a user clicking a file in a commit's changed-file list is asking. Against an old
    /// commit in an active repository that diff is mostly other people's later work, and against a
    /// file changed since it is impossible to tell which lines the commit was responsible for.
    /// B155 documented that behaviour after finding it reported confusing numbers; this changes the
    /// behaviour instead.</para>
    ///
    /// <para><b>The change type does not enter into it</b>, and that part is B155's fix, kept.
    /// A file added by the commit has no previous content and an empty left-hand side; a file
    /// deleted by it has no content at the commit and an empty right-hand side. Both fall out of
    /// the content being null, and a branch on <c>ChangeType</c> to say the same thing is a second
    /// statement of it that can disagree - which is exactly what it did, putting the revision's
    /// content in the pane labelled "Working Copy" for an added file.</para>
    /// </remarks>
    internal static (string Original, string Modified) SidesFor(
        string? previousContent, string? revisionContent)
        => (previousContent ?? string.Empty, revisionContent ?? string.Empty);

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var repository = RepositoryService.GetRepository(RepositoryId);
            if (repository == null)
            {
                _errorMessage = "Repository not found.";
                return;
            }

            // The path from the log, passed through as it came. This used to try to convert an SVN
            // log path ("trunk/Modelica/Foo.mo") into a working-copy-relative one by stripping the
            // prefix and checking whether the result was on disk - and it checked against
            // LocalPath while the lookup underneath used VcsRootPath, which are not the same
            // directory when a repository is registered at a library inside the checkout. The strip
            // then never fired and every SVN history diff failed. Which path space a path is in is
            // the VCS layer's question and it is answered there now (B265).
            var filePath = FilePath;
            _isSvn = repository.VcsType == RepositoryVcsType.SVN;

            _previousRevision = await Task.Run(() =>
                RepositoryService.GetPreviousRevision(RepositoryId, Revision));

            var revisionContent = await Task.Run(() =>
                RepositoryService.GetFileContentAtRevision(RepositoryId, filePath, Revision));

            // Null where there is no predecessor at all, which is the first commit of the repository
            // - the same empty left-hand side as a file the commit added, and for the same reason.
            var previousContent = _previousRevision is null
                ? null
                : await Task.Run(() =>
                    RepositoryService.GetFileContentAtRevision(RepositoryId, filePath, _previousRevision));

            (_originalContent, _modifiedContent) = SidesFor(previousContent, revisionContent);

            // A file deleted by this commit has no content at it, which is the answer rather than a
            // failure. Anything else with no content on either side is a failure worth naming.
            if (revisionContent == null && ChangeType != VcsChangeType.Deleted)
            {
                _errorMessage = previousContent == null
                    ? $"Could not load file content for '{filePath}' at {ShortRevision}."
                    : $"Could not load revision content for '{filePath}' at {ShortRevision}.";
            }
        }
        catch (Exception ex)
        {
            _errorMessage = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }
}

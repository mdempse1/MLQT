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

            // For SVN, changed file paths from the log are repo-root-relative (e.g. "trunk/Models/Foo.mo")
            // but GetFileContentAtRevision needs paths relative to the working copy root. Try the path
            // as-is first; if nothing is there, strip the known SVN prefixes. The working copy is still
            // what says which spelling is right, even though neither side of the diff comes from it.
            var filePath = FilePath;

            if (repository.VcsType == RepositoryVcsType.SVN
                && !File.Exists(Path.Combine(repository.LocalPath, filePath)))
            {
                var stripped = StripSvnBranchPrefix(filePath);
                if (stripped != filePath && File.Exists(Path.Combine(repository.LocalPath, stripped)))
                    filePath = stripped;
            }

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

    /// <summary>
    /// Strips standard SVN branch prefixes (trunk/, branches/X/, tags/X/) from a repo-root-relative path.
    /// </summary>
    private static string StripSvnBranchPrefix(string path)
    {
        if (path.StartsWith("trunk/", StringComparison.OrdinalIgnoreCase))
            return path[6..];

        string[] prefixes = ["branches/", "tags/", "tickets/", "releases/"];
        foreach (var prefix in prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Strip "branches/branchName/" — find the second slash
                var rest = path[prefix.Length..];
                var slashIndex = rest.IndexOf('/');
                if (slashIndex >= 0)
                    return rest[(slashIndex + 1)..];
            }
        }

        return path;
    }
}

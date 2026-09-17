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
    /// Which content goes on which side: the revision on the left, the working copy on the right.
    /// </summary>
    /// <remarks>
    /// <para><b>The change type does not enter into it</b>, and that is the fix for B155. This dialog
    /// compares one revision against the working copy - its title says <c>@ &lt;revision&gt;</c>, its
    /// panes are labelled "Revision N" and "Working Copy", and its own empty-diff message says "File
    /// is identical between revision N and the working copy". Three statements of the same contract.
    /// </para>
    ///
    /// <para>The code disagreed with all three for an <b>added</b> file: it put <c>string.Empty</c>
    /// on the left and the <i>revision's</i> content on the right, so the pane labelled "Working
    /// Copy" showed the revision and the pane labelled "Revision N" showed nothing. Against the first
    /// commit of a file that was changed again later, it reported that revision's lines as additions
    /// and never mentioned the lines the working copy actually has - which is what B155 saw, and why
    /// it read as "comparing against nothing". That is the diff of the commit itself, a reasonable
    /// thing to want and not what this dialog offers.</para>
    ///
    /// <para>The <c>Deleted</c> case it also carried was dead: a file deleted at that revision is
    /// absent from the working copy, so the right-hand side is empty by way of
    /// <c>workingCopyContent</c> being null, without a branch to say so. Where it was not dead it was
    /// wrong - a file deleted then re-added would have had its current content hidden.</para>
    /// </remarks>
    internal static (string Original, string Modified) SidesFor(
        string? revisionContent, string? workingCopyContent)
        => (revisionContent ?? string.Empty, workingCopyContent ?? string.Empty);

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

            // For SVN, changed file paths from the log are repo-root-relative (e.g., "trunk/Models/Foo.mo")
            // but GetFileContentAtRevision and the working copy need paths relative to the WC root.
            // Try the path as-is first; if the working copy file isn't found, strip known SVN prefixes.
            var filePath = FilePath;
            var fullPath = Path.Combine(repository.LocalPath, filePath);

            if (repository.VcsType == RepositoryVcsType.SVN && !File.Exists(fullPath))
            {
                var stripped = StripSvnBranchPrefix(filePath);
                if (stripped != filePath)
                {
                    var strippedFullPath = Path.Combine(repository.LocalPath, stripped);
                    if (File.Exists(strippedFullPath))
                    {
                        filePath = stripped;
                        fullPath = strippedFullPath;
                    }
                }
            }

            var revisionContent = await Task.Run(() =>
                RepositoryService.GetFileContentAtRevision(RepositoryId, filePath, Revision));

            string? workingCopyContent = null;
            if (File.Exists(fullPath))
            {
                workingCopyContent = await ModelicaFileEncoding.ReadAllTextOnlyAsync(fullPath);
            }

            (_originalContent, _modifiedContent) = SidesFor(revisionContent, workingCopyContent);

            if (revisionContent == null && workingCopyContent == null)
            {
                _errorMessage = $"Could not load file content. Revision content not found for '{filePath}' at {ShortRevision}, and working copy not found at '{fullPath}'.";
            }
            else if (revisionContent == null && ChangeType != VcsChangeType.Added)
            {
                _errorMessage = $"Could not load revision content for '{filePath}' at {ShortRevision}.";
            }
            else if (workingCopyContent == null && ChangeType != VcsChangeType.Deleted)
            {
                _errorMessage = $"Working copy not found at '{fullPath}'.";
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

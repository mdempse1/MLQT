namespace MLQT.Shared.Dialogs;

/// <summary>
/// What a VCS dialog did to the working copy, recorded as it happens so the caller learns it however
/// the dialog was closed (B296).
/// </summary>
/// <remarks>
/// <para><b>Why not the dialog's result.</b> A dialog closed with Escape, or from the backdrop, is
/// cancelled without passing through any code of its own, and a merge or rebase cancelled in its
/// conflict phase has still rewritten the working copy. The result said "cancelled", so the browser
/// did nothing: the libraries it showed were the ones from before the merge.</para>
///
/// <para><b>Why the dialog does not start the analysis itself.</b> The dialogs used to fire
/// <see cref="AppState.VcsFilesChanged"/> while still open, and the browser then removed and reloaded
/// every library once they closed - so the analysis ran over a graph being rebuilt under it. The
/// dialog records what it did; the browser reloads, and only then starts the analysis.</para>
/// </remarks>
public sealed class VcsDialogOutcome
{
    /// <summary>Files in the working copy were rewritten, so the libraries have to be reloaded.</summary>
    public bool WorkingCopyChanged { get; set; }

    /// <summary>
    /// A merge or rebase is still unfinished, with conflicts in the working copy. The formatter must
    /// not be run over it: a file with conflict markers in it is not Modelica.
    /// </summary>
    public bool LeftInProgress { get; set; }

    /// <summary>
    /// Files the dialog may have rewritten, relative to the working copy root, for a caller that
    /// reloads only those rather than everything - a revert. Recorded before the operation runs, so
    /// one that fails part way, having reverted some files and not others, still has them reloaded:
    /// reloading a file that did not change costs a parse, and not reloading one that did leaves the
    /// graph describing text that is no longer on disk.
    /// </summary>
    public List<string> FilesTouched { get; } = new();
}

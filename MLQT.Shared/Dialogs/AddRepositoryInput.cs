using System.IO;

namespace MLQT.Shared.Dialogs;

/// <summary>
/// Whether the Add Repository dialog has enough, and valid, input to proceed.
/// </summary>
/// <remarks>
/// Extracted from <c>AddRepositoryDialog</c> in 7a-3. The messages are the entire feedback a user
/// gets when the dialog refuses, and none of them was reachable by a test — the only way to see one
/// was to open the dialog and get it wrong on purpose.
/// </remarks>
public static class AddRepositoryInput
{
    /// <summary>
    /// The message to show, or <c>null</c> when the input is good enough to try.
    /// </summary>
    /// <param name="isLocalFolder">
    /// True for the "existing folder" tab, false for the "check out from a URL" tab. The two tabs
    /// have entirely different required fields, and validating the wrong set is how a dialog comes
    /// to complain about an empty box the user cannot see.
    /// </param>
    /// <remarks>
    /// Validation only — it never creates anything. Creating the checkout directory is a side effect
    /// and stays with the caller, so that asking "is this input valid?" cannot leave a directory
    /// behind on disk.
    /// </remarks>
    public static string? Validate(bool isLocalFolder, string? path, string? url, string? checkoutPath)
    {
        if (isLocalFolder)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "You need to select a directory before you can add a repository";

            if (!Directory.Exists(path))
                return $"The directory selected does not exist ({path}). You need to select a directory on this machine";

            return null;
        }

        if (string.IsNullOrWhiteSpace(url))
            return "You need to specify a url to checkout the repository from";

        if (string.IsNullOrWhiteSpace(checkoutPath))
            return "You need to specify a directory that the repository can be checked out to";

        // The checkout directory is allowed not to exist - it is created on the way past. Only its
        // absence from the form is an error.
        return null;
    }
}

using MLQT.Services.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Loads and unloads the libraries of the <b>Reference Libraries</b> setting: folders on this machine
/// whose libraries are loaded so references out of the user's code resolve, and are never checked.
/// </summary>
/// <remarks>
/// <para>One place for both directions, because they have to agree on which loaded libraries a folder
/// contributed. Loading lived in the main layout and ran only when a project was loaded, so removing a
/// folder in the settings changed nothing until the next load: its classes stayed in the graph and
/// went on resolving references and counting as known words, and a check of every class reported what
/// it had before - then a restart reported something else.</para>
/// </remarks>
public interface IReferenceLibraryLoader
{
    /// <summary>
    /// Loads every library under <paramref name="folders"/> that is not loaded already. A folder
    /// already loaded, or a library another copy of which is, is skipped
    /// (<see cref="ReferenceLibraryRules.ReasonToSkip"/>), so calling it again after a change loads
    /// only what is new.
    /// </summary>
    /// <param name="folders">The configured folders.</param>
    /// <param name="useEncryptedDocumentation">Whether encrypted libraries are recovered from their
    /// documentation.</param>
    Task<ReferenceLibraryLoadSummary> LoadAsync(IReadOnlyList<string> folders, bool useEncryptedDocumentation);

    /// <summary>
    /// Unloads every library this setting loaded from <paramref name="folder"/> or below it, classes
    /// and files with it, and marks the dependency edges for re-analysis. A library in a repository
    /// is never touched, even one under that folder.
    /// </summary>
    /// <returns>How many libraries were unloaded.</returns>
    int UnloadFolder(string folder);
}

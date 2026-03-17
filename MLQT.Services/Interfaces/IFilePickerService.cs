using MLQT.Services.DataTypes;

namespace MLQT.Services.Interfaces;

/// <summary>
/// Service for picking and reading files.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Opens a file picker dialog and reads the selected file.
    /// </summary>
    /// <param name="fileExtension">The file extension to filter (e.g., ".mo").</param>
    /// <returns>The contents of the selected file, or null if cancelled.</returns>
    Task<string?> PickAndReadFileAsync(string fileExtension);

    /// <summary>
    /// Opens a file picker dialog for a Modelica package file and returns information about it.
    /// If package.mo is selected, this provides the directory path for recursive loading.
    /// </summary>
    /// <param name="fileExtension">The file extension to filter (e.g., ".mo").</param>
    /// <returns>File picker result with path and content, or null if cancelled.</returns>
    Task<FilePickerResult?> PickModelicaFileAsync(string fileExtension);

    /// <summary>
    /// Opens a folder picker dialog to select a directory.
    /// </summary>
    /// <param name="title">The title of the folder picker dialog.</param>
    /// <returns>The selected directory path, or null if cancelled.</returns>
    Task<string?> PickFolderAsync(string title = "Select folder");
}

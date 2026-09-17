using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Photino.Services;

/// <summary>
/// The file and folder pickers, over Photino's native dialogs.
/// </summary>
/// <remarks>
/// <para>A thin adapter, as the plan predicted: <c>PhotinoWindow.ShowOpenFile</c> and
/// <c>ShowOpenFolder</c> call the operating system's own dialogs on Windows, Linux and macOS through
/// <c>Photino.Native</c>, so none of the Win32 <c>IFileDialog</c> or GTK <c>FileChooser</c> work the
/// original sketch expected is needed.</para>
///
/// <para>The MAUI implementation returned file *content* as well as a path, because MAUI's picker
/// hands back a stream rather than a location. Having a real path makes this simpler, not harder —
/// the file is read here.</para>
///
/// <para>Probe 11 (<c>filepicker.wired</c>) asserts only that a picker resolves. That a dialog opens
/// and returns a path stays a manual check, once per platform — 7b-5 and 7b-6.</para>
/// </remarks>
internal sealed class PhotinoFilePickerService(PhotinoWindowAccessor windows) : IFilePickerService
{
    public Task<string?> PickAndReadFileAsync(string fileExtension)
    {
        var path = PickFile(fileExtension);
        return Task.FromResult(path is null ? null : ReadOrNull(path));
    }

    public Task<FilePickerResult?> PickModelicaFileAsync(string fileExtension)
    {
        var path = PickFile(fileExtension);
        if (path is null)
            return Task.FromResult<FilePickerResult?>(null);

        var content = ReadOrNull(path);
        if (content is null)
            return Task.FromResult<FilePickerResult?>(null);

        // A package.mo is the root of a directory-shaped library, and callers act on the directory.
        var isPackage = string.Equals(Path.GetFileName(path), "package.mo", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult<FilePickerResult?>(new FilePickerResult
        {
            FilePath = path,
            Content = content,
            IsPackageFile = isPackage,
            DirectoryPath = isPackage ? Path.GetDirectoryName(path) : null,
        });
    }

    public Task<string?> PickFolderAsync(string title = "Select folder")
    {
        var window = windows.Window;
        if (window is null)
            return Task.FromResult<string?>(null);

        var chosen = window.ShowOpenFolder(title, multiSelect: false);
        return Task.FromResult(chosen is { Length: > 0 } ? chosen[0] : null);
    }

    private string? PickFile(string fileExtension)
    {
        var window = windows.Window;
        if (window is null)
            return null;

        // Photino wants the pattern with the dot, and callers pass either shape.
        var pattern = fileExtension.StartsWith('.') ? "*" + fileExtension : "*." + fileExtension.TrimStart('*', '.');

        var chosen = window.ShowOpenFile(
            "Please select a Modelica file",
            multiSelect: false,
            filters: [("Modelica", [pattern])]);

        return chosen is { Length: > 0 } ? chosen[0] : null;
    }

    /// <summary>
    /// Reads a file, or returns null rather than throwing at the caller.
    /// </summary>
    /// <remarks>
    /// Every caller of this interface treats null as "the user cancelled", and a file that cannot be
    /// read is not meaningfully different to them - but it is worth a log line, because a picker that
    /// silently returns nothing for a file the user definitely chose is otherwise baffling.
    /// </remarks>
    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            MLQT.Services.LoggingService.Error(nameof(PhotinoFilePickerService), $"Could not read {path}", ex);
            return null;
        }
    }
}

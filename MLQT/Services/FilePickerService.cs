using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

#if MACOS
using AppKit;
#endif

namespace MLQT.Services;

/// <summary>
/// MAUI implementation of file picker service using native file picker APIs.
/// </summary>
public class FilePickerService : IFilePickerService
{
    public async Task<string?> PickAndReadFileAsync(string fileExtension)
    {
        try
        {
            var customFileType = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { fileExtension } },
                    { DevicePlatform.Android, new[] { fileExtension } },
                    { DevicePlatform.WinUI, new[] { fileExtension } },
                    { DevicePlatform.macOS, new[] { fileExtension } },
                });

            var options = new PickOptions
            {
                PickerTitle = "Please select a Modelica file",
                FileTypes = customFileType
            };

            var result = await FilePicker.Default.PickAsync(options);

            if (result != null)
            {
                // Read the file content
                using var stream = await result.OpenReadAsync();
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }

            return null;
        }
        catch (Exception)
        {
            // User cancelled or an error occurred
            return null;
        }
    }

    public async Task<FilePickerResult?> PickModelicaFileAsync(string fileExtension)
    {
        try
        {
            var customFileType = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { fileExtension } },
                    { DevicePlatform.Android, new[] { fileExtension } },
                    { DevicePlatform.WinUI, new[] { fileExtension } },
                    { DevicePlatform.macOS, new[] { fileExtension } },
                });

            var options = new PickOptions
            {
                PickerTitle = "Please select a Modelica file",
                FileTypes = customFileType
            };

            var result = await FilePicker.Default.PickAsync(options);

            if (result != null)
            {
                // Read the file content
                using var stream = await result.OpenReadAsync();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();

                // Get the file path and check if it's a package file
                var filePath = result.FullPath;
                var fileName = Path.GetFileName(filePath);
                var isPackageFile = fileName.Equals("package.mo", StringComparison.OrdinalIgnoreCase);

                return new FilePickerResult
                {
                    FilePath = filePath,
                    Content = content,
                    IsPackageFile = isPackageFile,
                    DirectoryPath = isPackageFile ? Path.GetDirectoryName(filePath) : null
                };
            }

            return null;
        }
        catch (Exception)
        {
            // User cancelled or an error occurred
            return null;
        }
    }

    public async Task<string?> PickFolderAsync(string title = "Select folder")
    {
        try
        {
            // MAUI doesn't have a built-in folder picker, so we use FolderPicker on supported platforms
#if WINDOWS
            var folderPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            folderPicker.FileTypeFilter.Add("*");

            // Get the current window handle
            var hwnd = ((MauiWinUIWindow)Application.Current!.Windows[0].Handler!.PlatformView!).WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            return folder?.Path;
#elif MACOS
            // Use native folder picker dialog
            var panel = new NSOpenPanel
            {
                CanChooseDirectories = true,
                CanChooseFiles = false,
                AllowsMultipleSelection = false,
                Title = title
            };

            var result = panel.RunModal();
            if (result == NSModalResponse.OK && panel.Urls.Length > 0)
            {
                return panel.Urls[0].Path;
            }
            return null;
#else
            // For Android, iOS, and Mac Catalyst, folder picker is not available
            // Return null to indicate not supported
            throw new NotSupportedException("Folder picker is not available on this platform. Please use the file-based save instead.");
#endif
        }
        catch (Exception)
        {
            return null;
        }
    }
}

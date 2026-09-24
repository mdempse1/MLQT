using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using Photino.NET;

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
///
/// <para><b>Every dialog is opened from the top of the message loop, never from inside WebView2
/// (B283).</b> See <see cref="OnTheMessageLoopAsync{T}"/>.</para>
/// </remarks>
internal sealed class PhotinoFilePickerService(PhotinoWindowAccessor windows) : IFilePickerService
{
    public async Task<string?> PickAndReadFileAsync(string fileExtension)
    {
        var path = await PickFileAsync(fileExtension);
        return path is null ? null : ReadOrNull(path);
    }

    public async Task<FilePickerResult?> PickModelicaFileAsync(string fileExtension)
    {
        var path = await PickFileAsync(fileExtension);
        if (path is null)
            return null;

        // Through ModelicaFileEncoding, not File.ReadAllText: this method returns Modelica source,
        // and the population is mixed — an older library stores curly quotes and accented characters
        // as single Windows-1252 bytes, which a UTF-8 decode turns into replacement characters. The
        // funnel detects the encoding per file and cannot fail (B239).
        var content = ReadModelicaOrNull(path);
        if (content is null)
            return null;

        // A package.mo is the root of a directory-shaped library, and callers act on the directory.
        var isPackage = string.Equals(Path.GetFileName(path), "package.mo", StringComparison.OrdinalIgnoreCase);

        return new FilePickerResult
        {
            FilePath = path,
            Content = content,
            IsPackageFile = isPackage,
            DirectoryPath = isPackage ? Path.GetDirectoryName(path) : null,
        };
    }

    public async Task<string?> PickFolderAsync(string title = "Select folder")
    {
        var window = windows.Window;
        if (window is null)
            return null;

        var chosen = await OnTheMessageLoopAsync(window, () => window.ShowOpenFolder(title, multiSelect: false));
        return chosen is { Length: > 0 } ? chosen[0] : null;
    }

    private async Task<string?> PickFileAsync(string fileExtension)
    {
        var window = windows.Window;
        if (window is null)
            return null;

        // Photino wants the pattern with the dot, and callers pass either shape.
        var pattern = fileExtension.StartsWith('.') ? "*" + fileExtension : "*." + fileExtension.TrimStart('*', '.');

        var chosen = await OnTheMessageLoopAsync(window, () => window.ShowOpenFile(
            "Please select a Modelica file",
            multiSelect: false,
            filters: [("Modelica", [pattern])]));

        return chosen is { Length: > 0 } ? chosen[0] : null;
    }

    /// <summary>
    /// Opens a native dialog from the top of the window's message loop, and waits for it without
    /// holding anything up.
    /// </summary>
    /// <remarks>
    /// <para><b>Why (B283).</b> A click reaches a component through WebView2's
    /// <c>WebMessageReceived</c> callback, and Photino.Blazor handles the message <i>inline</i>, on that
    /// callback's own stack — its <c>SynchronousTaskScheduler</c> runs the task where it is queued. So a
    /// picker opened directly from a click ran the dialog's nested message loop inside WebView2's event
    /// handler, and anything Blazor rendered while the user browsed (a snackbar, progress, the file
    /// monitor) reached <c>SendWebMessage</c> re-entrantly, with that callback still on the stack. WebView2
    /// does not support that re-entrancy, and runtime 153 stops the process on it: MLQT crashed with
    /// <c>0x80000003</c> in <c>EmbeddedBrowserWebView.dll</c> while a user navigated the folder dialog,
    /// with nothing in the log because nothing managed ever saw it.</para>
    ///
    /// <para><b>How.</b> <see cref="PhotinoWindow.Invoke"/> called from a thread-pool thread queues the
    /// work to the UI thread, which reaches it only after the click's callback has returned — so the
    /// dialog runs from the message loop itself, where a nested loop is ordinary. The pool thread waits
    /// in <c>Invoke</c> for as long as the dialog is open; the caller only awaits. Not
    /// <c>await Task.Yield()</c>: that goes wherever the current synchronization context sends it, and
    /// with none it would open a native dialog on a pool thread, which is worse.</para>
    /// </remarks>
    private static Task<T> OnTheMessageLoopAsync<T>(PhotinoWindow window, Func<T> open)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                window.Invoke(() =>
                {
                    try
                    {
                        result.TrySetResult(open());
                    }
                    catch (Exception ex)
                    {
                        result.TrySetException(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                // The window is closing, or Photino refused the call. The caller treats a failed
                // picker as cancelled, and the log says why.
                MLQT.Services.LoggingService.Error(nameof(PhotinoFilePickerService), "Could not open a native dialog", ex);
                result.TrySetResult(default!);
            }
        });
        return result.Task;
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

    /// <summary>
    /// The same, for a file that holds Modelica source: its encoding is detected per file rather
    /// than assumed, which is the whole of why <c>ModelicaFileEncoding</c> exists.
    /// </summary>
    private static string? ReadModelicaOrNull(string path)
    {
        try
        {
            return ModelicaParser.Helpers.ModelicaFileEncoding.ReadAllTextOnly(path);
        }
        catch (Exception ex)
        {
            MLQT.Services.LoggingService.Error(nameof(PhotinoFilePickerService), $"Could not read {path}", ex);
            return null;
        }
    }
}

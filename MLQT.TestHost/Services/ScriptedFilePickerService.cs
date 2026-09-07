using System.Collections.Concurrent;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.TestHost.Services;

/// <summary>
/// A file picker that answers from a queue the test primed.
/// </summary>
/// <remarks>
/// <para>The native picker is the one piece of MLQT's UI that no browser automation can drive: it is
/// an operating-system dialog outside the page. Every journey that opens a library or adds a
/// repository goes through it, so without this the most important journeys are unreachable.</para>
///
/// <para>An empty queue returns null, which is what a real picker returns when the user cancels — so
/// "the user changed their mind" is the default rather than something a test has to arrange, and a
/// journey that opens one more dialog than it primed fails as a cancel rather than by reusing the
/// last answer.</para>
/// </remarks>
public sealed class ScriptedFilePickerService : IFilePickerService
{
    private readonly ConcurrentQueue<string> _paths = new();

    /// <summary>Every path this picker has been asked for, in order, for a test to assert on.</summary>
    public IReadOnlyList<string> Requests => _requests;
    private readonly List<string> _requests = [];

    /// <summary>Primes the next answer. Queue one per dialog the journey will open.</summary>
    public void Enqueue(string path) => _paths.Enqueue(path);

    private string? Next(string request)
    {
        lock (_requests)
            _requests.Add(request);

        return _paths.TryDequeue(out var path) ? path : null;
    }

    public Task<string?> PickAndReadFileAsync(string fileExtension)
    {
        var path = Next($"file:{fileExtension}");
        return Task.FromResult(path is not null && File.Exists(path) ? File.ReadAllText(path) : null);
    }

    public Task<FilePickerResult?> PickModelicaFileAsync(string fileExtension)
    {
        var path = Next($"modelica:{fileExtension}");
        if (path is null || !File.Exists(path))
            return Task.FromResult<FilePickerResult?>(null);

        return Task.FromResult<FilePickerResult?>(new FilePickerResult
        {
            FilePath = path,
            Content = File.ReadAllText(path),
        });
    }

    public Task<string?> PickFolderAsync(string title = "Select folder")
    {
        var path = Next($"folder:{title}");
        return Task.FromResult(path is not null && Directory.Exists(path) ? path : null);
    }
}

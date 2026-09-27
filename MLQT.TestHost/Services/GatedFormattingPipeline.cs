using MLQT.Services.Interfaces;
using ModelicaGraph;

namespace MLQT.TestHost.Services;

/// <summary>
/// The application's own formatting pipeline, with a door a journey can close in front of either pass.
/// </summary>
/// <remarks>
/// <para>Everything is passed to the real <c>FormattingPipeline</c>, so a journey still formats a
/// real library on disk. What this adds is a way to hold one pass part-way, which is the only way to
/// see what the window does <i>while</i> it runs: the incremental pass is startup's and a project
/// switch's step 2 (B407, B423), and the full pass is Format All Files (B385). Test host only.</para>
/// </remarks>
public sealed class GatedFormattingPipeline(IFormattingPipeline inner) : IFormattingPipeline
{
    private HostGate? _modifiedFilesGate;
    private HostGate? _allFilesGate;

    /// <summary>Holds the next <see cref="FormatModifiedFilesAsync"/> - startup's step 2.</summary>
    public HostGate HoldModifiedFiles() => _modifiedFilesGate = new HostGate();

    /// <summary>Holds the next <see cref="SaveAllLibrariesWithFormattingAsync"/> - Format All Files.</summary>
    public HostGate HoldAllFiles() => _allFilesGate = new HostGate();

    public async Task<int> FormatModifiedFilesAsync()
    {
        if (Interlocked.Exchange(ref _modifiedFilesGate, null) is { } gate)
            await gate.PassAsync();
        return await inner.FormatModifiedFilesAsync();
    }

    public async Task<IReadOnlyList<string>> SaveAllLibrariesWithFormattingAsync(
        string? filterRepositoryId = null, Action<string, Exception>? onLibraryFailed = null)
    {
        if (Interlocked.Exchange(ref _allFilesGate, null) is { } gate)
            await gate.PassAsync();
        return await inner.SaveAllLibrariesWithFormattingAsync(filterRepositoryId, onLibraryFailed);
    }

    public Task FormatChangedFilesAsync(IEnumerable<string> changedFilePaths, StyleCheckingSettings styleSettings)
        => inner.FormatChangedFilesAsync(changedFilePaths, styleSettings);

    public IReadOnlyDictionary<string, DateTime> WrittenFileTimestamps => inner.WrittenFileTimestamps;

    public void ClearWrittenFileTimestamps() => inner.ClearWrittenFileTimestamps();
}

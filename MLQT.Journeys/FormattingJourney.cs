using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using MLQT.Shared.Models;
using ModelicaGraph;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 4 — a settings change reruns formatting, and the file on disk changes.
/// </summary>
/// <remarks>
/// <para>Driven through <see cref="IFormattingPipeline"/> rather than by clicking, because 7a-4 put
/// both formatting paths behind that interface precisely so this journey could reach them. Clicking
/// through the settings panel would test the panel; this tests that the host resolves the pipeline
/// the app registers, and that it writes to a real working copy.</para>
///
/// <para>What makes it a journey rather than a unit test: a real library on disk, in a real Git
/// working copy, loaded through the real service graph, with the real formatter writing to it.
/// Everything the desktop app does except render the button.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class FormattingJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    [Fact]
    public async Task FormattingAChangedFile_RewritesItOnDisk()
    {
        var pipeline = host.Services.GetRequiredService<IFormattingPipeline>();
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();

        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);

        // Stated as a precondition rather than left implicit: the formatter only touches files the
        // graph knows, so a library that failed to load would make this test pass by doing nothing.
        var fileId = GraphBuilder.GenerateFileId(_library.ModifiedFile);
        Assert.True(libraries.CombinedGraph.GetModelsInFile(fileId).Any(),
            "the fixture library did not load - the graph holds no classes for Modified.mo");

        var before = File.ReadAllText(_library.ModifiedFile);
        var settings = new StyleCheckingSettings { ApplyFormattingRules = true };

        await pipeline.FormatChangedFilesAsync([_library.ModifiedFile], settings);

        var after = File.ReadAllText(_library.ModifiedFile);
        Assert.NotEqual(before, after);

        // Still valid Modelica, and still the same class: a formatter that rewrote it into something
        // else would satisfy "the file changed" and be a disaster.
        Assert.Contains("model Modified", after);
        Assert.Contains("end Modified;", after);
    }

    [Fact]
    public async Task TheWriteIsRecorded_SoTheMonitorDoesNotChaseItsOwnTail()
    {
        // The file monitor tells MLQT's writes from the user's by their timestamps. An unrecorded
        // write looks like a user edit, which starts a formatting pass on the formatter's output.
        var pipeline = host.Services.GetRequiredService<IFormattingPipeline>();
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();

        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);
        pipeline.ClearWrittenFileTimestamps();

        await pipeline.FormatChangedFilesAsync(
            [_library.ModifiedFile], new StyleCheckingSettings { ApplyFormattingRules = true });

        Assert.True(pipeline.WrittenFileTimestamps.ContainsKey(_library.ModifiedFile));
        Assert.Equal(File.GetLastWriteTimeUtc(_library.ModifiedFile),
                     pipeline.WrittenFileTimestamps[_library.ModifiedFile]);
    }

    [Fact]
    public async Task WithFormattingSwitchedOff_TheWorkingCopyIsUntouched()
    {
        // The promise the settings page makes. Getting this wrong rewrites files for a user who
        // asked MLQT not to.
        var pipeline = host.Services.GetRequiredService<IFormattingPipeline>();
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();

        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);
        var before = Directory.GetFiles(_library.LibraryPath)
                              .ToDictionary(f => f, File.ReadAllText);

        await pipeline.FormatChangedFilesAsync(
            before.Keys, new StyleCheckingSettings { ApplyFormattingRules = false });

        foreach (var (path, content) in before)
            Assert.Equal(content, File.ReadAllText(path));
    }
}

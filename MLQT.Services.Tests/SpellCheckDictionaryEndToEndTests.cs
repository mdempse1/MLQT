using MLQT.Services;
using MLQT.Services.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaGraph;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// End-to-end check that a repository's accepted spellings actually reach the spell checker that
/// style checking runs with. The settings page reading the same file is not evidence of that: the
/// two go through different calls, and a word the user can see listed but still gets reported is
/// worse than no list at all, because nothing on screen explains it.
/// </summary>
public class SpellCheckDictionaryEndToEndTests : IDisposable
{
    private readonly string _repoDir = Path.Combine(
        Path.GetTempPath(), "mlqt-spellcheck", Guid.NewGuid().ToString("N"));

    public SpellCheckDictionaryEndToEndTests() => Directory.CreateDirectory(_repoDir);

    public void Dispose()
    {
        try { Directory.Delete(_repoDir, recursive: true); } catch (IOException) { }
    }

    private void WriteDictionary(params string[] words)
    {
        Directory.CreateDirectory(Path.Combine(_repoDir, ".mlqt"));
        File.WriteAllLines(Path.Combine(_repoDir, ".mlqt", "dictionary.txt"), words);
    }

    [Fact]
    public async Task AnAcceptedWordIsNotReported_ButAnUnknownOneStillIs()
    {
        WriteDictionary("kinemtics");

        var libraries = new LibraryDataService();
        var settingsService = new InMemorySettingsService();
        var monitoring = new FileMonitoringService();
        var repositories = new RepositoryService(libraries, settingsService, monitoring);
        var review = new CodeReviewService();
        var service = new StyleCheckingService(
            libraries, repositories, settingsService,
            new CustomDictionaryService(), new DictionaryManagerService(), review);

        var found = new List<LogMessage>();
        service.OnViolationsFound += v => { lock (found) found.AddRange(v); };

        var repository = new Repository
        {
            Name = "R",
            LocalPath = _repoDir,
            VcsRootPath = _repoDir,
            StyleSettings = new StyleCheckingSettings { SpellCheckDescription = true },
        };

        var library = await libraries.AddLibraryFromFileAsync(
            Path.Combine(_repoDir, "Lib.mo"),
            "model Lib \"Vehicle kinemtics and hendling\"\nend Lib;");
        library.RepositoryId = repository.Id;

        await service.StartBackgroundCheckingAsync(repository);

        await Task.Delay(200);   // let the worker pick the queue up before watching IsRunning
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (service.IsRunning && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        await Task.Delay(600);

        List<string> flagged;
        lock (found)
            flagged = found.Where(m => m.Summary.Contains("Misspelled word")).Select(m => m.Summary).ToList();

        Assert.DoesNotContain(flagged, s => s.Contains("kinemtics"));   // accepted by the repository
        Assert.Contains(flagged, s => s.Contains("hendling"));          // nothing accepted this one
    }
    [Fact]
    public void AListChangedOnDiskTakesEffectWithoutRestarting()
    {
        // The list is a committed file, so it also arrives by version control update or a text
        // editor. Reading it once per session meant the settings page showed the new words while
        // checking went on reporting them, with nothing on screen to explain why.
        WriteDictionary("kinemtics");

        var libraries = new LibraryDataService();
        var settingsService = new InMemorySettingsService();
        var monitoring = new FileMonitoringService();
        var repositories = new RepositoryService(libraries, settingsService, monitoring);
        var service = new StyleCheckingService(
            libraries, repositories, settingsService,
            new CustomDictionaryService(), new DictionaryManagerService(), new CodeReviewService());

        var before = service.EnsureSpellChecker(_repoDir, new[] { "en_US" });
        Assert.True(before.IsCorrect("kinemtics"));
        Assert.False(before.IsCorrect("hendling"));

        WriteDictionary("kinemtics", "hendling");

        var after = service.EnsureSpellChecker(_repoDir, new[] { "en_US" });
        Assert.True(after.IsCorrect("hendling"));
    }
}

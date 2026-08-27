using ModelicaGraph;
using MLQT.Services;
using MLQT.Services.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// What a reload reports as affected. Callers act on the list — re-analysing dependencies,
/// re-checking style, invalidating rendered content — so a class named twice is work done twice, and
/// in the case of style checking that means its findings reported twice.
/// </summary>
public class ReloadAffectedModelsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-reload", Guid.NewGuid().ToString("N"));

    public ReloadAffectedModelsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteLibrary(string content)
    {
        var lib = Path.Combine(_dir, "P");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), content);
        File.WriteAllText(Path.Combine(lib, "package.order"), "A\nB\n");
        return lib;
    }

    private const string Library = """
        within;
        package P "p"
          model A "a"
            Real x;
          equation
            x = 1;
          end A;

          model B "b"
            Real y;
          equation
            y = 2;
          end B;
        end P;
        """;

    [Fact]
    public async Task AnUnchangedFile_NamesEachClassOnce()
    {
        // Every class comes back twice from an unchanged file — once as removed, once as re-added —
        // and a caller cannot tell that from a class listed twice for two different reasons.
        var lib = WriteLibrary(Library);
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(lib);

        var affected = await service.ReloadFileAsync(Path.Combine(lib, "package.mo"));

        Assert.Equal(affected.Distinct(StringComparer.Ordinal).Count(), affected.Count);
        Assert.Contains("P.A", affected);
        Assert.Contains("P.B", affected);
    }

    [Fact]
    public async Task AClassRemovedFromTheFile_IsStillReportedAsAffected()
    {
        // The list is the union of both sides, not just what the file holds now: a class that has
        // gone still has stale findings and stale rendered content to clear.
        var lib = WriteLibrary(Library);
        var service = new LibraryDataService();
        await service.AddLibraryFromPathAsync(lib);

        File.WriteAllText(Path.Combine(lib, "package.mo"),
            Library.Replace("  model B \"b\"\n    Real y;\n  equation\n    y = 2;\n  end B;\n\n", ""));
        var affected = await service.ReloadFileAsync(Path.Combine(lib, "package.mo"));

        Assert.Contains("P.B", affected);
        Assert.Equal(affected.Distinct(StringComparer.Ordinal).Count(), affected.Count);
    }

    [Fact]
    public async Task AClassQueuedTwice_IsNotReportedTwice()
    {
        // CheckModelsAsync takes whatever a caller passes. The workers run in parallel, so two
        // entries for one class can both pass the already-checked guard before either sets it.
        var lib = WriteLibrary(Library);
        var libraries = new LibraryDataService();
        await libraries.AddLibraryFromPathAsync(lib);

        var settingsService = new InMemorySettingsService();
        var monitoring = new FileMonitoringService();
        var repositories = new RepositoryService(libraries, settingsService, monitoring);
        var service = new StyleCheckingService(
            libraries, repositories, settingsService, new CustomDictionaryService(),
            new DictionaryManagerService(), new CodeReviewService());

        await settingsService.SetAsync("StyleChecking", new StyleCheckingSettings { ClassHasIcon = true });

        var found = new List<LogMessage>();
        service.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        await service.CheckModelsAsync(["P.A", "P.A", "P.A"], libraries.CombinedGraph);
        await service.WaitForCompletionAsync();
        await Task.Delay(700);   // the flush loop batches deliveries

        lock (found)
        {
            var forA = found.Where(m => m.ModelName == "P.A").ToList();
            Assert.Equal(forA.Select(m => m.Summary).Distinct().Count(), forA.Count);
        }
    }
}

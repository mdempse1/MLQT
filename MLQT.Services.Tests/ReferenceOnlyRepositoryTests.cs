using ModelicaGraph;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaParser.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// A repository the user has no say over: a tool's library folder, or someone else's repository that
/// theirs depends on. It is loaded so references into it resolve, and left alone otherwise — findings
/// against code nobody here can change are noise, and settings written beside a vendor's library are
/// settings nobody will read.
/// </summary>
public class ReferenceOnlyRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-reference-only", Guid.NewGuid().ToString("N"));

    public ReferenceOnlyRepositoryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteLibrary(string name)
    {
        var lib = Path.Combine(_dir, name, "P");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            "within;\npackage P\n  model A\n    Real x;\n  equation\n    x = 1;\n  end A;\nend P;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "A\n");
        return Path.Combine(_dir, name);
    }

    private sealed record Harness(
        LibraryDataService Libraries, RepositoryService Repositories,
        StyleCheckingService Checking, CodeReviewService Review);

    private static Harness Build()
    {
        var libraries = new LibraryDataService();
        var settings = new InMemorySettingsService();
        var monitoring = new FileMonitoringService();
        var repositories = new RepositoryService(libraries, settings, monitoring);
        var review = new CodeReviewService();
        var checking = new StyleCheckingService(
            libraries, repositories, settings, new CustomDictionaryService(),
            new DictionaryManagerService(), review);
        return new Harness(libraries, repositories, checking, review);
    }

    [Fact]
    public async Task AReferenceRepository_IsNotStyleChecked()
    {
        var h = Build();
        var added = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        var found = new List<LogMessage>();
        h.Checking.OnViolationsFound += v => { lock (found) found.AddRange(v); };

        h.Checking.StartBackgroundCheckingForRepositories([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        lock (found)
            Assert.Empty(found);
    }

    [Fact]
    public async Task AReferenceRepository_IsNotMeasuredForCoverage()
    {
        // Its coverage is a vendor's achievement or debt, and measuring tens of thousands of their
        // classes is the largest thing the sweep could be asked to do for nobody's benefit.
        var h = Build();
        var added = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings();

        h.Checking.StartBackgroundCheckingForRepositories([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        Assert.All(h.Libraries.GetAllModels(), m => Assert.Null(m.Definition.Coverage));
    }

    [Fact]
    public async Task ARepositoryTheTeamMaintains_IsStillCheckedAndMeasured()
    {
        var h = Build();
        var added = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Ours"), startMonitoring: false, isReferenceOnly: false);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        var found = new List<LogMessage>();
        h.Checking.OnViolationsFound += v => { lock (found) found.AddRange(v); };

        h.Checking.StartBackgroundCheckingForRepositories([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        // No sleep: completion means the final flush has happened, so the violations are already here.
        lock (found)
            Assert.NotEmpty(found);
        Assert.All(h.Libraries.GetAllModels(), m => Assert.NotNull(m.Definition.Coverage));
    }

    [Fact]
    public async Task NothingIsWrittenIntoAReferenceRepository()
    {
        var h = Build();
        var path = WriteLibrary("Vendor");
        var added = await h.Repositories.AddRepositoryAsync(
            path, startMonitoring: false, isReferenceOnly: true);
        added.Repository!.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        await h.Repositories.SaveRepositorySettingsAsync();

        Assert.False(Directory.Exists(Path.Combine(path, ".mlqt")));
    }

    [Fact]
    public async Task ARepositoryTheTeamMaintains_KeepsItsSettingsBesideTheCode()
    {
        var h = Build();
        var path = WriteLibrary("Ours");
        var added = await h.Repositories.AddRepositoryAsync(
            path, startMonitoring: false, isReferenceOnly: false);
        added.Repository!.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        await h.Repositories.SaveRepositorySettingsAsync();

        Assert.True(File.Exists(Path.Combine(path, ".mlqt", "settings.json")));
    }

    [Fact]
    public async Task ItsClassesAreNamedAsOutOfScope()
    {
        var h = Build();
        var vendor = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(vendor.Repository!.Id);

        var excluded = ReferenceOnlyScope.ModelIds(h.Libraries, h.Repositories);

        Assert.NotEmpty(excluded);
        Assert.Contains("P.A", excluded);
    }

    [Fact]
    public void AFolderThatCannotBeWrittenTo_IsOfferedAsReferenceOnly()
    {
        // The answer comes from trying, because permissions are decided by more than the path.
        Assert.True(DirectoryWritability.CanWriteInto(_dir));
        Assert.False(DirectoryWritability.CanWriteInto(Path.Combine(_dir, "no-such-directory")));
    }
}

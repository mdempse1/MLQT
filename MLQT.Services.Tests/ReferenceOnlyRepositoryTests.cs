using ModelicaGraph;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
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

    private string WriteLibrary(string name, string package = "P")
    {
        var lib = Path.Combine(_dir, name, package);
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"),
            $"within;\npackage {package}\n  model A\n    Real x;\n  equation\n    x = 1;\n  end A;\nend {package};\n");
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
            libraries,
            repositories,
            new CustomDictionaryService(),
            new DictionaryManagerService(),
            review);
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
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

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
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        h.Checking.StartBackgroundCheckingForRepositories([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        // No sleep: completion means the final flush has happened, so the findings are already here.
        lock (found)
            Assert.NotEmpty(found);
        Assert.All(h.Libraries.GetAllModels(), m => Assert.NotNull(m.Definition.Coverage));
    }

    // ---- every entry point, not just the one that had the guard (B66) --------------------------

    [Fact]
    public async Task AReferenceRepository_IsNotCheckedByTheSingleRepositoryEntryPoint()
    {
        // The path the Add Repository dialog takes, and the one "Apply" in repository settings takes.
        // Neither had a reference-only guard at all: a repository the user ticked Reference only in
        // that very dialog was style-checked the moment it finished loading, and stayed quiet only
        // while the vendor's own .mlqt/settings.json enabled nothing.
        var h = Build();
        var added = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        var found = new List<LogMessage>();
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        h.Checking.StartBackgroundChecking(added.Repository);
        await h.Checking.WaitForCompletionAsync();

        lock (found)
            Assert.Empty(found);
    }

    [Fact]
    public async Task AReferenceRepository_IsNotCheckedByTheAsyncSingleRepositoryEntryPoint()
    {
        var h = Build();
        var added = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        var found = new List<LogMessage>();
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        await h.Checking.StartBackgroundCheckingAsync(added.Repository);
        await h.Checking.WaitForCompletionAsync();

        lock (found)
            Assert.Empty(found);
    }

    [Fact]
    public async Task AReferenceRepository_HasNoWholeGraphAnalysesRunOnItEither()
    {
        // The graph analyses had no guard on any path — including inside the one entry point that
        // carefully skipped reference-only repositories for the per-class workers, which handed the
        // unfiltered list straight to the graph pass. "It is not style-checked" held for the rules
        // that run per class and not for the six that run over the graph.
        //
        // PackageOrder is the one to use: it needs no dependency analysis, and the fixture's
        // package.order lists only A while the package also holds nothing else — so a run that
        // happens produces findings and a run that does not produces none.
        var h = Build();
        var path = WriteLibrary("Vendor");
        File.WriteAllText(Path.Combine(path, "P", "package.order"), "A\nGone\n");

        var added = await h.Repositories.AddRepositoryAsync(path, startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings();
        added.Repository.StyleSettings.RuleSeverities[RuleIds.PackageOrder] = RuleSeverity.Warning;

        var found = new List<LogMessage>();
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        await h.Checking.RunGraphAnalysesForRepositoriesAsync([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        lock (found)
            Assert.Empty(found);
    }

    [Fact]
    public async Task ARepositoryTheTeamMaintains_DoesGetTheWholeGraphAnalyses()
    {
        // The other half: the guard must not be the reason nothing ran.
        var h = Build();
        var path = WriteLibrary("Ours");
        File.WriteAllText(Path.Combine(path, "P", "package.order"), "A\nGone\n");

        var added = await h.Repositories.AddRepositoryAsync(path, startMonitoring: false, isReferenceOnly: false);
        await h.Repositories.LoadLibrariesAsync(added.Repository!.Id);
        added.Repository.StyleSettings = new StyleCheckingSettings();
        added.Repository.StyleSettings.RuleSeverities[RuleIds.PackageOrder] = RuleSeverity.Warning;

        var found = new List<LogMessage>();
        h.Checking.OnFindingsFound += v => { lock (found) found.AddRange(v); };

        await h.Checking.RunGraphAnalysesForRepositoriesAsync([added.Repository]);
        await h.Checking.WaitForCompletionAsync();

        lock (found)
            Assert.Contains(found, m => m.RuleId == RuleIds.PackageOrder);
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

    // ---- the other way of saying it: a library with no repository at all (B80) -----------------

    [Fact]
    public async Task AReadableLibraryLoadedForReference_IsOutOfScopeToo()
    {
        // Settings > Reference Libraries loads libraries with no repository, and it holds readable
        // ones by design — a tool's library folder ships MSL as source. Such a library was neither an
        // external stub nor in a reference-only repository, so nothing recognised it and the Metrics
        // tab counted a vendor's library in its Size census.
        var h = Build();
        var library = await h.Libraries.AddLibraryFromPathAsync(Path.Combine(WriteLibrary("Vendor"), "P"));
        library.IsReferenceOnly = true;

        var excluded = ReferenceOnlyScope.ModelIds(h.Libraries, h.Repositories);

        Assert.Contains("P.A", excluded);
        Assert.True(ReferenceOnlyScope.IsReference(library, h.Repositories));
    }

    [Fact]
    public async Task TheSameLibraryLoadedNormally_IsNot()
    {
        // The guard: the flag is what makes the difference, not "has no repository". A library the
        // user opened directly is their own code.
        var h = Build();
        var library = await h.Libraries.AddLibraryFromPathAsync(Path.Combine(WriteLibrary("Mine"), "P"));

        Assert.Empty(ReferenceOnlyScope.ModelIds(h.Libraries, h.Repositories));
        Assert.False(ReferenceOnlyScope.IsReference(library, h.Repositories));
    }

    [Fact]
    public async Task AClassTheUserAlsoHasFromSource_SurvivesAReferenceLibraryClaimingIt()
    {
        // Same rule as for a reference-only repository: a vendor's copy of a class the user also has
        // must not take the user's own copy out of scope.
        var h = Build();
        var reference = await h.Libraries.AddLibraryFromPathAsync(Path.Combine(WriteLibrary("Vendor"), "P"));
        reference.IsReferenceOnly = true;
        var ours = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Ours"), startMonitoring: false, isReferenceOnly: false);
        await h.Repositories.LoadLibrariesAsync(ours.Repository!.Id);

        Assert.DoesNotContain("P.A", ReferenceOnlyScope.ModelIds(h.Libraries, h.Repositories));
    }

    [Fact]
    public async Task AReferenceLibraryIsNotMeasuredForCoverageEither()
    {
        // Distinct package names, so the two libraries really do contribute different classes and the
        // assertion has something to be about.
        var h = Build();
        var library = await h.Libraries.AddLibraryFromPathAsync(
            Path.Combine(WriteLibrary("Vendor", "Vend"), "Vend"));
        library.IsReferenceOnly = true;
        var ours = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Ours"), startMonitoring: false, isReferenceOnly: false);
        await h.Repositories.LoadLibrariesAsync(ours.Repository!.Id);
        ours.Repository!.StyleSettings = new StyleCheckingSettings { ClassHasDescription = true };

        h.Checking.StartBackgroundCheckingForRepositories([ours.Repository]);
        await h.Checking.WaitForCompletionAsync();

        // The vendor's classes are measured for nothing; the user's own are measured.
        Assert.Null(h.Libraries.GetModelById("Vend.A")!.Definition.Coverage);
        Assert.NotNull(h.Libraries.GetModelById("P.A")!.Definition.Coverage);
    }

    [Fact]
    public async Task AClassTheUserAlsoHasAsSource_IsNotOutOfScope()
    {
        // A tool ships the encrypted build of a library the user has checked out as source, so both
        // claim the same class ids. The graph keeps the readable source; excluding the id because the
        // vendor copy also has it left the user's own class unchecked, unmeasured, and absent from the
        // Coverage scope list — with the vendor copy, which nobody can see, as the only explanation.
        var h = Build();
        var vendor = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Vendor"), startMonitoring: false, isReferenceOnly: true);
        await h.Repositories.LoadLibrariesAsync(vendor.Repository!.Id);
        var ours = await h.Repositories.AddRepositoryAsync(
            WriteLibrary("Ours"), startMonitoring: false, isReferenceOnly: false);
        await h.Repositories.LoadLibrariesAsync(ours.Repository!.Id);

        var excluded = ReferenceOnlyScope.ModelIds(h.Libraries, h.Repositories);

        Assert.DoesNotContain("P.A", excluded);
        Assert.DoesNotContain("P", excluded);
    }

    [Fact]
    public void AFolderThatCannotBeWrittenTo_IsOfferedAsReferenceOnly()
    {
        // The answer comes from trying, because permissions are decided by more than the path.
        Assert.True(DirectoryWritability.CanWriteInto(_dir));
        Assert.False(DirectoryWritability.CanWriteInto(Path.Combine(_dir, "no-such-directory")));
    }
}

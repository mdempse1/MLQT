using ModelicaGraph;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// B310 — a repository's <c>.mlqt/settings.json</c> is committed, so MLQT writes it only when the
/// user changed something. It used to be rewritten at the end of every load, on every project switch
/// and on every reorder, and since B244 each rewrite added the default-on rules to it: the first launch
/// after that change put a modified file in every repository's commit dialog that nobody had edited.
/// </summary>
public class RepositorySettingsFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-settings-file", Guid.NewGuid().ToString("N"));

    public RepositorySettingsFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A repository holding one library and, when given, a committed settings file.</summary>
    private string WriteRepository(string name, string? settingsJson = null)
    {
        var root = Path.Combine(_dir, name);
        var lib = Path.Combine(root, "P");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "package.mo"), "within;\npackage P\n  model A\n  end A;\nend P;\n");
        File.WriteAllText(Path.Combine(lib, "package.order"), "A\n");
        if (settingsJson is not null)
        {
            Directory.CreateDirectory(Path.Combine(root, ".mlqt"));
            File.WriteAllText(SettingsPath(root), settingsJson);
        }
        return root;
    }

    private static string SettingsPath(string root) => Path.Combine(root, ".mlqt", "settings.json");

    private static RepositoryService Build()
        => new(new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService());

    /// <summary>A committed file as a person or an older MLQT wrote it: LF, not the serializer's
    /// layout, and without the default-on rule B244 would add.</summary>
    private const string Committed = "{\n  \"ClassHasDescription\": true\n}\n";

    [Fact]
    public async Task LoadingARepository_LeavesItsCommittedSettingsFileAlone()
    {
        var service = Build();
        var root = WriteRepository("Ours", Committed);

        var added = await service.AddRepositoryAsync(root, startMonitoring: false);
        await service.LoadLibrariesAsync(added.Repository!.Id);

        Assert.Equal(Committed, File.ReadAllText(SettingsPath(root)));
        Assert.True(added.Repository.StyleSettings!.ClassHasDescription);
    }

    [Fact]
    public async Task ReorderingRepositories_LeavesEverySettingsFileAlone()
    {
        var service = Build();
        var first = WriteRepository("First", Committed);
        var second = WriteRepository("Second", Committed);
        await service.AddRepositoryAsync(first, startMonitoring: false);
        var added = await service.AddRepositoryAsync(second, startMonitoring: false);

        Assert.True(service.MoveRepository(added.Repository!.Id, -1));
        await service.SaveRepositorySettingsAsync();

        Assert.Equal(Committed, File.ReadAllText(SettingsPath(first)));
        Assert.Equal(Committed, File.ReadAllText(SettingsPath(second)));
    }

    [Fact]
    public async Task ApplyingARepositorysSettings_RecordsTheDefaults_InThatRepositoryOnly()
    {
        var service = Build();
        var ours = WriteRepository("Ours", Committed);
        var theirs = WriteRepository("Theirs", Committed);
        var added = await service.AddRepositoryAsync(ours, startMonitoring: false);
        await service.AddRepositoryAsync(theirs, startMonitoring: false);

        await service.ApplyRepositorySettingsAsync(added.Repository!.Id);

        Assert.Contains(RuleIds.SingleFilePackage, File.ReadAllText(SettingsPath(ours)));
        Assert.Equal(Committed, File.ReadAllText(SettingsPath(theirs)));
    }

    [Fact]
    public async Task AChangedSetting_IsWritten_WithTheFilesOwnLineEndings()
    {
        var service = Build();
        var crlf = Committed.Replace("\n", "\r\n");
        var root = WriteRepository("Ours", crlf);
        var added = await service.AddRepositoryAsync(root, startMonitoring: false);

        added.Repository!.StyleSettings!.ClassHasIcon = true;
        await service.SaveRepositorySettingsAsync();

        var written = File.ReadAllText(SettingsPath(root));
        Assert.Contains("\"ClassHasIcon\": true", written);
        Assert.Equal(written.Split('\n').Length - 1, written.Split("\r\n").Length - 1);
        Assert.EndsWith("\r\n", written);
    }

    [Fact]
    public async Task ApplyingWithNothingToChange_DoesNotTouchTheFile()
    {
        var service = Build();
        var root = WriteRepository("Ours", Committed);
        var added = await service.AddRepositoryAsync(root, startMonitoring: false);
        await service.ApplyRepositorySettingsAsync(added.Repository!.Id);

        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SettingsPath(root), old);

        await service.ApplyRepositorySettingsAsync(added.Repository.Id);
        await service.SaveRepositorySettingsAsync();

        Assert.Equal(old, File.GetLastWriteTimeUtc(SettingsPath(root)));
    }

    [Fact]
    public async Task AFileThatDoesNotParse_IsNotOverwrittenByLoading()
    {
        // The user's file is wrong, and they are told so in the log; replacing it with the defaults
        // would lose whatever they meant.
        var service = Build();
        const string broken = "{ \"ClassHasDescription\": tru";
        var root = WriteRepository("Ours", broken);

        await service.AddRepositoryAsync(root, startMonitoring: false);

        Assert.Equal(broken, File.ReadAllText(SettingsPath(root)));
    }

    [Fact]
    public async Task ARepositoryWithNoSettingsFile_StillGetsOne_WithTheDefaultsWritten()
    {
        // Creating the file changes nothing anybody committed, and it is how a new repository gets
        // settings the team can share (and how a read-only one is found, B223).
        var service = Build();
        var root = WriteRepository("Ours");

        await service.AddRepositoryAsync(root, startMonitoring: false);

        Assert.Contains(RuleIds.SingleFilePackage, File.ReadAllText(SettingsPath(root)));
    }
}

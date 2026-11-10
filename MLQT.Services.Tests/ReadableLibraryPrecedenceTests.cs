using ModelicaGraph.DataTypes;
using MLQT.Services;
using MLQT.Services.DataTypes;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// Two readable copies of one library in a project - a checkout of MSL and a tool's library folder
/// that ships its own, say.
///
/// <para>Both used to load, into one graph under the same class ids, in parallel. The browser showed
/// the library under the repository whose copy registered first, and the checker read whichever
/// copy's classes were written last: a project holding a checkout of MSL and Dymola's library folder
/// showed MSL under the checkout and checked Dymola's copy, with 76 findings for a file layout the
/// checkout does not have. Now the copy that loses is never loaded: reference-only loses, then the
/// repository lower in the project's list.</para>
///
/// <para><b>What the browser shows is what is checked</b> is the invariant these assert: every class a
/// loaded library lists is, in the graph, the class from that library's own files.</para>
/// </summary>
public class ReadableLibraryPrecedenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-readable-precedence", Guid.NewGuid().ToString("N"));

    public ReadableLibraryPrecedenceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    // ── the rank ──

    private static (string Name, string Location, bool IsReferenceOnly, int Order) Copy(
        string location, bool referenceOnly = false, int order = 0, string name = "Lib") =>
        (name, Path.Combine(Path.GetTempPath(), location), referenceOnly, order);

    [Fact]
    public void ACopyHigherInTheList_Outranks()
    {
        var top = Copy("a", order: 0);
        var lower = Copy("b", order: 1);

        Assert.Equal(top.Location, LibraryPrecedence.ReadableCopyOutranking(lower, [top, lower]));
        Assert.Null(LibraryPrecedence.ReadableCopyOutranking(top, [top, lower]));
    }

    [Fact]
    public void ACopyThatIsNotReferenceOnly_OutranksOneThatIs_WhereverEachStands()
    {
        var referenceAtTop = Copy("a", referenceOnly: true, order: 0);
        var ownBelow = Copy("b", order: 5);

        Assert.Equal(ownBelow.Location, LibraryPrecedence.ReadableCopyOutranking(referenceAtTop, [referenceAtTop, ownBelow]));
        Assert.Null(LibraryPrecedence.ReadableCopyOutranking(ownBelow, [referenceAtTop, ownBelow]));
    }

    [Fact]
    public void TwoCopiesEqualOnBoth_StillGiveOneAnswer()
    {
        // Ordered by location, so exactly one of the two loses, whichever is asked.
        var a = Copy("a");
        var b = Copy("b");

        var aLoses = LibraryPrecedence.ReadableCopyOutranking(a, [a, b]) is not null;
        var bLoses = LibraryPrecedence.ReadableCopyOutranking(b, [a, b]) is not null;

        Assert.NotEqual(aLoses, bLoses);
    }

    [Fact]
    public void TheCopyItself_AndOtherLibraries_NeverOutrankIt()
    {
        var copy = Copy("a", referenceOnly: true, order: 3);
        var sameLocation = Copy("a", order: 0);
        var otherLibrary = Copy("b", order: 0, name: "Other");

        Assert.Null(LibraryPrecedence.ReadableCopyOutranking(copy, [copy, sameLocation, otherLibrary]));
    }

    [Fact]
    public void OfSeveralThatOutrankIt_TheBestIsNamed()
    {
        var lowest = Copy("c", referenceOnly: true, order: 0);
        var middle = Copy("b", order: 2);
        var best = Copy("a", order: 1);

        Assert.Equal(best.Location, LibraryPrecedence.ReadableCopyOutranking(lowest, [lowest, middle, best]));
    }

    // ── a project ──

    /// <summary>
    /// A repository holding library <c>Lib</c>. The two layouts differ the way MSL's checkout and a
    /// tool's copy do - class per file, or nested in package.mo - and the second has a class the
    /// first does not, so a mixed graph would show.
    /// </summary>
    private string WriteRepository(string folder, bool classPerFile)
    {
        var lib = Path.Combine(_root, folder, "Lib");
        Directory.CreateDirectory(lib);

        if (classPerFile)
        {
            File.WriteAllText(Path.Combine(lib, "package.mo"), "package Lib \"Lib\"\nend Lib;\n");
            File.WriteAllText(Path.Combine(lib, "package.order"), "A\n");
            File.WriteAllText(Path.Combine(lib, "A.mo"), "within Lib;\nmodel A \"From the class-per-file copy\"\nend A;\n");
        }
        else
        {
            File.WriteAllText(Path.Combine(lib, "package.mo"),
                "package Lib \"Lib\"\n" +
                "  model A \"From the single-file copy\"\n  end A;\n" +
                "  model OnlyInTheSingleFileCopy \"Extra\"\n  end OnlyInTheSingleFileCopy;\n" +
                "end Lib;\n");
        }

        return Path.Combine(_root, folder);
    }

    private static async Task<(RepositoryService Repositories, LibraryDataService Libraries, Repository First, Repository Second)>
        AddBothAsync(string first, bool firstReferenceOnly, string second, bool secondReferenceOnly)
    {
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, new InMemorySettingsService(), new FileMonitoringService());

        var a = await repositories.AddRepositoryAsync(first, null, "first", startMonitoring: false, isReferenceOnly: firstReferenceOnly);
        var b = await repositories.AddRepositoryAsync(second, null, "second", startMonitoring: false, isReferenceOnly: secondReferenceOnly);
        Assert.True(a.Success && b.Success);

        return (repositories, libraries, a.Repository!, b.Repository!);
    }

    /// <summary>
    /// Exactly one <c>Lib</c> is loaded, it is <paramref name="winner"/>'s, and every class it lists is
    /// in the graph from its own files - what the browser shows under that repository is what is
    /// checked.
    /// </summary>
    private static void AssertOnlyCopyIs(LibraryDataService libraries, Repository winner, Repository loser)
    {
        var lib = Assert.Single(libraries.Libraries, l => l.Name == "Lib");

        Assert.Equal(winner.Id, lib.RepositoryId);
        Assert.Contains(lib.Id, winner.LibraryIds);
        Assert.DoesNotContain(lib.Id, loser.LibraryIds);

        foreach (var id in lib.ModelIds)
        {
            var node = libraries.GetModelById(id);
            Assert.NotNull(node);
            var file = libraries.CombinedGraph.GetNode<FileNode>(node!.ContainingFileId!);
            Assert.NotNull(file);
            Assert.StartsWith(winner.LocalPath, file!.FilePath, StringComparison.OrdinalIgnoreCase);
            Assert.Same(lib, libraries.GetOwningLibrary(id));
        }

        Assert.All(libraries.CombinedGraph.FileNodes,
            f => Assert.False(f.FilePath.StartsWith(loser.LocalPath, StringComparison.OrdinalIgnoreCase),
                $"a file of the copy that lost is in the graph: {f.FilePath}"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadedTogether_TheRepositoryHigherInTheList_Wins(bool classPerFileOnTop)
    {
        // The project load: every repository discovered, then all loaded in parallel. Repeated,
        // because the race this replaces did not lose every time.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var top = WriteRepository($"top{attempt}{classPerFileOnTop}", classPerFileOnTop);
            var below = WriteRepository($"below{attempt}{classPerFileOnTop}", !classPerFileOnTop);
            var (repositories, libraries, first, second) = await AddBothAsync(top, false, below, false);

            await Task.WhenAll(
                Task.Run(() => repositories.LoadLibrariesAsync(second.Id)),
                Task.Run(() => repositories.LoadLibrariesAsync(first.Id)));

            AssertOnlyCopyIs(libraries, first, second);
        }
    }

    [Fact]
    public async Task AReferenceOnlyRepository_LosesToOneThatIsNot_EvenAboveIt()
    {
        // The case that started this: a tool's library folder added to the project, which is
        // reference-only because MLQT cannot write into Program Files.
        var tool = WriteRepository("tool", classPerFile: false);
        var checkout = WriteRepository("checkout", classPerFile: true);
        var (repositories, libraries, first, second) = await AddBothAsync(tool, true, checkout, false);

        await Task.WhenAll(
            Task.Run(() => repositories.LoadLibrariesAsync(first.Id)),
            Task.Run(() => repositories.LoadLibrariesAsync(second.Id)));

        AssertOnlyCopyIs(libraries, second, first);
        Assert.Null(libraries.GetModelById("Lib.OnlyInTheSingleFileCopy"));
    }

    [Theory]
    [InlineData(true)]    // the loaded copy's repository moved below the other
    [InlineData(false)]   // the loaded copy's repository switched to reference only
    public async Task RefreshingTheLoadedCopy_AfterItsRankChanged_KeepsTheLibraryLoaded(bool byMoving)
    {
        // Rank is decided at load, and neither change reloads anything. Ranked against every copy
        // discovered, a refresh of the repository whose copy is loaded - which a VCS update does -
        // skipped it in favour of the other repository's, which nobody would load: the library left
        // the graph until the project was loaded again.
        var (repositories, libraries, first, second) = await AddBothAsync(
            WriteRepository("first", classPerFile: true), false,
            WriteRepository("second", classPerFile: false), false);
        await Task.WhenAll(
            Task.Run(() => repositories.LoadLibrariesAsync(first.Id)),
            Task.Run(() => repositories.LoadLibrariesAsync(second.Id)));
        AssertOnlyCopyIs(libraries, first, second);

        if (byMoving)
            Assert.True(repositories.MoveRepository(first.Id, 1));
        else
            repositories.SetReferenceOnly(first.Id, true);

        await repositories.RefreshRepositoryAsync(first.Id);

        AssertOnlyCopyIs(libraries, first, second);
    }

    [Fact]
    public async Task RemovingTheRepositoryWhoseCopyWon_LoadsTheOtherCopy()
    {
        // The copy that lost was never loaded, so removing the winner left the library out of the
        // graph altogether until the project was loaded again.
        var (repositories, libraries, first, second) = await AddBothAsync(
            WriteRepository("first", classPerFile: true), false,
            WriteRepository("second", classPerFile: false), false);
        await Task.WhenAll(
            Task.Run(() => repositories.LoadLibrariesAsync(first.Id)),
            Task.Run(() => repositories.LoadLibrariesAsync(second.Id)));

        repositories.RemoveRepository(first.Id);
        Assert.DoesNotContain(libraries.Libraries, l => l.Name == "Lib");

        Assert.Equal(1, await repositories.LoadLibrariesNoLongerProvidedAsync());

        var lib = Assert.Single(libraries.Libraries, l => l.Name == "Lib");
        Assert.Equal(second.Id, lib.RepositoryId);
        Assert.Contains(lib.Id, second.LibraryIds);
        Assert.NotNull(libraries.GetModelById("Lib.OnlyInTheSingleFileCopy"));
        Assert.Equal(0, await repositories.LoadLibrariesNoLongerProvidedAsync());
    }

    [Fact]
    public async Task AProjectLoad_RanksAgainstEveryRepositoryInIt()
    {
        // The other half of the rule above: a load of the whole project ranks against every
        // repository's copy, loaded yet or not, so the order decides and not which load ran first.
        var settings = new InMemorySettingsService();
        var setup = new RepositoryService(new LibraryDataService(), settings, new FileMonitoringService());
        var top = await setup.AddRepositoryAsync(
            WriteRepository("top", classPerFile: true), null, "top", startMonitoring: false, isReferenceOnly: false);
        await setup.AddRepositoryAsync(
            WriteRepository("below", classPerFile: false), null, "below", startMonitoring: false, isReferenceOnly: false);
        var projectId = setup.GetActiveProject()?.Id;

        // A restart: the same saved project, loaded by services that have loaded nothing yet.
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, settings, new FileMonitoringService());
        await repositories.LoadRepositorySettingsAsync(projectId);

        var lib = Assert.Single(libraries.Libraries, l => l.Name == "Lib");
        Assert.StartsWith(top.Repository!.LocalPath, lib.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TwoReadableCopiesFromDifferentPlaces_AreTwins_AndOneCopyOrAReadOnlyOneIsNot()
    {
        var libraries = new LibraryDataService();
        var a = await libraries.AddLibraryFromPathAsync(Path.Combine(WriteRepository("a", true), "Lib"));
        var b = await libraries.AddLibraryFromPathAsync(Path.Combine(WriteRepository("b", false), "Lib"));

        Assert.Same(a, LibraryDataService.ReadableTwinOf(b, [a]));
        Assert.Null(LibraryDataService.ReadableTwinOf(a, [a]));
        Assert.Null(LibraryDataService.ReadableTwinOf(b, [new LoadedLibrary { Name = "Lib", SourcePath = b.SourcePath }]));
        Assert.Null(LibraryDataService.ReadableTwinOf(b,
            [new LoadedLibrary { Name = "Lib", SourcePath = "x", SourceType = LibrarySourceType.EncryptedDirectory }]));
    }

    [Fact]
    public async Task ACopyAlreadyLoaded_IsUnloaded_WhenOneThatOutranksItArrives()
    {
        // The reference-only folder was loaded on its own first; the checkout is added after. Its
        // classes must replace the loaded copy's whole, not land over them.
        var libraries = new LibraryDataService();
        var repositories = new RepositoryService(libraries, new InMemorySettingsService(), new FileMonitoringService());

        var tool = await repositories.AddRepositoryAsync(
            WriteRepository("tool", classPerFile: false), null, "tool", startMonitoring: false, isReferenceOnly: true);
        await repositories.LoadLibrariesAsync(tool.Repository!.Id);
        Assert.NotNull(libraries.GetModelById("Lib.OnlyInTheSingleFileCopy"));

        var checkout = await repositories.AddRepositoryAsync(
            WriteRepository("checkout", classPerFile: true), null, "checkout", startMonitoring: false, isReferenceOnly: false);
        await repositories.LoadLibrariesAsync(checkout.Repository!.Id);

        AssertOnlyCopyIs(libraries, checkout.Repository!, tool.Repository!);
        Assert.Null(libraries.GetModelById("Lib.OnlyInTheSingleFileCopy"));
    }
}

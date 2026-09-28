using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// The guards that keep MLQT from judging, or writing to, a library whose source it does not have:
/// a class recovered from a vendor's help HTML, an encrypted <c>package.moe</c> library, and a
/// repository or library loaded for reference only.
///
/// <para>Each of these is documented at its call site and each was executed on every test run — and
/// the whole-solution mutation audit found that <b>inverting any of them broke nothing</b> (B219).
/// The reason is the same every time: the suite asserted the included outcome. A test that checks
/// two ordinary classes and gets two findings still passes when the filter stops excluding, because
/// the thing that should have been excluded was never in the list. So these assert the
/// <b>excluded</b> outcome, and each carries its own positive control in the same test — the
/// ordinary sibling that must still come through, so an assertion of "nothing was reported" cannot
/// pass by reporting nothing at all.</para>
/// </summary>
public class ExternalStubWriteGuardTests
{
    private static ModelNode Model(string id, string code, bool stub = false) =>
        new(id, new ModelDefinition(id.Split('.')[^1], code)) { IsExternalStub = stub };

    /// <summary>A rule that fires on every class, so "was this class checked?" has a visible answer.</summary>
    private static StyleCheckingSettings CheckEverything()
    {
        var settings = new StyleCheckingSettings { ClassHasDescription = true };
        settings.RuleSeverities[RuleIds.ClassDescription] = RuleSeverity.Warning;
        return settings;
    }

    private static IReadOnlyList<Finding> Check(DirectedGraph graph, IEnumerable<ModelNode> models) =>
        LibraryCheckSession.Check(
            graph, models, CheckEverything(),
            new CustomDictionaryService(), new DictionaryManagerService());

    [Fact]
    public void AStubIsNotChecked_WhileItsOrdinarySiblingIs()
    {
        // The mutation that survived was `node is null` → `node is not null` in the filter, which
        // keeps every non-null node and so checks the stub too. Nothing noticed, because no test had
        // ever put a stub in front of LibraryCheckSession.
        var graph = new DirectedGraph();
        var ours = Model("Ours.Widget", "model Widget Real x; end Widget;");
        var stub = Model("Vendor.Cell", "model Cell Real x; end Cell;", stub: true);
        graph.AddNode(ours);
        graph.AddNode(stub);

        var findings = Check(graph, new[] { ours, stub });

        Assert.DoesNotContain(findings, f => f.ModelId == "Vendor.Cell");
        Assert.Contains(findings, f => f.ModelId == "Ours.Widget");   // positive control
    }

    [Fact]
    public void ALibraryOfNothingButStubs_ProducesNoFindingsAtAll()
    {
        // The whole-library shape of the same rule, and the one a user sees: loading a vendor's
        // encrypted library must not add a single finding to their Code Review list.
        var graph = new DirectedGraph();
        var stubs = new[]
        {
            Model("Vendor.A", "model A Real x; end A;", stub: true),
            Model("Vendor.B", "model B Real x; end B;", stub: true),
        };
        foreach (var s in stubs) graph.AddNode(s);

        Assert.Empty(Check(graph, stubs));
    }

    // ---- the write half: which libraries a full save is allowed to rewrite -----------------------

    private static LoadedLibrary Library(
        string name, LibrarySourceType type = LibrarySourceType.Directory,
        string? repositoryId = null, bool referenceOnly = false) =>
        new()
        {
            Name = name,
            SourcePath = Path.Combine(Path.GetTempPath(), name),
            SourceType = type,
            RepositoryId = repositoryId,
            IsReferenceOnly = referenceOnly,
        };

    private static IReadOnlyList<string> Selected(
        IEnumerable<LoadedLibrary> libraries, string? filterRepositoryId = null) =>
        FormattableLibraries
            .Select(libraries, new RepositoryService(
                new LibraryDataService(), new InMemorySettingsService(), new FileMonitoringService()),
                filterRepositoryId)
            .Select(l => l.Name)
            .ToList();

    [Fact]
    public void AnEncryptedLibraryIsNeverFormatted()
    {
        // A write here would replace a vendor's package.moe with MLQT's reconstruction of their
        // documentation — the worst outcome available to this code path, and until now the exclusion
        // that stopped it was covered by every test and depended on by none.
        var selected = Selected(new[]
        {
            Library("Vendor", LibrarySourceType.EncryptedDirectory),
            Library("Ours"),
        });

        Assert.Equal(new[] { "Ours" }, selected);
    }

    [Fact]
    public void AReferenceLibraryIsNeverFormatted()
    {
        var selected = Selected(new[]
        {
            Library("Installed", referenceOnly: true),
            Library("Ours"),
        });

        Assert.Equal(new[] { "Ours" }, selected);
    }

    [Fact]
    public void WithNoRepositoryFilter_TheExclusionsStillApply()
    {
        // The half that used to rest on filterRepositoryId being non-null. "All repositories" is not
        // "all libraries": a null filter is the Format All Files button, which is exactly when a
        // vendor's library must not be swept up.
        var selected = Selected(new[]
        {
            Library("Vendor", LibrarySourceType.EncryptedDirectory),
            Library("Installed", referenceOnly: true),
            Library("Ours", repositoryId: "repo-1"),
        }, filterRepositoryId: null);

        Assert.Equal(new[] { "Ours" }, selected);
    }

    [Fact]
    public void ARepositoryFilterNarrowsToThatRepository()
    {
        var selected = Selected(new[]
        {
            Library("Mine", repositoryId: "repo-1"),
            Library("Theirs", repositoryId: "repo-2"),
        }, filterRepositoryId: "repo-1");

        Assert.Equal(new[] { "Mine" }, selected);
    }
}

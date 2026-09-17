using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 2 — the core product loop: open a library, check it, get findings you can act on.
/// </summary>
/// <remarks>
/// <para>Everything the unit tests cover in pieces, running together against real files: the loader
/// reads a directory off disk, the graph is built from what it parsed, the checker runs the
/// configured rules over it, and the findings arrive where the Code Review page reads them.</para>
///
/// <para>What makes it worth having on top of those unit tests is that each of those steps agrees
/// with the next about a class's identity and its line numbers. That agreement is the thing MLQT
/// gets wrong when it gets anything wrong — it is what B1, the GUI/CLI count mismatch and the
/// package-trimming line shift were all about — and no test of one step can see it.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class StyleCheckJourney(TestHostFixture host) : IDisposable
{
    private readonly LibraryFixture _library = new();

    public void Dispose() => _library.Dispose();

    private async Task<DirectedGraph> LoadAsync()
    {
        var libraries = host.Services.GetRequiredService<ILibraryDataService>();
        await libraries.AddLibraryFromDirectoryAsync(_library.LibraryPath);
        return libraries.CombinedGraph;
    }

    /// <summary>
    /// Checks every loaded class against a named rule.
    /// </summary>
    /// <remarks>
    /// The rule has to be switched on explicitly, and that is not a quirk of the test: MLQT ships
    /// with every style rule off, so a library opened in a repository nobody has configured reports
    /// nothing. A journey that checked with the defaults would assert that MLQT finds no problems in
    /// a file written to have one, and pass.
    /// </remarks>
    private async Task<List<LogMessage>> CheckWithAsync(DirectedGraph graph, string ruleId)
    {
        var checking = host.Services.GetRequiredService<IStyleCheckingService>();
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities[ruleId] = RuleSeverity.Warning;

        var findings = new List<LogMessage>();
        foreach (var model in graph.ModelNodes.Where(m => !m.IsExternalStub))
            findings.AddRange(await checking.CheckModelAsync(model.Definition, settings));

        return findings;
    }

    [Fact]
    public async Task OpeningTheLibrary_LoadsEveryClassInIt()
    {
        var graph = await LoadAsync();

        var ids = graph.ModelNodes.Select(m => m.Id).ToList();

        Assert.Contains("Lib", ids);
        Assert.Contains("Lib.Documented", ids);
        Assert.Contains("Lib.Modified", ids);
        Assert.Contains("Lib.badly_named", ids);
    }

    [Fact]
    public async Task EveryLoadedClass_KnowsWhichFileItCameFrom()
    {
        // The agreement every later step depends on. A class whose file is unknown cannot be shown,
        // diffed, or have a finding's line mapped into a file — the finding is reported against
        // nothing a user can open.
        var graph = await LoadAsync();

        foreach (var model in graph.ModelNodes.Where(m => !m.IsExternalStub))
        {
            Assert.False(string.IsNullOrEmpty(model.ContainingFileId),
                $"{model.Id} was loaded without a file");
            Assert.NotNull(graph.GetNode<ModelicaGraph.DataTypes.FileNode>(model.ContainingFileId!));
        }
    }

    [Fact]
    public async Task CheckingTheLibrary_FindsTheClassThatBreaksARule()
    {
        // The fixture holds one deliberately badly named class. If the pipeline runs and reports
        // nothing, either the rules did not reach the classes or the classes did not reach the rules.
        var graph = await LoadAsync();

        var findings = await CheckWithAsync(graph, RuleIds.NamingConvention);

        Assert.NotEmpty(findings);
        Assert.Contains(findings, f => f.ModelName.Contains("badly_named"));
    }

    [Fact]
    public async Task TheWellFormedClass_IsNotReportedForNaming()
    {
        // The other half, and the one that catches a rule firing on everything: a check that reports
        // every class is as useless as one that reports none, and only this direction says which.
        var graph = await LoadAsync();

        var findings = await CheckWithAsync(graph, RuleIds.NamingConvention);

        Assert.DoesNotContain(findings, f => f.ModelName.EndsWith("Documented"));
    }

    [Fact]
    public async Task WithNoRuleEnabled_NothingIsReported()
    {
        // Stated because it is easy to read the test above as "MLQT finds naming problems" when what
        // it means is "MLQT finds them once asked to". Every rule ships off; a repository nobody has
        // configured reports nothing, by design.
        var graph = await LoadAsync();
        var checking = host.Services.GetRequiredService<IStyleCheckingService>();

        var findings = new List<LogMessage>();
        foreach (var model in graph.ModelNodes.Where(m => !m.IsExternalStub))
            findings.AddRange(await checking.CheckModelAsync(model.Definition, new StyleCheckingSettings()));

        Assert.Empty(findings);
    }

    [Fact]
    public async Task EveryFindingPointsAtALineSomebodyCanOpen()
    {
        // A finding whose line is zero or negative sends the user nowhere. This is the invariant
        // ReportLocation guarantees, checked against findings that came out of the real pipeline
        // rather than ones a test constructed.
        var graph = await LoadAsync();

        var findings = await CheckWithAsync(graph, RuleIds.NamingConvention);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.True(f.LineNumber >= 1, $"{f.ModelName} reports line {f.LineNumber}"));
    }

    [Fact]
    public async Task ThePipelineGoesQuietAfterwards()
    {
        // The signal the other journeys wait on, exercised with a library actually loaded rather
        // than against an empty app.
        await LoadAsync();
        await host.WaitForIdleAsync();
    }
}

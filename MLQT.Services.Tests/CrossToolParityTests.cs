using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Guards that the three tools (GUI, CLI, MCP) report the SAME set of style findings on the same
/// library. They deliberately share the count-determining primitives — <see cref="PackageCodeTrimmer"/>,
/// <see cref="StyleCheckContext"/>, <see cref="StyleCheckRunner"/>, <see cref="LibraryCheckSession"/> —
/// but each wraps them in its own orchestration (background workers vs a synchronous facade). If a
/// future change touches one orchestration and not the others, these tests fail rather than silently
/// letting the tools disagree on a count in CI vs the desktop app.
/// </summary>
public class CrossToolParityTests
{
    // A package that stores several standalone child classes inline (as MSL packages do). The trimmer
    // moves those children out of the package's stored source; each child then has its own node. Every
    // path must still check the same set of classes and produce the same findings.
    private const string Library = @"within;
package P ""P package""
  model A
    Real x;
  equation
    x = 1;
  end A;

  model B ""has a description""
    parameter Real p = 1;
  equation

  end B;

  package Sub ""sub package""
    model C
      Real y;
    equation
      y = 2;
    end C;
  end Sub;
end P;
";

    private static StyleCheckingSettings Settings() => new()
    {
        ClassHasDescription = true,
        ParameterHasDescription = true,
    };

    private static (DirectedGraph graph, List<ModelNode> models) LoadTrimmed()
    {
        var data = new LibraryDataService();
        var library = data.AddLibraryFromFileAsync("P.mo", Library).GetAwaiter().GetResult();
        var graph = data.CombinedGraph;

        // Same first step every checking path takes.
        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var models = library.ModelIds
            .Select(id => data.GetModelById(id))
            .Where(m => m is not null && !m.IsParseFailurePlaceholder)!
            .Cast<ModelNode>()
            .ToList();
        return (graph, models);
    }

    /// <summary>CLI/MCP path: the shared <see cref="LibraryCheckSession"/> facade.</summary>
    private static int FacadeCount()
    {
        var (graph, models) = LoadTrimmed();
        return LibraryCheckSession
            .Check(graph, models, Settings(), new CustomDictionaryService(), new DictionaryManagerService())
            .Count;
    }

    /// <summary>GUI path: drive the real background <see cref="StyleCheckingWorker"/> to completion.</summary>
    private static int WorkerCount()
    {
        var (graph, models) = LoadTrimmed();
        var worker = new StyleCheckingWorker(graph, Settings(), "test", spellChecker: null);

        var violations = new List<LogMessage>();
        var done = new ManualResetEventSlim(false);
        worker.OnViolationFound += (_, v) => { lock (violations) violations.AddRange(v); };
        worker.OnWorkCompleted += (_, _) => done.Set();

        foreach (var m in models)
            worker.AddToQueue(m.Id);
        worker.StartProcessing();

        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "worker did not complete in time");
        return violations.Count;
    }

    [Fact]
    public void Facade_And_Worker_ReportSameFindingCount()
    {
        var facade = FacadeCount();
        var worker = WorkerCount();

        Assert.True(facade > 0, "fixture should produce findings");
        Assert.Equal(facade, worker);
    }
}

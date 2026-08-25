using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Helpers;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

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

    // --- Whole-graph analyses -------------------------------------------------------------------
    //
    // The tests above compare the per-class rules only, which is how the GUI came to report fewer
    // findings than the CLI on the same library: the whole-graph analyzers ran on one GUI entry
    // point (whole-project checking) but not the other (Apply in repository settings / adding a
    // repository), and they were raced against dependency analysis rather than sequenced after it.
    // The fixture below turns on a graph rule that needs dependency edges, so every path has to run
    // dependency analysis before checking to report the finding at all.

    // P references Q.Thing but declares no uses(Q(...)) → one MLQT.Uses.Undeclared finding on P.
    // Model A also has no description → at least one per-class finding, so both families are covered.
    private const string LibraryP = @"within;
package P ""P package""
  model A
    Q.Thing t ""a thing"";
  end A;
end P;
";

    private const string LibraryQ = @"within;
package Q ""Q package""
  model Thing ""a thing""
    Real x ""a real"";
  end Thing;
end Q;
";

    private static StyleCheckingSettings GraphSettings() => new()
    {
        ClassHasDescription = true,
        CheckUsesUndeclared = true,
    };

    /// <summary>Loads both libraries into one repository, trimmed the way every checking path starts.</summary>
    private static (LibraryDataService data, Repository repo) LoadTwoLibraryRepo()
    {
        var data = new LibraryDataService();
        var repo = new Repository { Name = "ParityRepo", StyleSettings = GraphSettings() };
        foreach (var (file, code) in new[] { ("P.mo", LibraryP), ("Q.mo", LibraryQ) })
        {
            var library = data.AddLibraryFromFileAsync(file, code).GetAwaiter().GetResult();
            library.RepositoryId = repo.Id;
        }
        PackageCodeTrimmer.TrimStandaloneChildren(data.CombinedGraph);
        return (data, repo);
    }

    /// <summary>CLI/MCP path: dependency analysis, then the shared facade — as CheckPipeline does.</summary>
    private static List<Finding> FacadeFindings()
    {
        var (data, _) = LoadTwoLibraryRepo();
        var graph = data.CombinedGraph;
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();

        var models = data.Libraries
            .SelectMany(l => l.ModelIds)
            .Select(data.GetModelById)
            .Where(m => m is not null && !m.IsParseFailurePlaceholder)!
            .Cast<ModelNode>()
            .ToList();

        return LibraryCheckSession
            .Check(graph, models, GraphSettings(), new CustomDictionaryService(), new DictionaryManagerService())
            .ToList();
    }

    /// <summary>
    /// GUI path: the real service, driven through whichever entry point the caller picks. Neither is
    /// given a pre-analysed graph — each has to arrange dependency analysis for itself, which is what
    /// the Apply-settings path previously failed to do.
    /// </summary>
    private static List<LogMessage> ServiceFindings(bool wholeProjectEntryPoint)
    {
        var (data, repo) = LoadTwoLibraryRepo();
        var settingsService = new InMemorySettingsService();
        var service = new StyleCheckingService(
            data,
            new RepositoryService(data, settingsService, new FileMonitoringService()),
            settingsService,
            new CustomDictionaryService(),
            new DictionaryManagerService(),
            new CodeReviewService());

        var found = new List<LogMessage>();
        service.OnViolationsFound += v => { lock (found) found.AddRange(v); };

        if (wholeProjectEntryPoint)
            service.StartBackgroundCheckingForRepositories(new List<Repository> { repo });
        else
            service.StartBackgroundChecking(repo);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        Thread.Sleep(100);
        while (service.IsRunning && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
        Assert.False(service.IsRunning, "style checking did not complete in time");
        Thread.Sleep(600); // final flush

        lock (found)
            return found.ToList();
    }

    [Fact]
    public void GraphAnalyses_RunOnEverySurface_AndReportTheSameCount()
    {
        var facade = FacadeFindings();
        var wholeProject = ServiceFindings(wholeProjectEntryPoint: true);
        var singleRepository = ServiceFindings(wholeProjectEntryPoint: false);

        // The fixture must exercise both families, or the parity assertion proves nothing.
        Assert.Contains(facade, f => f.RuleId == ModelicaParser.StyleRules.RuleIds.UsesUndeclared);
        Assert.Contains(facade, f => f.RuleId == ModelicaParser.StyleRules.RuleIds.ClassDescription);

        Assert.Equal(facade.Count, wholeProject.Count);
        Assert.Equal(facade.Count, singleRepository.Count);
    }

    // --- Parse diagnostics ----------------------------------------------------------------------

    // A Documentation(info=...) annotation missing its closing quote. The parser recovers, so the
    // class still loads and every style rule reports on a tree that is missing part of the file.
    private const string UnterminatedString = """
        within;
        package P "P package"
          model A "a model"
          end A;
          annotation(Documentation(info="<html><p>docs</p>));
        end P;
        """;

    [Fact]
    public void ParseErrors_AreReportedByTheSharedFacade_WhateverTheSettings()
    {
        var data = new LibraryDataService();
        var library = data.AddLibraryFromFileAsync("P.mo", UnterminatedString).GetAwaiter().GetResult();
        var models = library.ModelIds.Select(data.GetModelById).Where(m => m is not null)!.Cast<ModelNode>().ToList();

        // No style rules at all — a check that returned nothing here would be reporting a clean bill
        // of health on code it could not read.
        var findings = LibraryCheckSession.Check(
            data.CombinedGraph, models, new StyleCheckingSettings(),
            new CustomDictionaryService(), new DictionaryManagerService());

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.True(RuleIds.IsParseDiagnostic(f.RuleId)));
        Assert.All(findings, f => Assert.Equal(RuleSeverity.Error, f.Severity));
        Assert.Contains(findings, f => f.Message.Contains("Unterminated string literal"));
    }

    [Fact]
    public void ParseErrors_ReadIdenticallyAsFindingsAndAsMessages()
    {
        // The GUI consumes LogMessages and the CLI/MCP consume Findings. Both come from the same
        // conversion, so the wording and line numbers cannot drift between the surfaces.
        var data = new LibraryDataService();
        var library = data.AddLibraryFromFileAsync("P.mo", UnterminatedString).GetAwaiter().GetResult();
        var models = library.ModelIds.Select(data.GetModelById).Where(m => m is not null)!.Cast<ModelNode>().ToList();

        var findings = ParserErrorReporter.ToFindings(models);
        var messages = ParserErrorReporter.ToLogMessages(models);

        Assert.Equal(findings.Count, messages.Count);
        Assert.Equal(
            findings.Select(f => (f.ModelId, f.LineNumber, f.Message)).OrderBy(x => x.LineNumber).ToList(),
            messages.Select(m => (m.ModelName, m.LineNumber, m.Details)).OrderBy(x => x.LineNumber).ToList());
    }

    [Fact]
    public void ParseErrors_SurviveTheLazyReparseThatStyleCheckingTriggers()
    {
        // EnsureParsed re-parses a class in isolation. It used to overwrite the load-time errors with
        // its own, replacing the readable diagnosis and the real file line numbers — so the message a
        // user saw depended on whether anything had checked the class yet.
        var data = new LibraryDataService();
        var library = data.AddLibraryFromFileAsync("P.mo", UnterminatedString).GetAwaiter().GetResult();
        var models = library.ModelIds.Select(data.GetModelById).Where(m => m is not null)!.Cast<ModelNode>().ToList();

        var beforeCheck = ParserErrorReporter.ToFindings(models)
            .Select(f => (f.LineNumber, f.Message)).OrderBy(x => x.LineNumber).ToList();

        foreach (var model in models)
            model.Definition.EnsureParsed();

        var afterCheck = ParserErrorReporter.ToFindings(models)
            .Select(f => (f.LineNumber, f.Message)).OrderBy(x => x.LineNumber).ToList();

        Assert.Equal(beforeCheck, afterCheck);
    }

    [Fact]
    public void SingleRepositoryCheck_RunsDependencyRequiringGraphAnalyzers()
    {
        // The reported bug in one assertion: Apply-in-settings checks a single repository, and used
        // to skip the graph analyzers entirely — so this finding was missing until the next restart.
        var findings = ServiceFindings(wholeProjectEntryPoint: false);

        Assert.Contains(findings, f => f.RuleId == ModelicaParser.StyleRules.RuleIds.UsesUndeclared);
    }

    [Fact]
    public void TheNamingRulesAreBuiltOncePerRun_AndTheFindingsAreUnchanged()
    {
        // The config is derived from the settings alone, so it is the same for every class in a run.
        // It is built with the other once-per-run inputs in StyleCheckContext; the per-class fallback
        // has to agree with it exactly, or which path a surface takes would decide what it reports.
        var settings = new StyleCheckingSettings { FollowNamingConvention = true };
        settings.NamingConvention.RecordNaming = ModelicaParser.StyleRules.NamingStyle.PascalCase;

        var data = new LibraryDataService();
        data.AddLibraryFromFileAsync("R.mo",
                "record lower_case_rec\n  Real x;\nend lower_case_rec;")
            .GetAwaiter().GetResult();
        var node = data.CombinedGraph.ModelNodes.Single();

        var context = StyleCheckContext.Build(settings, data.CombinedGraph, spellChecker: null);
        Assert.NotNull(context.NamingConfig);

        var viaContext = StyleCheckRunner.RunFindings(node, settings, context);
        node.Definition.ParsedCode = null;
        var viaFallback = StyleChecking.RunStyleCheckingFindings(node.Definition, settings, node.Id);

        Assert.Equal(
            viaFallback.Select(f => f.Fingerprint).OrderBy(f => f, StringComparer.Ordinal),
            viaContext.Select(f => f.Fingerprint).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Contains(viaContext, f => f.RuleId == ModelicaParser.StyleRules.RuleIds.NamingConvention);
    }
}

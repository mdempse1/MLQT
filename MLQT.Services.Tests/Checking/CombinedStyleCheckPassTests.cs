using System.Collections.Concurrent;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// <see cref="CombinedStyleCheckPass"/>, extracted from <c>MainLayout</c> in phase 7a-4.
///
/// <para>The style check that rides along with dependency analysis, so each class is checked while
/// its parse tree is still in hand. It is the piece of that method most worth moving: it had drifted
/// from the paths it must agree with twice, and until now the only way to run it was to open a
/// library in the application.</para>
/// </summary>
public class CombinedStyleCheckPassTests
{
    private const string BadlyNamed = "model badly_named Real x; end badly_named;";

    private static (DirectedGraph Graph, ModelNode Model) GraphWithOneClass(string id = "Lib.Thing")
    {
        var graph = new DirectedGraph();
        var model = new ModelNode(id, id.Split('.')[^1], BadlyNamed);
        graph.AddNode(model);
        return (graph, model);
    }

    private static StyleCheckingSettings WithRule(string ruleId, RuleSeverity severity = RuleSeverity.Warning)
    {
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities[ruleId] = severity;
        return settings;
    }

    private static Repository Repo(string id, StyleCheckingSettings? settings) =>
        new() { Id = id, Name = id, StyleSettings = settings };

    [Fact]
    public void AClassIsCheckedAgainstItsRepositorysRules()
    {
        var (graph, model) = GraphWithOneClass();
        var settings = WithRule(RuleIds.NamingConvention);
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", settings)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = settings },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => null, findings);

        pass(model);

        Assert.NotEmpty(findings);
    }

    [Fact]
    public void AClassWithNoRulesEnabled_ProducesNoFindings()
    {
        var (graph, model) = GraphWithOneClass();
        var none = new StyleCheckingSettings();
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", none)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = none },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => null, findings);

        pass(model);

        Assert.Empty(findings);
    }

    [Fact]
    public void AClassNotInTheSettingsMap_IsSkipped()
    {
        // A class loaded after the map was built. Skipping is right; throwing would abandon the pass
        // partway through a library.
        var (graph, model) = GraphWithOneClass();
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [], new Dictionary<string, StyleCheckingSettings>(),
            new Dictionary<string, string>(), _ => null, findings);

        pass(model);

        Assert.Empty(findings);
    }

    [Fact]
    public void AClassWithNoRepository_IsCheckedAgainstTheDefaults()
    {
        // A library the user opened directly rather than through a repository. It still gets checked
        // — against the tool's defaults, and with no accepted spellings, because there is no
        // repository to have accepted any.
        var (graph, model) = GraphWithOneClass();
        var settings = WithRule(RuleIds.NamingConvention);
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = settings },
            new Dictionary<string, string>(),   // no repository for this class
            _ => null, findings);

        pass(model);

        Assert.NotEmpty(findings);
    }

    [Fact]
    public void AClassWhoseRepositoryHasNoContext_FallsBackToTheDefaultContext()
    {
        // The repository is named in the map but has no settings, so no context was built for it.
        // Falling back keeps the class checked rather than dropping it.
        var (graph, model) = GraphWithOneClass();
        var settings = WithRule(RuleIds.NamingConvention);
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", settings: null)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = settings },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => null, findings);

        pass(model);

        Assert.NotEmpty(findings);
    }

    [Fact]
    public void TwoRepositoriesAreCheckedAgainstTheirOwnRules()
    {
        var graph = new DirectedGraph();
        var strictModel = new ModelNode("A.thing", "thing", "model thing Real x; end thing;");
        var lenientModel = new ModelNode("B.thing", "thing", "model thing Real x; end thing;");
        graph.AddNode(strictModel);
        graph.AddNode(lenientModel);

        var strict = WithRule(RuleIds.NamingConvention);
        var lenient = new StyleCheckingSettings();
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("strict", strict), Repo("lenient", lenient)],
            new Dictionary<string, StyleCheckingSettings>
            {
                [strictModel.Id] = strict,
                [lenientModel.Id] = lenient,
            },
            new Dictionary<string, string>
            {
                [strictModel.Id] = "strict",
                [lenientModel.Id] = "lenient",
            },
            _ => null, findings);

        pass(lenientModel);
        Assert.Empty(findings);   // no rules enabled for its repository

        pass(strictModel);
        Assert.NotEmpty(findings);
    }

    [Fact]
    public void TheSpellCheckerIsAskedForOncePerRepository_NotPerClass()
    {
        // Building one reads that repository's accepted-spellings file. Per class, over a library of
        // tens of thousands, that is the file read tens of thousands of times.
        var (graph, model) = GraphWithOneClass();
        var settings = WithRule(RuleIds.NamingConvention);
        var asked = 0;

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", settings)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = settings },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => { asked++; return null; },
            new ConcurrentBag<LogMessage>());

        pass(model);
        pass(model);
        pass(model);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void AClassWithNoRulesEnabled_IsStillMeasuredForCoverage()
    {
        // Coverage reports the state of the code, not the result of the rules. A library with every
        // rule switched off still has a documented-class percentage, and dropping these classes
        // would report it against a smaller denominator than the library actually has.
        var (graph, model) = GraphWithOneClass();
        var none = new StyleCheckingSettings();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", none)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = none },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => null, new ConcurrentBag<LogMessage>());

        pass(model);

        Assert.Equal(1, contexts["repo-1"].Coverage!.MeasuredHere);
    }

    [Fact]
    public void EachRepositoryGetsItsOwnContext()
    {
        // Not a detail: a context carries that repository's spell checker and its accepted
        // spellings. Sharing one across repositories would check a class against another team's
        // dictionary.
        var (graph, _) = GraphWithOneClass();

        var (_, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("a", new StyleCheckingSettings()), Repo("b", new StyleCheckingSettings())],
            new Dictionary<string, StyleCheckingSettings>(),
            new Dictionary<string, string>(),
            _ => null, new ConcurrentBag<LogMessage>());

        Assert.True(contexts.ContainsKey("a"));
        Assert.True(contexts.ContainsKey("b"));
        Assert.NotSame(contexts["a"], contexts["b"]);
        Assert.NotSame(contexts["a"], contexts[""]);
    }

    [Fact]
    public void FindingsGoIntoTheCallersBag()
    {
        // The caller owns the bag because dependency analysis runs this in parallel and collects the
        // result afterwards.
        var (graph, model) = GraphWithOneClass();
        var settings = WithRule(RuleIds.NamingConvention);
        var findings = new ConcurrentBag<LogMessage>();

        var (pass, contexts) = CombinedStyleCheckPass.Build(
            graph, [Repo("repo-1", settings)],
            new Dictionary<string, StyleCheckingSettings> { [model.Id] = settings },
            new Dictionary<string, string> { [model.Id] = "repo-1" },
            _ => null, findings);

        pass(model);

        // The bag being filled is the assertion: the caller passed this instance in, and dependency
        // analysis collects from it after running the pass across every class in parallel.
        Assert.NotEmpty(findings);
    }
}

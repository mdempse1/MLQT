using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// B177 — a package stored as one <c>.mo</c> file rather than as a directory of standalone classes.
///
/// <para>The judgement being tested is "could this be split", not "is this one file". A package
/// whose children all have to be inline — <c>replaceable</c>, <c>redeclare</c>, <c>inner</c>,
/// <c>outer</c> — is correctly a single file, and reporting it would be advice nobody can act on.
/// </para>
/// </summary>
public class SingleFilePackageAnalyzerTests
{
    /// <summary>
    /// A package and its children. <paramref name="childFileIds"/> gives each child's file: the same
    /// id as the package means it is held inside <c>package.mo</c>, a different one means it has a
    /// file of its own.
    /// </summary>
    private static DirectedGraph Build(
        (string Name, string? FileId, bool Standalone)[] children, string packageFileId = "P.mo")
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;")
        {
            ClassType = "package",
            ContainingFileId = packageFileId,
            StartLine = 1
        });

        foreach (var (name, fileId, standalone) in children)
            graph.AddNode(new ModelNode("P." + name, name, $"model {name} end {name};")
            {
                ClassType = "model",
                ParentModelName = "P",
                ContainingFileId = fileId,
                CanBeStoredStandalone = standalone
            });

        return graph;
    }

    private static List<Finding> Analyze(DirectedGraph graph)
    {
        var settings = new StyleCheckingSettings { CheckSingleFilePackage = true };
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.SingleFilePackage).ToList();
    }

    [Fact]
    public void APackageHoldingItsClassesInline_IsReported()
    {
        var findings = Analyze(Build([("A", "P.mo", true), ("B", "P.mo", true)]));

        var finding = Assert.Single(findings);
        Assert.Equal("P", finding.ModelId);
        Assert.Contains("2 classes", finding.Message);
    }

    [Fact]
    public void APackageWhoseClassesHaveTheirOwnFiles_IsAccepted()
    {
        Assert.Empty(Analyze(Build([("A", "A.mo", true), ("B", "B.mo", true)])));
    }

    [Fact]
    public void APackageThatIsPartlySplit_IsNotReported()
    {
        // "Entirely in one file" is the claim. A package someone has begun splitting is a different
        // situation, and reporting it would be telling them something they are evidently doing.
        Assert.Empty(Analyze(Build([("A", "A.mo", true), ("B", "P.mo", true)])));
    }

    [Fact]
    public void APackageWhoseClassesCannotBeStandalone_IsAccepted()
    {
        // The case that makes this a judgement rather than a file count: these have to be inline, so
        // the package is correctly one file and there is no advice to give.
        Assert.Empty(Analyze(Build([("A", "P.mo", false), ("B", "P.mo", false)])));
    }

    [Fact]
    public void OnlyTheSplittableChildrenAreCounted()
    {
        // One of each: the message must describe the one class that could move, not both children.
        var findings = Analyze(Build([("A", "P.mo", true), ("B", "P.mo", false)]));

        var finding = Assert.Single(findings);
        Assert.Contains("'A'", finding.Message);
        Assert.DoesNotContain("'B'", finding.Message);
    }

    [Fact]
    public void AnEmptyPackage_IsAccepted()
    {
        // Nothing to split, so nothing to say.
        Assert.Empty(Analyze(Build([])));
    }

    [Fact]
    public void AChildWithNoFileId_CountsAsInline()
    {
        // It came from the package's own source rather than from a file of its own, which is the
        // same thing this rule is about.
        var findings = Analyze(Build([("A", null, true)]));

        Assert.Single(findings);
    }

    [Fact]
    public void ANonPackageClass_IsNeverReported()
    {
        // A model holding nested classes is not what this rule is about — a model is one file by
        // definition, and the one-class-per-file convention is about packages.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("M", "M", "model M end M;") { ClassType = "model", ContainingFileId = "M.mo" });
        graph.AddNode(new ModelNode("M.Inner", "Inner", "model Inner end Inner;")
        {
            ClassType = "model",
            ParentModelName = "M",
            ContainingFileId = "M.mo"
        });

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void TheRuleIsOnUnlessItIsSwitchedOff()
    {
        // The one rule that does not wait to be discovered. A library drifts away from the
        // one-class-per-file layout without anyone doing anything — another tool saves a new package
        // as a single file, and the incremental formatter, which never moves a class between files,
        // reformats it in place and leaves it that way. A user who has not heard of this rule is
        // exactly the user who needs it.
        var graph = Build([("A", "P.mo", true)]);
        var ctx = new GraphAnalysisContext(graph, new StyleCheckingSettings(), graph.ModelNodes.ToList());

        Assert.Single(GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.SingleFilePackage));
    }

    [Fact]
    public void ARepositoryCanStillSwitchItOff()
    {
        // Single-file storage is a repository's choice in the end — some libraries are deliberately
        // kept that way. The default decides who has to know the rule exists, not who decides.
        var graph = Build([("A", "P.mo", true)]);
        var settings = new StyleCheckingSettings();
        settings.SetRuleEnabled(RuleIds.SingleFilePackage, false);
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());

        Assert.Empty(GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.SingleFilePackage));
    }
}

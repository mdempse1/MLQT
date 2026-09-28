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
        (string Name, string? FileId, bool Standalone)[] children, string packageFileId = "P.mo",
        string[]? asPackages = null)
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
                // A package child is written as a directory, a model child as Name.mo — which is
                // the difference that decides whether two names colliding on case really collide.
                ClassType = asPackages?.Contains(name) == true ? "package" : "model",
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
    public void APackageThatIsPartlySplit_IsStillReported()
    {
        // The first reading of this rule was "entirely in one file, or say nothing", on the grounds
        // that a package someone had begun splitting was a different situation. A real library shows
        // how wrong that is (B243): MSL's Spice3.Internal has thirteen classes in their own files
        // and eight more still written into package.mo. Half-way through is the ordinary shape of
        // this drift, and silence is how a class stays put for years.
        var findings = Analyze(Build([("A", "A.mo", true), ("B", "P.mo", true)]));

        var finding = Assert.Single(findings);
        Assert.Contains("'B'", finding.Message);
        Assert.DoesNotContain("'A'", finding.Message);
    }

    [Fact]
    public void TwoModelsWhoseNamesDifferOnlyInCase_AreNotReported()
    {
        // Both would be written as Jfet.mo, so neither can be given a file and reporting them would
        // be a finding whose fix moves nothing.
        var findings = Analyze(Build([("Jfet", "P.mo", true), ("JFET", "P.mo", true)]));

        Assert.Empty(findings);
    }

    [Fact]
    public void AModelAndAPackageWhoseNamesDifferOnlyInCase_AreReported()
    {
        // Why MSL's Spice3.Internal.JFET was still in package.mo, which took a while to work out.
        // It has a sibling *package* called Jfet, and the saver refused to write two children whose
        // names differ only in case — though one becomes the directory Jfet and the other the file
        // JFET.mo, which cannot collide. Four pairs in MSL alone (B245).
        var findings = Analyze(
            Build([("Jfet", "P.mo", true), ("JFET", "P.mo", true)], asPackages: ["Jfet"]));

        var finding = Assert.Single(findings);
        Assert.Contains("2 classes", finding.Message);
    }

    [Fact]
    public void TwoPackagesWhoseNamesDifferOnlyInCase_AreNotReported()
    {
        // ...and two packages both want the directory Jfet, so they are back to colliding.
        var findings = Analyze(
            Build([("Jfet", "P.mo", true), ("JFET", "P.mo", true)], asPackages: ["Jfet", "JFET"]));

        Assert.Empty(findings);
    }

    [Fact]
    public void APackageCalledPackage_IsReported()
    {
        // The reserved-entry question is about the entry, not the name: package.mo is taken, but a
        // package called Package becomes the directory Package and collides with nothing.
        var findings = Analyze(Build([("Package", "P.mo", true)], asPackages: ["Package"]));

        Assert.Single(findings);
    }

    [Fact]
    public void AModelCalledPackage_IsNotReported()
    {
        // It would be written as package.mo, which is the file its parent already occupies.
        Assert.Empty(Analyze(Build([("package", "P.mo", true)])));
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

        Assert.Single(GraphAnalysisRunner.Run(ctx), f => f.RuleId == RuleIds.SingleFilePackage);
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

        Assert.DoesNotContain(GraphAnalysisRunner.Run(ctx), f => f.RuleId == RuleIds.SingleFilePackage);
    }
}

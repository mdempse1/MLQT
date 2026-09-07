using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

public class UnusedClassAnalyzerTests
{
    // Package P with a public model A and two protected models (Helper, Used); A references Used.
    private const string ParentCode =
        "package P\n  model A end A;\nprotected\n  model Helper end Helper;\n  model Used end Used;\nend P;";

    // IsPublic mirrors what the loader records from ParentCode above: A is public, Helper and Used
    // sit after the `protected` keyword. The analyzer reads the flag rather than re-parsing the
    // parent's source, because that source gets trimmed of its standalone children.
    private static DirectedGraph Build(bool partialHelper = false)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", ParentCode) { ClassType = "package" });
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", IsPublic = true });
        graph.AddNode(new ModelNode("P.Helper", "Helper", "model Helper end Helper;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", IsPartial = partialHelper, IsPublic = false });
        graph.AddNode(new ModelNode("P.Used", "Used", "model Used end Used;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", IsPublic = false });
        graph.AddModelUsesModel("P.A", "P.Used");   // Used is referenced → not dead
        return graph;
    }

    private static List<Finding> Run(DirectedGraph graph, bool depAnalyzed = true)
        => Run(graph, new StyleCheckingSettings { CheckUnusedClass = true }, depAnalyzed);

    private static List<Finding> Run(DirectedGraph graph, StyleCheckingSettings settings, bool depAnalyzed = true)
    {
        var ctx = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), depAnalyzed);
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedClassAnalyzer() });
    }

    [Fact]
    public void UnreferencedProtectedClass_IsFlagged()
    {
        var f = Assert.Single(Run(Build()), x => x.RuleId == RuleIds.UnusedClass);
        Assert.Equal("P.Helper", f.ModelId);
    }

    [Fact]
    public void ReferencedProtectedClass_IsNotFlagged()
    {
        Assert.DoesNotContain(Run(Build()), x => x.ModelId == "P.Used");
    }

    [Fact]
    public void PublicClass_IsNotFlagged_EvenIfUnreferenced()
    {
        // A is public and has no usedBy, but public classes may be used by invisible downstream libraries.
        Assert.DoesNotContain(Run(Build()), x => x.ModelId == "P.A");
    }

    [Fact]
    public void UnreferencedPublicClass_IsFlagged_WhenPublicRuleEnabled()
    {
        // A is public with no usedBy; the opt-in public rule flags it at Info.
        var settings = new StyleCheckingSettings { CheckUnusedPublicClass = true };
        var f = Assert.Single(Run(Build(), settings), x => x.RuleId == RuleIds.UnusedPublicClass);
        Assert.Equal("P.A", f.ModelId);
        Assert.Equal(RuleSeverity.Info, f.Severity);
    }

    [Fact]
    public void PublicRuleEnabled_DoesNotFlagProtectedClass()
    {
        // With only the public rule on, the protected Helper must not surface under either id.
        var settings = new StyleCheckingSettings { CheckUnusedPublicClass = true };
        Assert.DoesNotContain(Run(Build(), settings), x => x.ModelId == "P.Helper");
    }

    [Fact]
    public void PartialProtectedClass_IsNotFlagged()
    {
        // A partial class is meant to be extended, not instantiated — never flagged as unused.
        Assert.DoesNotContain(Run(Build(partialHelper: true)), x => x.ModelId == "P.Helper");
    }

    [Fact]
    public void SkippedEntirely_WhenDependenciesNotAnalysed()
    {
        // Without edges every class looks unreferenced — the analyzer must not run.
        Assert.Empty(Run(Build(), depAnalyzed: false));
    }

    // --- simulation entry points ----------------------------------------------------------------

    [Fact]
    public void ExperimentAnnotatedClass_IsNotFlagged_InEitherTier()
    {
        // A class with experiment(...) is meant to be simulated, not instantiated by something else,
        // so nothing referencing it is the expected state. Without this a library's whole example or
        // test package reports as dead code.
        var graph = Build();
        graph.GetNode<ModelNode>("P.A")!.HasExperimentAnnotation = true;        // public tier
        graph.GetNode<ModelNode>("P.Helper")!.HasExperimentAnnotation = true;   // protected tier

        var settings = new StyleCheckingSettings { CheckUnusedClass = true, CheckUnusedPublicClass = true };

        Assert.Empty(Run(graph, settings));
    }

    [Fact]
    public void WithoutTheAnnotation_TheSameClassesAreStillFlagged()
    {
        // Guards the premise: the exemption above is doing the work, not an empty fixture.
        var settings = new StyleCheckingSettings { CheckUnusedClass = true, CheckUnusedPublicClass = true };

        Assert.NotEmpty(Run(Build(), settings));
    }


    [Fact]
    public void ExternalObjectConstructorAndDestructor_AreNotFlagged()
    {
        // Modelica reserves these two names inside an ExternalObject and calls them implicitly when the
        // object is created and destroyed, so nothing anywhere references them by name. Reporting them
        // is never right — it fires on every library that wraps native code.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P end P;") { ClassType = "package" });
        graph.AddNode(new ModelNode("P.Ext", "Ext", "class Ext extends ExternalObject; end Ext;")
        { ClassType = "class", IsNested = true, ParentModelName = "P" });
        graph.AddNode(new ModelNode("P.Ext.constructor", "constructor", "function constructor end constructor;")
        { ClassType = "function", IsNested = true, ParentModelName = "P.Ext" });
        graph.AddNode(new ModelNode("P.Ext.destructor", "destructor", "function destructor end destructor;")
        { ClassType = "function", IsNested = true, ParentModelName = "P.Ext" });

        var settings = new StyleCheckingSettings { CheckUnusedClass = true, CheckUnusedPublicClass = true };
        var findings = Run(graph, settings);

        Assert.DoesNotContain(findings, f => f.ModelId.EndsWith("constructor"));
        Assert.DoesNotContain(findings, f => f.ModelId.EndsWith("destructor"));
        // The ExternalObject wrapper itself is still eligible, so the fixture isn't vacuous.
        Assert.Contains(findings, f => f.ModelId == "P.Ext");
    }

    [Fact]
    public void AnOrdinaryUnusedFunction_IsStillFlagged()
    {
        // The exemption is by name inside the ExternalObject protocol, not "functions are exempt".
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P end P;") { ClassType = "package" });
        graph.AddNode(new ModelNode("P.helper", "helper", "function helper end helper;")
        { ClassType = "function", IsNested = true, ParentModelName = "P" });

        var settings = new StyleCheckingSettings { CheckUnusedPublicClass = true };

        Assert.Contains(Run(graph, settings), f => f.ModelId == "P.helper");
    }

}

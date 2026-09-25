using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// A name resolves through the imports of every enclosing class, and through every form of import
/// (B292).
/// </summary>
/// <remarks>
/// <para>Reported as MSL's <c>Modelica.Blocks.Continuous.LimPID</c> offering links to some of the
/// classes it uses and not others. MSL declares <c>import Modelica.Units.SI;</c> once, in
/// <c>Modelica.Blocks</c>, and LimPID writes <c>SI.Time</c>; both resolvers asked only the class that
/// wrote a name for its imports, so that name meant nothing. Dependency analysis's copy of the lookup
/// had also stopped reading a plain <c>import A.B.C;</c> at all.</para>
/// </remarks>
public class EnclosingImportTests
{
    /// <summary>MSL's shape: the import on the grandparent package, the use two levels below it.</summary>
    private static DirectedGraph Msl(string limPidBody)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Modelica", "Modelica", "package Modelica\nend Modelica;"));
        graph.AddNode(new ModelNode("Modelica.Units", "Units", "package Units\nend Units;"));
        graph.AddNode(new ModelNode("Modelica.Units.SI", "SI", "package SI\nend SI;"));
        graph.AddNode(new ModelNode("Modelica.Units.SI.Time", "Time", "type Time = Real;"));
        graph.AddNode(new ModelNode("Modelica.Blocks", "Blocks",
            "package Blocks\n  import Modelica.Units.SI;\nend Blocks;"));
        graph.AddNode(new ModelNode("Modelica.Blocks.Continuous", "Continuous",
            "package Continuous\nend Continuous;"));
        graph.AddNode(new ModelNode("Modelica.Blocks.Continuous.LimPID", "LimPID", limPidBody));
        return graph;
    }

    private static HashSet<string> Dependencies(DirectedGraph graph, string modelId)
    {
        var analyzer = new ModelAnalyzer(modelId, graph);
        analyzer.Visit(ModelicaParserHelper.Parse(graph.GetNode<ModelNode>(modelId)!.Definition.ModelicaCode));
        return analyzer.ReferencedModels;
    }

    // ── the reported case, through both resolvers ──

    [Fact]
    public void DependencyAnalysis_SeesAnImportDeclaredOnAnEnclosingPackage()
    {
        var graph = Msl("block LimPID\n  parameter SI.Time Ti = 0.5;\nend LimPID;");

        Assert.Contains("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    [Fact]
    public void TypeResolver_SeesAnImportDeclaredOnAnEnclosingPackage()
    {
        var graph = Msl("block LimPID\nend LimPID;");

        Assert.Equal("Modelica.Units.SI.Time",
            TypeResolver.Resolve(graph, "Modelica.Blocks.Continuous.LimPID", "SI.Time")?.Id);
    }

    [Fact]
    public void TheNearestEnclosingImportWins()
    {
        // Continuous declares its own SI, so the one on Blocks is shadowed: lookup stops at the first
        // scope that knows the name.
        var graph = Msl("block LimPID\n  parameter SI.Time Ti = 0.5;\nend LimPID;");
        graph.AddNode(new ModelNode("Other", "Other", "package Other\nend Other;"));
        graph.AddNode(new ModelNode("Other.Time", "Time", "type Time = Real;"));
        graph.GetNode<ModelNode>("Modelica.Blocks.Continuous")!.Definition.ModelicaCode =
            "package Continuous\n  import SI = Other;\nend Continuous;";

        var dependencies = Dependencies(graph, "Modelica.Blocks.Continuous.LimPID");

        Assert.Contains("Other.Time", dependencies);
        Assert.DoesNotContain("Modelica.Units.SI.Time", dependencies);
    }

    [Fact]
    public void WithoutTheImport_TheNameStillResolvesToNothing()
    {
        // The control: it is the import on Blocks doing the work, not the lookup giving up on scope.
        var graph = Msl("block LimPID\n  parameter SI.Time Ti = 0.5;\nend LimPID;");
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode = "package Blocks\nend Blocks;";

        Assert.DoesNotContain("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    // ── the import forms dependency analysis had stopped reading ──

    [Fact]
    public void APlainImportInTheClass_MakesItsLastSegmentVisible()
    {
        var graph = Msl("block LimPID\n  import Modelica.Units.SI;\n  parameter SI.Time Ti = 0.5;\nend LimPID;");
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode = "package Blocks\nend Blocks;";

        Assert.Contains("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    [Fact]
    public void AListImport_MakesEachListedNameVisible()
    {
        var graph = Msl("block LimPID\n  import Modelica.Units.SI.{Time};\n  parameter Time Ti = 0.5;\nend LimPID;");
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode = "package Blocks\nend Blocks;";

        Assert.Contains("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    // ── a list import makes the listed names visible and no others (B348) ──

    /// <summary><see cref="Msl"/> with <c>Current</c> beside <c>Time</c> in SI, and Blocks importing
    /// only <c>Time</c> from it.</summary>
    private static DirectedGraph MslWithListImport(string limPidBody)
    {
        var graph = Msl(limPidBody);
        graph.AddNode(new ModelNode("Modelica.Units.SI.Current", "Current", "type Current = Real;"));
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode =
            "package Blocks\n  import Modelica.Units.SI.{Time};\nend Blocks;";
        return graph;
    }

    [Fact]
    public void AListImport_ResolvesTheListedName_ThroughBothResolvers()
    {
        // The positive control for the two below.
        var graph = MslWithListImport("block LimPID\n  parameter Time Ti = 0.5;\nend LimPID;");

        Assert.Equal("Modelica.Units.SI.Time",
            TypeResolver.Resolve(graph, "Modelica.Blocks.Continuous.LimPID", "Time")?.Id);
        Assert.Contains("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    [Fact]
    public void AListImport_DoesNotMakeAnUnlistedNameVisible()
    {
        // Read as a wildcard, `import SI.{Time};` made SI.Current visible as Current.
        var graph = MslWithListImport("block LimPID\n  parameter Current i = 0.5;\nend LimPID;");

        Assert.Null(TypeResolver.Resolve(graph, "Modelica.Blocks.Continuous.LimPID", "Current"));
        Assert.DoesNotContain("Modelica.Units.SI.Current", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    [Fact]
    public void AnEnclosingListImport_DoesNotCaptureANameThatBelongsFurtherOut()
    {
        // Modelica.Current is what Current means in LimPID; Blocks' list import names only Time.
        var graph = MslWithListImport("block LimPID\n  parameter Current i = 0.5;\nend LimPID;");
        graph.AddNode(new ModelNode("Modelica.Current", "Current", "type Current = Real;"));

        Assert.Equal("Modelica.Current",
            TypeResolver.Resolve(graph, "Modelica.Blocks.Continuous.LimPID", "Current")?.Id);
        var dependencies = Dependencies(graph, "Modelica.Blocks.Continuous.LimPID");
        Assert.Contains("Modelica.Current", dependencies);
        Assert.DoesNotContain("Modelica.Units.SI.Current", dependencies);
    }

    [Fact]
    public void AListImport_ReachesBelowAListedName()
    {
        // `import Modelica.Units.{SI};` makes SI visible, and SI.Time with it.
        var graph = Msl("block LimPID\nend LimPID;");
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode =
            "package Blocks\n  import Modelica.Units.{SI};\nend Blocks;";

        Assert.Equal("Modelica.Units.SI.Time",
            TypeResolver.Resolve(graph, "Modelica.Blocks.Continuous.LimPID", "SI.Time")?.Id);
    }

    // ── dependency analysis reads the class's own imports, and only its own (B348) ──

    [Fact]
    public void AnImportWrittenBelowTheReference_StillApplies()
    {
        // An import is in scope for the whole class. Collected as the visit reached it, SI.Time was
        // resolved before the class's own `import SI = Other;` was seen, through Blocks' SI instead.
        var graph = Msl("block LimPID\n  parameter SI.Time Ti = 0.5;\n  import SI = Other;\nend LimPID;");
        graph.AddNode(new ModelNode("Other", "Other", "package Other\nend Other;"));
        graph.AddNode(new ModelNode("Other.Time", "Time", "type Time = Real;"));

        var dependencies = Dependencies(graph, "Modelica.Blocks.Continuous.LimPID");

        Assert.Contains("Other.Time", dependencies);
        Assert.DoesNotContain("Modelica.Units.SI.Time", dependencies);
    }

    [Fact]
    public void ANestedClassesImport_DoesNotLeakIntoTheEnclosingClass()
    {
        var graph = Msl(
            "block LimPID\n  model Inner\n    import SI = Other;\n  end Inner;\n  parameter SI.Time Ti = 0.5;\nend LimPID;");
        graph.AddNode(new ModelNode("Other", "Other", "package Other\nend Other;"));
        graph.AddNode(new ModelNode("Other.Time", "Time", "type Time = Real;"));

        var dependencies = Dependencies(graph, "Modelica.Blocks.Continuous.LimPID");

        Assert.Contains("Modelica.Units.SI.Time", dependencies);
        Assert.DoesNotContain("Other.Time", dependencies);
    }

    [Fact]
    public void AnAlias_IsMatchedAsAWholeSegment()
    {
        // The old lookup matched an alias by prefix and replaced it wherever it occurred, so `SIx`
        // was read as the alias `SI` followed by `x`.
        var graph = Msl("block LimPID\n  import SI = Modelica.Units.SI;\n  parameter SIx.Time Ti = 0.5;\nend LimPID;");
        graph.AddNode(new ModelNode("Modelica.Units.SIx", "SIx", "package SIx\nend SIx;"));
        graph.AddNode(new ModelNode("Modelica.Units.SIx.Time", "Time", "type Time = Real;"));

        Assert.DoesNotContain("Modelica.Units.SIx.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    [Fact]
    public void ALeadingDot_IsAFullyQualifiedName()
    {
        // MSL writes `.Modelica.Blocks.Types.SimpleController` in LimPID itself.
        var graph = Msl("block LimPID\n  parameter .Modelica.Units.SI.Time Ti = 0.5;\nend LimPID;");
        graph.GetNode<ModelNode>("Modelica.Blocks")!.Definition.ModelicaCode = "package Blocks\nend Blocks;";

        Assert.Contains("Modelica.Units.SI.Time", Dependencies(graph, "Modelica.Blocks.Continuous.LimPID"));
    }

    // ── what a class declares, read once ──

    [Fact]
    public void ClassImports_ReadsEveryFormOfTheClassesOwnImports_AndNotItsChildren()
    {
        var definition = new ModelDefinition("P",
            "package P\n  import A = X.Y;\n  import X.Z;\n  import X.W.*;\n  import X.V.{a, b};\n" +
            "  model Inner\n    import Nope.N;\n  end Inner;\nend P;");

        Assert.Equal(["A = X.Y", "X.Z", "X.W.*", "X.V.{a, b}"], ClassImports.For(definition));
    }

    [Fact]
    public void ClassImports_IsReadAgainWhenTheSourceChanges()
    {
        var definition = new ModelDefinition("P", "package P\n  import X.Z;\nend P;");
        Assert.Equal(["X.Z"], ClassImports.For(definition));

        definition.ModelicaCode = "package P\n  import X.Q;\nend P;";

        Assert.Equal(["X.Q"], ClassImports.For(definition));
    }

    [Fact]
    public void ClassImports_OfAClassThatWillNotParse_IsEmpty()
    {
        Assert.Empty(ClassImports.For(new ModelDefinition("P", "package P import ;;; end")));
    }
}

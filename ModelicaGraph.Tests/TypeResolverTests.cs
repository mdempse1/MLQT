using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Resolving a type name as Modelica would: fully qualified, through the class's imports in each of
/// the forms the language allows, or by walking outward through the enclosing packages.
///
/// <para>This decides whether a component counts towards unit coverage and whether a reference is
/// reported as broken, so a name it fails to resolve becomes a finding against code that is perfectly
/// correct. The import forms are where that is easiest to get wrong: an alias, a wildcard and an
/// explicit list all look different and mean nearly the same thing.</para>
/// </summary>
public class TypeResolverTests
{
    private static DirectedGraph GraphWith(params string[] classIds)
    {
        var graph = new DirectedGraph();
        foreach (var id in classIds)
        {
            var name = id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;
            graph.AddNode(new ModelNode(id, name, $"model {name}\nend {name};"));
        }
        return graph;
    }

    // ── what needs no resolving ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingToResolve_IsNull(string? typeText)
    {
        Assert.Null(TypeResolver.Resolve(GraphWith("Lib.M"), "Lib.M", typeText));
    }

    [Theory]
    [InlineData("Real")]
    [InlineData("Integer")]
    [InlineData("Boolean")]
    [InlineData("String")]
    public void APredefinedType_IsNotAClassInTheGraph(string typeText)
    {
        // Not "unresolved": there is nothing to resolve. A caller that treated null as a broken
        // reference would report every Real in the library.
        Assert.True(TypeResolver.IsPredefined(typeText));
        Assert.Null(TypeResolver.Resolve(GraphWith("Lib.M"), "Lib.M", typeText));
    }

    [Fact]
    public void APredefinedTypeWrittenGlobally_IsStillPredefined()
    {
        Assert.True(TypeResolver.IsPredefined(".Real"));
    }

    [Fact]
    public void AnUnknownName_IsNull()
    {
        Assert.Null(TypeResolver.Resolve(GraphWith("Lib.M"), "Lib.M", "NoSuchType"));
    }

    // ── the three ways a name resolves without imports ──

    [Fact]
    public void AFullyQualifiedName_ResolvesDirectly()
    {
        var graph = GraphWith("Lib.Sub.Target", "Lib.M");

        Assert.Equal("Lib.Sub.Target", TypeResolver.Resolve(graph, "Lib.M", "Lib.Sub.Target")!.Id);
    }

    [Fact]
    public void ALeadingDot_MeansTheSameFullyQualifiedName()
    {
        var graph = GraphWith("Lib.Sub.Target", "Lib.M");

        Assert.Equal("Lib.Sub.Target", TypeResolver.Resolve(graph, "Lib.M", ".Lib.Sub.Target")!.Id);
    }

    [Fact]
    public void ANameInAnEnclosingPackage_ResolvesByWalkingOutward()
    {
        // Lib.Deep.M refers to Target, which lives in Lib: the lookup starts in the class's own scope
        // and drops a segment at a time until it finds one.
        var graph = GraphWith("Lib.Target", "Lib.Deep.M");

        Assert.Equal("Lib.Target", TypeResolver.Resolve(graph, "Lib.Deep.M", "Target")!.Id);
    }

    [Fact]
    public void ANearerScope_WinsOverAnOuterOne()
    {
        var graph = GraphWith("Lib.Target", "Lib.Deep.Target", "Lib.Deep.M");

        Assert.Equal("Lib.Deep.Target", TypeResolver.Resolve(graph, "Lib.Deep.M", "Target")!.Id);
    }

    // ── imports, in each form Modelica allows ──

    [Fact]
    public void AnAliasImport_ResolvesTheAlias()
    {
        var graph = GraphWith("Modelica.Units.SI", "Lib.M");

        Assert.Equal("Modelica.Units.SI",
            TypeResolver.Resolve(graph, "Lib.M", "SI", ["SI = Modelica.Units.SI"])!.Id);
    }

    [Fact]
    public void AnAliasImport_ResolvesANameBeneathIt()
    {
        var graph = GraphWith("Modelica.Units.SI.Voltage", "Lib.M");

        Assert.Equal("Modelica.Units.SI.Voltage",
            TypeResolver.Resolve(graph, "Lib.M", "SI.Voltage", ["SI = Modelica.Units.SI"])!.Id);
    }

    [Fact]
    public void AnAliasImport_DoesNotAnswerForAnotherName()
    {
        var graph = GraphWith("Modelica.Units.SI.Voltage", "Lib.M");

        Assert.Null(TypeResolver.Resolve(graph, "Lib.M", "Voltage", ["SI = Modelica.Units.SI"]));
    }

    [Fact]
    public void AWildcardImport_ResolvesAnyNameBeneathIt()
    {
        var graph = GraphWith("Modelica.Units.SI.Voltage", "Lib.M");

        Assert.Equal("Modelica.Units.SI.Voltage",
            TypeResolver.Resolve(graph, "Lib.M", "Voltage", ["Modelica.Units.SI.*"])!.Id);
    }

    [Fact]
    public void AnExplicitListImport_ResolvesANameFromThePackage()
    {
        var graph = GraphWith("Modelica.Units.SI.Current", "Lib.M");

        Assert.Equal("Modelica.Units.SI.Current",
            TypeResolver.Resolve(graph, "Lib.M", "Current", ["Modelica.Units.SI.{Voltage, Current}"])!.Id);
    }

    [Fact]
    public void APlainImport_MakesItsLastSegmentTheName()
    {
        var graph = GraphWith("Modelica.Units.SI", "Lib.M");

        Assert.Equal("Modelica.Units.SI",
            TypeResolver.Resolve(graph, "Lib.M", "SI", ["Modelica.Units.SI"])!.Id);
    }

    [Fact]
    public void APlainImport_ResolvesANameBeneathItsLastSegment()
    {
        var graph = GraphWith("Modelica.Units.SI.Voltage", "Lib.M");

        Assert.Equal("Modelica.Units.SI.Voltage",
            TypeResolver.Resolve(graph, "Lib.M", "SI.Voltage", ["Modelica.Units.SI"])!.Id);
    }

    [Fact]
    public void APlainImportWithNoDots_StillActsAsItsOwnName()
    {
        var graph = GraphWith("Modelica", "Lib.M");

        Assert.Equal("Modelica", TypeResolver.Resolve(graph, "Lib.M", "Modelica", ["Modelica"])!.Id);
    }

    [Fact]
    public void APlainImport_DoesNotAnswerForAnUnrelatedName()
    {
        var graph = GraphWith("Modelica.Units.SI.Voltage", "Lib.M");

        Assert.Null(TypeResolver.Resolve(graph, "Lib.M", "Current", ["Modelica.Units.SI"]));
    }

    [Fact]
    public void AnImportPointingNowhere_ResolvesToNothing()
    {
        var graph = GraphWith("Lib.M");

        Assert.Null(TypeResolver.Resolve(graph, "Lib.M", "SI", ["SI = Modelica.Units.SI"]));
    }

    [Fact]
    public void TheFirstImportThatAnswers_Wins()
    {
        var graph = GraphWith("A.Target", "B.Target", "Lib.M");

        Assert.Equal("A.Target",
            TypeResolver.Resolve(graph, "Lib.M", "Target", ["A.*", "B.*"])!.Id);
    }

    // ── names inherited into scope ──

    [Fact]
    public void ANameInABaseClassesPackage_ResolvesThroughExtends()
    {
        // Lib.M extends Base.Thing, and refers to Helper — which lives beside Base.Thing, not beside
        // Lib.M. Modelica brings it into scope; reporting it unresolved would be a finding against
        // correct code.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Base.Helper", "Helper", "model Helper\nend Helper;"));
        graph.AddNode(new ModelNode("Base.Thing", "Thing", "model Thing\nend Thing;"));
        graph.AddNode(new ModelNode("Lib.M", "M",
            "model M\n  extends Base.Thing;\nend M;"));

        Assert.Null(TypeResolver.Resolve(graph, "Lib.M", "Helper"));
        Assert.Equal("Base.Helper", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Helper", null)!.Id);
    }

    [Fact]
    public void AnInheritedImport_IsInScopeToo()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Modelica.Units.SI.Voltage", "Voltage", "type Voltage = Real;"));
        graph.AddNode(new ModelNode("Base.Thing", "Thing",
            "model Thing\n  import Modelica.Units.SI.*;\nend Thing;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Thing;\nend M;"));

        Assert.Equal("Modelica.Units.SI.Voltage",
            TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Voltage", null)!.Id);
    }

    [Fact]
    public void ResolveWithInheritance_AnswersDirectlyWhenItCan()
    {
        var graph = GraphWith("Lib.Target", "Lib.M");

        Assert.Equal("Lib.Target", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Target", null)!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Real")]
    public void ResolveWithInheritance_HasNothingToSayAboutThese(string? typeText)
    {
        var graph = GraphWith("Lib.M");

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.M", typeText, null));
    }

    [Fact]
    public void ACycleInTheInheritance_DoesNotHangTheResolver()
    {
        // Invalid Modelica, but a half-edited file produces it and the resolver runs over whatever is
        // on disk.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.A", "A", "model A\n  extends Lib.B;\nend A;"));
        graph.AddNode(new ModelNode("Lib.B", "B", "model B\n  extends Lib.A;\nend B;"));

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.A", "NoSuchType", null));
    }

    [Fact]
    public void AnUnresolvableBaseClass_IsSkippedRatherThanFatal()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Missing.Thing;\nend M;"));

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Whatever", null));
    }
}

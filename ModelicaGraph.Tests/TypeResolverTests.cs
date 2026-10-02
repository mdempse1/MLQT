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

    [Fact]
    public void AnEnclosingPackagesClass_WinsOverATopLevelClassOfTheSameName()
    {
        // Modelica takes the innermost scope that has the name; the name as written, matched at the
        // root, is the last resort and not the first.
        var graph = GraphWith("Constants", "Lib.Constants", "Lib.Examples.M");

        Assert.Equal("Lib.Constants", TypeResolver.Resolve(graph, "Lib.Examples.M", "Constants")!.Id);
    }

    [Fact]
    public void AnImport_WinsOverATopLevelClassOfTheSameName()
    {
        var graph = GraphWith("SI", "Modelica.Units.SI", "Lib.M");

        Assert.Equal("Modelica.Units.SI",
            TypeResolver.Resolve(graph, "Lib.M", "SI", ["SI = Modelica.Units.SI"])!.Id);
    }

    [Fact]
    public void AGlobalName_IsLookedUpFromTheTopOnly()
    {
        var graph = GraphWith("Constants", "Lib.Constants", "Lib.M");

        Assert.Equal("Constants", TypeResolver.Resolve(graph, "Lib.M", ".Constants")!.Id);
        Assert.Equal("Constants", ReferenceResolver.Resolve(graph, "Lib.M", [], ".Constants"));
        Assert.Equal("Lib.Constants", ReferenceResolver.Resolve(graph, "Lib.M", [], "Constants"));
        Assert.Null(TypeResolver.Resolve(GraphWith("Lib.Constants", "Lib.M"), "Lib.M", ".Constants"));
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
    public void ABaseClassesNestedClass_IsInherited()
    {
        // Lib.M extends Base.Thing, which declares Medium; M writes Medium.State. A nested class is an
        // element, and elements are inherited.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Base.Thing", "Thing", "model Thing\n  package Medium\n  end Medium;\nend Thing;"));
        graph.AddNode(new ModelNode("Base.Thing.Medium", "Medium", "package Medium\nend Medium;"));
        graph.AddNode(new ModelNode("Base.Thing.Medium.State", "State", "record State\nend State;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Thing;\nend M;"));

        Assert.Null(TypeResolver.Resolve(graph, "Lib.M", "Medium.State"));
        Assert.Equal("Base.Thing.Medium.State",
            TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Medium.State", null)!.Id);
    }

    [Fact]
    public void ANearerBasesNestedClass_WinsOverTheOneItReplaces()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Base.Root", "Root", "model Root\nend Root;"));
        graph.AddNode(new ModelNode("Base.Root.Medium", "Medium", "package Medium\nend Medium;"));
        graph.AddNode(new ModelNode("Base.Mid", "Mid", "model Mid\n  extends Base.Root;\nend Mid;"));
        graph.AddNode(new ModelNode("Base.Mid.Medium", "Medium", "package Medium\nend Medium;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Mid;\nend M;"));

        Assert.Equal("Base.Mid.Medium", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Medium", null)!.Id);

        // ...and one only the grandparent declares is inherited all the same.
        graph.AddNode(new ModelNode("Base.Root.Only", "Only", "record Only\nend Only;"));
        Assert.Equal("Base.Root.Only", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Only", null)!.Id);
    }

    [Fact]
    public void ABaseClassesPackage_IsNotInherited()
    {
        // Helper lives beside Base.Thing, not in it. Lookup goes through a class's own elements -
        // inherited ones included - and then the classes enclosing IT (MLS 5.3.1), never the
        // classes enclosing a base. Reading it this way once let a class name what Modelica would
        // reject, and let an encapsulated class reach past itself through its base.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Base.Helper", "Helper", "model Helper\nend Helper;"));
        graph.AddNode(new ModelNode("Base.Thing", "Thing", "model Thing\nend Thing;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Thing;\nend M;"));

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Helper", null));
        // Asked where the name is written - the base - it resolves, which is what OwnerId is for.
        Assert.Equal("Base.Helper", TypeResolver.ResolveWithInheritance(graph, "Base.Thing", "Helper", null)!.Id);
    }

    [Fact]
    public void ABaseClassesImport_IsNotInherited()
    {
        // Imports are not inherited (MLS 13.2.1).
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Modelica.Units.SI.Voltage", "Voltage", "type Voltage = Real;"));
        graph.AddNode(new ModelNode("Base.Thing", "Thing",
            "model Thing\n  import Modelica.Units.SI.*;\nend Thing;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Thing;\nend M;"));

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Voltage", null));
    }

    [Fact]
    public void AGlobalName_IsNotLookedForAmongTheBases()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Base.Thing", "Thing", "model Thing\nend Thing;"));
        graph.AddNode(new ModelNode("Base.Thing.Medium", "Medium", "package Medium\nend Medium;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends Base.Thing;\nend M;"));

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Lib.M", ".Medium", null));
    }

    [Fact]
    public void WithoutImportsGiven_TheClassesOwnAreRead()
    {
        // An encapsulated class stops the lookup before the root, so a caller that passed no imports
        // used to lose every name the class imports - a fully qualified one included.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Modelica.Blocks.Step", "Step", "block Step\nend Step;"));
        graph.AddNode(new ModelNode("Lib.Sealed", "Sealed",
            "encapsulated model Sealed\n  import Modelica;\nend Sealed;"));

        Assert.Equal("Modelica.Blocks.Step", TypeResolver.Resolve(graph, "Lib.Sealed", "Modelica.Blocks.Step", null)?.Id);
        Assert.Null(TypeResolver.Resolve(graph, "Lib.Sealed", "Modelica.Blocks.Step", []));   // none, said so
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

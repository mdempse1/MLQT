using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="ClassElementResolver.ResolveReference"/>: a component reference written in a class,
/// resolved to the element each segment names and the class the last one's type is.
/// </summary>
public class ClassElementResolverReferenceTests
{
    private static ModelNode Node(string id, string kind, string code) =>
        new(id, id.Split('.')[^1], code) { ClassType = kind };

    // A cut-down Modelica.Mechanics.Rotational: a connector, a partial with two of them, a component
    // extending it, and a model using two instances.
    private static DirectedGraph Rotational()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Node("Lib", "package", "package Lib\n  constant Real g = 9.81;\nend Lib;"));
        graph.AddNode(Node("Lib.SI", "package", "package SI\nend SI;"));
        graph.AddNode(Node("Lib.SI.Torque", "type", "type Torque = Real(unit=\"N.m\");"));
        graph.AddNode(Node("Lib.SI.Angle", "type", "type Angle = Real(unit=\"rad\");"));
        graph.AddNode(Node("Lib.Flange", "connector",
            "connector Flange\n  import Lib.SI;\n  SI.Angle phi;\n  flow SI.Torque tau;\nend Flange;"));
        graph.AddNode(Node("Lib.TwoFlanges", "model",
            "partial model TwoFlanges\n  Flange flange_a;\n  Flange flange_b;\nprotected\n  Real hidden;\nend TwoFlanges;"));
        graph.AddNode(Node("Lib.Inertia", "model",
            "model Inertia\n  extends TwoFlanges;\n  parameter Real J = 1;\n  Real w;\nend Inertia;"));
        graph.AddNode(Node("Lib.BigInertia", "model", "model BigInertia = Inertia(J = 10);"));
        graph.AddNode(Node("Lib.Constants", "package",
            "package Constants\n  constant Real pi = 3.14159;\nend Constants;"));
        graph.AddNode(Node("Lib.Examples", "package", "package Examples\nend Examples;"));
        graph.AddNode(Node("Lib.Examples.Drive", "model",
            "model Drive\n" +
            "  Inertia inertia1(J = 2);\n" +
            "  Inertia shafts[3];\n" +
            "  BigInertia big;\n" +
            "  Real x;\n" +
            "protected\n" +
            "  Real secret;\n" +
            "equation\n" +
            "  connect(inertia1.flange_b, shafts[1].flange_a);\n" +
            "end Drive;"));
        return graph;
    }

    private static ResolvedReference? Resolve(DirectedGraph graph, string reference,
        ClassElementResolver.InterfaceCache? interfaces = null) =>
        ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Lib.Examples.Drive")!,
            reference, interfaces, new TypeResolver.AncestorCache());

    [Fact]
    public void AnInheritedConnectorsVariable_ResolvesToItsDeclarationAndType()
    {
        var resolved = Resolve(Rotational(), "inertia1.flange_b.tau");

        Assert.NotNull(resolved);
        Assert.Equal(["inertia1", "flange_b", "tau"], resolved.Path.Select(p => p.Element.Name));
        Assert.Equal("tau", resolved.Element.Element.Name);
        Assert.Equal("flow", resolved.Element.Element.Connection);
        Assert.Equal("Lib.Flange", resolved.Element.OwnerId);
        Assert.Equal("Lib.SI.Torque", resolved.Type?.Id);
        Assert.Equal("Lib.Examples.Drive", resolved.Scope.Id);

        // flange_b is declared in the partial base, and says so.
        Assert.Equal("Lib.TwoFlanges", resolved.Path[1].InheritedFrom);
    }

    [Fact]
    public void AComponentResolvesToItsClass()
    {
        var resolved = Resolve(Rotational(), "inertia1.flange_b");

        Assert.Equal("flange_b", resolved?.Element.Element.Name);
        Assert.Equal("Lib.Flange", resolved?.Type?.Id);
    }

    [Fact]
    public void APredefinedType_ResolvesTheElementWithNoClass()
    {
        var resolved = Resolve(Rotational(), "x");

        Assert.Equal("Real", resolved?.Element.Element.Type);
        Assert.Null(resolved?.Type);
    }

    [Theory]
    [InlineData("shafts[2].flange_a.phi")]
    [InlineData("shafts[i + 1].flange_a.phi")]
    [InlineData("shafts[a[1]].flange_a.phi")]
    [InlineData(" shafts [ 2 ] . flange_a . phi ")]
    public void SubscriptsAndWhitespaceAreDropped(string reference)
    {
        var resolved = Resolve(Rotational(), reference);

        Assert.Equal("Lib.SI.Angle", resolved?.Type?.Id);
    }

    [Fact]
    public void AnInstanceModification_IsTheDefault()
    {
        var graph = Rotational();

        Assert.Equal("2", Resolve(graph, "inertia1.J")?.Element.Element.DefaultValue);
        Assert.Equal("1", Resolve(graph, "shafts[1].J")?.Element.Element.DefaultValue);
    }

    [Fact]
    public void AShortClassesMembers_AreItsBases()
    {
        var resolved = Resolve(Rotational(), "big.flange_a.tau");

        Assert.Equal("Lib.SI.Torque", resolved?.Type?.Id);
        Assert.Equal("Lib.TwoFlanges", resolved?.Path[1].InheritedFrom);

        // Declared in the short class's base itself, so that is where it is inherited from.
        Assert.Equal("Lib.Inertia", Resolve(Rotational(), "big.J")?.Element.InheritedFrom);
    }

    [Fact]
    public void TheClassesOwnProtectedElement_Resolves_ButNotOneReachedThroughADot()
    {
        var graph = Rotational();

        Assert.NotNull(Resolve(graph, "secret"));
        Assert.Null(Resolve(graph, "inertia1.hidden"));
    }

    [Fact]
    public void AnEnclosingPackagesConstant_Resolves()
    {
        var resolved = Resolve(Rotational(), "g");

        Assert.Equal("Lib", resolved?.Scope.Id);
        Assert.Equal("Lib", resolved?.Element.OwnerId);
    }

    [Theory]
    [InlineData("Constants.pi")]
    [InlineData("Lib.Constants.pi")]
    [InlineData(".Lib.Constants.pi")]
    public void AClassQualifiedReference_ResolvesInThatClass(string reference)
    {
        var resolved = Resolve(Rotational(), reference);

        Assert.Equal("pi", resolved?.Element.Element.Name);
        Assert.Equal("Lib.Constants", resolved?.Scope.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nothing")]
    [InlineData("inertia1.nothing")]
    [InlineData("inertia1.flange_b.tau.more")]   // through a predefined type
    [InlineData("x.y")]
    [InlineData("Constants")]                   // a class, not a component
    [InlineData("Constants.nothing")]
    [InlineData(".Constants.pi")]               // global, so not relative
    [InlineData("inertia1..J")]
    [InlineData("inertia1[1.J")]
    [InlineData("inertia1]")]
    [InlineData("'unterminated")]
    public void WhatIsNotAReference_ResolvesToNull(string reference)
    {
        Assert.Null(Resolve(Rotational(), reference));
    }

    [Fact]
    public void AQuotedIdentifier_KeepsItsDotsAndSpaces()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Node("M", "model", "model M\n  Real 'a. b';\nend M;"));

        var resolved = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("M")!, "'a. b'");

        Assert.Equal("'a. b'", resolved?.Element.Element.Name);
    }

    [Fact]
    public void AnElementTheClassDeclaresItself_IsNotInherited()
    {
        Assert.Null(Resolve(Rotational(), "inertia1")?.Element.InheritedFrom);
    }

    [Fact]
    public void ANestedClassOfTheSameName_HidesAnEnclosingConstant()
    {
        var graph = Rotational();
        graph.AddNode(Node("Lib.Examples.Shadow", "model",
            "model Shadow\n  record g\n  end g;\nend Shadow;"));

        Assert.Null(ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Lib.Examples.Shadow")!, "g"));
    }

    [Fact]
    public void AQualifiedPrefix_IsResolvedThroughTheClassesImports()
    {
        var graph = Rotational();
        graph.AddNode(Node("Other", "model", "model Other\n  import K = Lib.Constants;\nend Other;"));

        var resolved = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Other")!, "K.pi");

        Assert.Equal("Lib.Constants", resolved?.Scope.Id);
    }

    [Fact]
    public void AnArrayDeclaredOnTheType_ResolvesToTheElementType()
    {
        var graph = Rotational();
        graph.AddNode(Node("Arr", "model", "model Arr\n  Lib.Flange[2] fs;\nend Arr;"));

        var resolved = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Arr")!, "fs[1].tau");

        Assert.Equal("Lib.SI.Torque", resolved?.Type?.Id);
    }

    [Fact]
    public void AModificationTwoLevelsOut_IsTheDefault()
    {
        var graph = Rotational();
        graph.AddNode(Node("Deep", "model", "model Deep\n  Lib.Inertia i(flange_a.phi = 3);\nend Deep;"));

        var resolved = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Deep")!, "i.flange_a.phi");

        Assert.Equal("3", resolved?.Element.Element.DefaultValue);
    }

    [Fact]
    public void AModificationIsAppliedAtItsOwnDepth_NotOneShort()
    {
        var graph = Rotational();
        graph.AddNode(Node("Holder", "model", "model Holder\n  Lib.Inertia i(flange_a.phi = 7);\nend Holder;"));
        graph.AddNode(Node("Outer", "model", "model Outer\n  Holder h;\nend Outer;"));

        var resolved = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Outer")!, "h.i.flange_a.phi");

        Assert.Null(resolved?.Path[2].Element.DefaultValue);
        Assert.Equal("7", resolved?.Element.Element.DefaultValue);
    }

    [Fact]
    public void AComponentOfAPackage_IsFoundUnderTheLongestPrefixThatIsAClass()
    {
        var graph = Rotational();
        graph.AddNode(Node("Lib.Data", "package",
            "package Data\n  constant Lib.Flange rest;\nprotected\n  constant Real internal = 1;\nend Data;"));

        Assert.Equal("Lib.SI.Torque", Resolve(graph, "Lib.Data.rest.tau")?.Type?.Id);
        Assert.Null(Resolve(graph, "Lib.Data.internal"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"'a\")]
    public void NoTextOrATrailingEscape_IsNotAReference(string? reference)
    {
        Assert.Null(ClassElementResolver.ReferenceSegments(reference));
    }

    [Theory]
    [InlineData(@"'a\'b'.c", new[] { @"'a\'b'", "c" })]
    [InlineData("'x[1]'[2].y", new[] { "'x[1]'", "y" })]
    public void AQuotedIdentifier_IsOneSegment_EscapesIncluded(string reference, string[] expected)
    {
        Assert.Equal(expected, ClassElementResolver.ReferenceSegments(reference)?.Segments);
    }

    [Fact]
    public void ACacheGivesTheSameAnswer_AndKeepsTheTypesPassedThrough()
    {
        var graph = Rotational();
        var interfaces = new ClassElementResolver.InterfaceCache();

        var cached = Resolve(graph, "inertia1.flange_b.tau", interfaces);
        var uncached = Resolve(graph, "inertia1.flange_b.tau");

        Assert.Equal(uncached?.Type?.Id, cached?.Type?.Id);
        Assert.Equal(uncached?.Path.Select(p => p.OwnerId), cached?.Path.Select(p => p.OwnerId));
        Assert.True(interfaces.Count >= 3);   // Drive, Inertia, TwoFlanges, Flange
    }
}

using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The classes a class inherits are in scope where Modelica puts them (MLS §5.3.1): among the class's
/// own elements, after the ones it declares and <b>before</b> its imports and its enclosing packages,
/// at every scope on the way out.
/// </summary>
/// <remarks>
/// Found last, a package's own <c>Medium</c> answered for the replaceable one a base declares - the
/// way every fluid library is written - in the type lookup, in component references, and in
/// dependency analysis, which had not looked at inheritance at all. Over MSL the last of those had
/// reported 37 classes as unused that the media packages call by inheritance, Buildings 6.
/// </remarks>
public class InheritedLookupTests
{
    /// <summary>
    /// Lib.M extends Base, which declares Medium; Lib has a Medium of its own, and Other.Imp imports
    /// one. Base.Medium is what Medium means in both.
    /// </summary>
    private static DirectedGraph Graph()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib\nend Lib;"));
        graph.AddNode(new ModelNode("Lib.Medium", "Medium", "package Medium\n  constant Real p = 1;\nend Medium;"));
        graph.AddNode(new ModelNode("Base", "Base", "model Base\n  package Medium\n    constant Real p = 2;\n  end Medium;\nend Base;"));
        graph.AddNode(new ModelNode("Base.Medium", "Medium", "package Medium\n  constant Real p = 2;\nend Medium;"));
        graph.AddNode(new ModelNode("Lib.Medium.State", "State", "record State\nend State;"));
        graph.AddNode(new ModelNode("Base.Medium.State", "State", "record State\nend State;"));
        graph.AddNode(new ModelNode("Lib.M", "M", "model M\n  extends .Base;\n  Medium.State s;\nend M;"));
        return graph;
    }

    private static HashSet<string> Dependencies(DirectedGraph graph, string modelId)
    {
        var analyzer = new ModelAnalyzer(modelId, graph, new TypeResolver.AncestorCache());
        analyzer.Visit(ModelicaParserHelper.Parse(graph.GetNode<ModelNode>(modelId)!.Definition.ModelicaCode));
        return analyzer.ReferencedModels;
    }

    [Fact]
    public void AnInheritedClass_WinsOverTheEnclosingPackagesOwn()
    {
        var graph = Graph();

        Assert.Equal("Base.Medium", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Medium", null)?.Id);
        // Plain Resolve is the lookup for an extends clause's base name, which does not see inheritance.
        Assert.Equal("Lib.Medium", TypeResolver.Resolve(graph, "Lib.M", "Medium")?.Id);
    }

    [Fact]
    public void AnInheritedClass_WinsOverAnImport()
    {
        var graph = Graph();
        graph.AddNode(new ModelNode("Other", "Other", "package Other\nend Other;"));
        graph.AddNode(new ModelNode("Other.Imp", "Imp", "model Imp\n  import Medium = Lib.Medium;\n  extends .Base;\nend Imp;"));

        Assert.Equal("Base.Medium",
            TypeResolver.ResolveWithInheritance(graph, "Other.Imp", "Medium", ["Medium = Lib.Medium"])?.Id);
    }

    [Fact]
    public void AClassItDeclaresItself_StillWinsOverAnInheritedOne()
    {
        var graph = Graph();
        graph.AddNode(new ModelNode("Lib.M.Medium", "Medium", "package Medium\nend Medium;"));

        Assert.Equal("Lib.M.Medium", TypeResolver.ResolveWithInheritance(graph, "Lib.M", "Medium", null)?.Id);
    }

    [Fact]
    public void AnEnclosingPackagesInheritedClass_IsInScope()
    {
        // Inner sits in Lib.Pkg, which extends Base: Base.Medium is one of Pkg's elements, so it is
        // visible from the classes inside Pkg - before Lib's own Medium further out.
        var graph = Graph();
        graph.AddNode(new ModelNode("Lib.Pkg", "Pkg", "package Pkg\n  extends .Base;\nend Pkg;"));
        graph.AddNode(new ModelNode("Lib.Pkg.Inner", "Inner", "model Inner\nend Inner;"));

        Assert.Equal("Base.Medium", TypeResolver.ResolveWithInheritance(graph, "Lib.Pkg.Inner", "Medium", null)?.Id);
    }

    [Fact]
    public void AComponentReference_GoesThroughTheInheritedClass()
    {
        var graph = Graph();

        var p = ClassElementResolver.ResolveReference(graph, graph.GetNode<ModelNode>("Lib.M")!, "Medium.p");

        Assert.Equal("Base.Medium", p?.Scope.Id);
        Assert.Equal("2", p?.Element.Element.DefaultValue);
    }

    [Fact]
    public void DependencyAnalysis_LinksToTheInheritedClass()
    {
        var used = Dependencies(Graph(), "Lib.M");

        Assert.Contains("Base.Medium.State", used);
        Assert.DoesNotContain("Lib.Medium.State", used);
    }

    [Fact]
    public void DependencyAnalysis_SeesABaseFunctionCalledByItsBareName()
    {
        // MSL's media: PartialMedium declares beta(state), and a medium extending it calls beta(...).
        // Without inheritance the call linked to nothing, and the unused-class rule reported beta.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Media", "Media", "package Media\nend Media;"));
        graph.AddNode(new ModelNode("Media.Partial", "Partial", "partial package Partial\nend Partial;"));
        graph.AddNode(new ModelNode("Media.Partial.beta", "beta",
            "function beta\n  input Real x;\n  output Real y;\nalgorithm\n  y := x;\nend beta;"));
        graph.AddNode(new ModelNode("Media.Water", "Water",
            "package Water\n  extends Media.Partial;\n  constant Real b = beta(1);\nend Water;"));

        Assert.Contains("Media.Partial.beta", Dependencies(graph, "Media.Water"));
    }

    [Fact]
    public void TheReferenceLocator_FindsAReferenceThroughTheInheritedClass()
    {
        // So renaming Base.Medium.State finds the use in Lib.M, and renaming Lib.Medium.State leaves
        // it alone.
        var graph = Graph();
        const string code = "within Lib;\nmodel M\n  extends .Base;\n  Medium.State s;\nend M;";

        Assert.Single(ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), ["Base.Medium.State"]));
        Assert.Empty(ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), ["Lib.Medium.State"]));
    }

    [Fact]
    public void WhatAClassExtends_IsReadOnceAndKept_AndReadAgainWhenTheSourceChanges()
    {
        var definition = new ModelDefinition("M", "model M\n  extends A;\n  extends B.C(k = 1);\nend M;");

        Assert.Equal(["A", "B.C"], ClassImports.BasesOf(definition));
        Assert.Equal(["A", "B.C"], definition.Bases);

        definition.ModelicaCode = "model M = D(x = 2);";
        Assert.Null(definition.Bases);
        Assert.Equal(["D"], ClassImports.BasesOf(definition));

        definition.ModelicaCode = "type T = Real;";
        Assert.Equal(["Real"], ClassImports.BasesOf(definition));   // resolves to nothing, as before
    }

    [Fact]
    public void AClassThatDoesNotParse_ExtendsNothing()
    {
        Assert.Empty(ClassImports.BasesOf(new ModelDefinition("M", "")));
    }
}

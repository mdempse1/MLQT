using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="ClassElementResolver.BaseClasses"/>: what a class inherits that is not an element - its
/// Diagram layer and its connections (B316) - comes from these classes, in the order they are drawn.
/// </summary>
public class ClassElementResolverBaseClassesTests
{
    private static ModelNode Model(string id, string code) =>
        new(id, id, code) { ClassType = "model" };

    private static List<string> BasesOf(DirectedGraph graph, string id)
        => [.. ClassElementResolver.BaseClasses(graph, graph.GetNode<ModelNode>(id)!).Select(n => n.Id)];

    [Fact]
    public void EachBaseFollowsItsOwnBases_AndTheFirstClauseComesFirst()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Root", "model Root\nend Root;"));
        graph.AddNode(Model("Left", "model Left\n  extends Root;\nend Left;"));
        graph.AddNode(Model("Right", "model Right\nend Right;"));
        graph.AddNode(Model("Leaf", "model Leaf\n  extends Left;\n  extends Right;\nend Leaf;"));

        Assert.Equal(["Root", "Left", "Right"], BasesOf(graph, "Leaf"));
    }

    [Fact]
    public void ADiamondIsWalkedOnce_AndAMissingBaseIsSkipped()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Root", "model Root\nend Root;"));
        graph.AddNode(Model("A", "model A\n  extends Root;\nend A;"));
        graph.AddNode(Model("B", "model B\n  extends Root;\n  extends NotLoaded;\nend B;"));
        graph.AddNode(Model("Leaf", "model Leaf\n  extends A;\n  extends B;\nend Leaf;"));

        Assert.Equal(["Root", "A", "B"], BasesOf(graph, "Leaf"));
    }

    [Fact]
    public void AClassWithNoBasesHasNone_AndACycleEnds()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Alone", "model Alone\n  Real x;\nend Alone;"));
        graph.AddNode(Model("P", "model P\n  extends Q;\nend P;"));
        graph.AddNode(Model("Q", "model Q\n  extends P;\nend Q;"));

        Assert.Empty(BasesOf(graph, "Alone"));
        Assert.Equal(["Q"], BasesOf(graph, "P"));
    }

    [Fact]
    public void AShortClassesBase_IsItsBase_AsCollectHasIt()
    {
        // A diagram takes its components from Collect and its wiring from these; the two must agree
        // on what a short class inherits, or the picture has parts and no wires (B316).
        var graph = new DirectedGraph();
        graph.AddNode(Model("Root", "model Root\nend Root;"));
        graph.AddNode(Model("Part", "model Part\n  extends Root;\nend Part;"));
        graph.AddNode(Model("Short", "model Short = Part(x = 1);"));
        var shortClass = graph.GetNode<ModelNode>("Short")!;

        Assert.Equal(["Root", "Part"], BasesOf(graph, "Short"));
        var (written, direct) = Assert.Single(ClassElementResolver.DirectBases(graph, shortClass));
        Assert.Equal("Part", written);
        Assert.Equal("Part", direct.Id);
    }

    [Fact]
    public void AShortClassOfAPredefinedType_HasNoBaseClass()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Length", "type Length = Real(unit = \"m\");"));

        Assert.Empty(BasesOf(graph, "Length"));
    }

    [Fact]
    public void AClassThatDoesNotParseHasNoBases()
    {
        var graph = new DirectedGraph();
        graph.AddNode(Model("Broken", "model Broken\n  extends ;;; nonsense\n"));

        Assert.Empty(BasesOf(graph, "Broken"));
    }
}

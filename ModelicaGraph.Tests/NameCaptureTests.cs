using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="NameCapture.CapturedBy"/>: whether renaming <c>Root.Src.Pkg</c> to <c>Box</c> changes
/// what <c>Box</c> means where a renamed reference sits - captured by an element of that name met
/// first on the way out.
/// </summary>
public class NameCaptureTests
{
    private static DirectedGraph Graph(params (string Id, string Code)[] extra)
    {
        var graph = new DirectedGraph();
        void Add(string id, string code) => graph.AddNode(new ModelNode(id, id.Split('.')[^1], code));
        Add("Root", "package Root\nend Root;");
        Add("Root.Src", "package Src\nend Src;");
        Add("Root.Src.Pkg", "package Pkg\nend Pkg;");
        Add("Root.Src.Plain", "model Plain\nend Plain;");
        foreach (var (id, code) in extra)
            Add(id, code);
        return graph;
    }

    private static string? Captured(DirectedGraph graph, string scope, string renamed = "Root.Src.Pkg") =>
        NameCapture.CapturedBy(graph, scope, "Box", renamed);

    [Fact]
    public void NothingCalledTheNewName_CapturesNothing()
    {
        Assert.Null(Captured(Graph(), "Root.Src.Plain"));
    }

    [Fact]
    public void ANestedClassOfTheNewName_Captures()
    {
        var graph = Graph(("Root.Src.Other", "model Other\n  package Box\n  end Box;\nend Other;"),
                          ("Root.Src.Other.Box", "package Box\nend Box;"));

        Assert.Equal("Root.Src.Other.Box", Captured(graph, "Root.Src.Other"));
    }

    [Fact]
    public void AComponentOfTheNewName_Captures()
    {
        var graph = Graph(("Root.Src.Other", "model Other\n  Real Box;\nend Other;"));

        Assert.Equal("component Box of Root.Src.Other", Captured(graph, "Root.Src.Other"));
    }

    [Fact]
    public void AnInheritedElementOfTheNewName_Captures()
    {
        var graph = Graph(("Root.Src.Base", "model Base\n  Real Box;\nend Base;"),
                          ("Root.Src.Other", "model Other\n  extends Root.Src.Base;\nend Other;"));

        Assert.Equal("component Box of Root.Src.Base", Captured(graph, "Root.Src.Other"));
    }

    [Fact]
    public void AnImportOfTheNewName_Captures()
    {
        var graph = Graph(("Lib", "package Lib\nend Lib;"), ("Lib.Box", "package Box\nend Box;"),
                          ("Root.Src.Other", "model Other\n  import Lib.Box;\nend Other;"));

        Assert.Equal("Lib.Box", Captured(graph, "Root.Src.Other"));
    }

    [Fact]
    public void AnEnclosingClassBelowTheRenamedOnesParent_Captures()
    {
        // Inner sits in Root.Src.Mid, which declares Box - nearer than Root.Src, where Pkg is.
        var graph = Graph(("Root.Src.Mid", "package Mid\n  constant Real Box = 1;\nend Mid;"),
                          ("Root.Src.Mid.Inner", "model Inner\nend Inner;"));

        Assert.Equal("component Box of Root.Src.Mid", Captured(graph, "Root.Src.Mid.Inner"));
    }

    [Fact]
    public void SomethingCalledTheNewNameFurtherOut_CapturesNothing()
    {
        // A top-level Box, or one in Root: the renamed class, in Root.Src, is found first.
        var graph = Graph(("Box", "package Box\nend Box;"), ("Root.Box", "package Box\nend Box;"));

        Assert.Null(Captured(graph, "Root.Src.Plain"));
    }

    [Fact]
    public void ThePlaceTheRenamedClassIsFound_EndsTheSearch_ByInheritanceOrImport()
    {
        // Derived extends Base, whose Medium is being renamed to Box: Base.Box is found there,
        // before Root's Box further out. And an import of the renamed class is where it is found.
        var graph = Graph(("Root.Src.Base", "model Base\n  package Medium\n  end Medium;\nend Base;"),
                          ("Root.Src.Base.Medium", "package Medium\nend Medium;"),
                          ("Root.Src.Derived", "model Derived\n  extends Root.Src.Base;\nend Derived;"),
                          ("Root.Box", "package Box\nend Box;"),
                          ("Lib", "package Lib\nend Lib;"),
                          ("Lib.User", "model User\n  import Root.Src.Pkg;\nend User;"));

        Assert.Null(Captured(graph, "Root.Src.Derived", "Root.Src.Base.Medium"));
        Assert.Null(Captured(graph, "Lib.User"));
    }

    [Fact]
    public void ADerivedClassesOwnElement_CapturesBeforeTheInheritedRenamedClass()
    {
        var graph = Graph(("Root.Src.Base", "model Base\n  package Medium\n  end Medium;\nend Base;"),
                          ("Root.Src.Base.Medium", "package Medium\nend Medium;"),
                          ("Root.Src.Derived", "model Derived\n  extends Root.Src.Base;\n  Real Box;\nend Derived;"));

        Assert.Equal("component Box of Root.Src.Derived", Captured(graph, "Root.Src.Derived", "Root.Src.Base.Medium"));
    }

    [Fact]
    public void TheRenamedClassesParentHoldingTheNewName_IsAClash()
    {
        var graph = Graph(("Root.Src.Holder", "package Holder\nend Holder;"));
        graph.GetNode<ModelNode>("Root.Src")!.Definition.ModelicaCode = "package Src\n  constant Real Box = 1;\nend Src;";

        Assert.Equal("component Box of Root.Src", Captured(graph, "Root.Src"));
    }

    [Fact]
    public void AnEncapsulatedClassEndsTheSearch()
    {
        var graph = Graph(("Root.Src.Sealed", "encapsulated model Sealed\nend Sealed;"),
                          ("Root.Box", "package Box\nend Box;"));

        Assert.Null(Captured(graph, "Root.Src.Sealed"));
    }
}

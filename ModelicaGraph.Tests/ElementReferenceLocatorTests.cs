using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Tests;

/// <summary>
/// Where one element - a component, by the class declaring it and its name - is named in a file, for a
/// rename that rewrites exactly those identifiers. Each case is a shape the MCP rename's own tests do
/// not reach.
/// </summary>
public class ElementReferenceLocatorTests
{
    private const string Wheel = "model Wheel\n  parameter Real Fz = 1;\nend Wheel;";

    /// <summary>A graph of classes in package P, each with its own source, so their members resolve.</summary>
    private static DirectedGraph Graph(params (string Leaf, string Code)[] classes)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P\nend P;"));
        foreach (var (leaf, code) in classes)
            graph.AddNode(new ModelNode($"P.{leaf}", leaf, code) { ParentModelName = "P" });
        return graph;
    }

    private static List<(ElementSiteKind Kind, string Text, string Scope)> Sites(DirectedGraph graph, string file)
    {
        var sites = ElementReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(file), "P.Wheel", "Fz");
        // Every span selects exactly the name.
        Assert.All(sites, s => Assert.Equal("Fz", file.Substring(s.Name.StartIndex, s.Name.StopIndex - s.Name.StartIndex + 1)));
        return [.. sites.Select(s => (s.Kind, s.Name.Text, s.ScopeId))];
    }

    [Fact]
    public void AShortClass_ThatSetsIt_IsASite()
    {
        const string small = "model Small = Wheel(Fz = 2);";
        var graph = Graph(("Wheel", Wheel), ("Small", small));

        var site = Assert.Single(Sites(graph, "within P;\n" + small));

        Assert.Equal(ElementSiteKind.Modification, site.Kind);
        Assert.Equal("P.Small", site.Scope);
    }

    [Fact]
    public void ADottedModification_NamesItInItsLastSegment_NotTheComponentBefore()
    {
        const string axle = "model Axle\n  Wheel wheel;\nend Axle;";
        const string car = "model Car\n  extends Axle(wheel.Fz = 1);\nend Car;";
        var graph = Graph(("Wheel", Wheel), ("Axle", axle), ("Car", car));

        var site = Assert.Single(Sites(graph, "within P;\n" + car));

        Assert.Equal(ElementSiteKind.Modification, site.Kind);
    }

    [Fact]
    public void AComprehensionsIterator_IsNotTheElement_AndIsRecordedWhereTheElementIs()
    {
        const string wheel = "model Wheel\n  parameter Real Fz = 1;\n  Real v[2] = {Fz*k for k in 1:2};\n  Real w[2] = {Fz for Fz in 1:2};\nend Wheel;";
        var graph = Graph(("Wheel", wheel));

        var sites = ElementReferenceLocator.Locate(graph, ModelicaParserHelper.Parse("within P;\n" + wheel), "P.Wheel", "Fz");

        Assert.Equal(2, sites.Count);                                    // the declaration and `Fz*k`
        Assert.Equal(ElementSiteKind.Declaration, sites[0].Kind);
        Assert.Equal(["k"], sites[1].Iterators);                       // what a rename to `k` would hit
    }

    [Fact]
    public void AModificationInsideAnAnnotation_IsAnnotationGrammar_NotTheElement()
    {
        const string user = "model User\n  Wheel w annotation (__Vendor(Fz = 1));\nend User;";
        var graph = Graph(("Wheel", Wheel), ("User", user));

        Assert.Empty(Sites(graph, "within P;\n" + user));
    }

    [Fact]
    public void ARedeclarationWrittenInAModification_IsNotTheDeclaration()
    {
        // `redeclare Real Fz` inside Wheel's own text, in a modification of a component of Wheel.
        const string wheel = "model Wheel\n  parameter Real Fz = 1;\n  Wheel inner_(redeclare Real Fz = 2);\nend Wheel;";
        var graph = Graph(("Wheel", wheel));

        var sites = Sites(graph, "within P;\n" + wheel);

        Assert.Equal(ElementSiteKind.Declaration, Assert.Single(sites, s => s.Kind == ElementSiteKind.Declaration).Kind);
    }

    [Fact]
    public void ACalledName_IsAClass_NotAReference()
    {
        // A function happens to be called Fz; calling it is not the parameter.
        const string fz = "function Fz\n  input Real x;\n  output Real y = x;\nend Fz;";
        const string user = "model User\n  extends Wheel;\n  Real q = Fz(1);\nend User;";
        var graph = Graph(("Wheel", Wheel), ("Fz", fz), ("User", user));

        Assert.Empty(Sites(graph, "within P;\n" + user));
    }

    [Fact]
    public void ANamedArgumentToAnotherFunction_IsLeftAlone()
    {
        const string f = "function F\n  input Real Fz;\n  output Real y = Fz;\nend F;";
        const string user = "model User\n  Real q = F(Fz = 1);\nend User;";
        var graph = Graph(("Wheel", Wheel), ("F", f), ("User", user));

        Assert.Empty(Sites(graph, "within P;\n" + user));
    }

    [Fact]
    public void AFileWithNoWithinClause_IsWalkedFromTheTop()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Wheel", "Wheel", Wheel));

        var site = Assert.Single(ElementReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(Wheel), "Wheel", "Fz"));

        Assert.Equal(ElementSiteKind.Declaration, site.Kind);
        Assert.Equal("Wheel", site.ScopeId);
    }

    [Fact]
    public void TheLocator_SaysWhatItLooksFor()
    {
        var locator = new ElementReferenceLocator(new DirectedGraph(), "P.Wheel", "Fz");

        Assert.Equal("P.Wheel", locator.DeclaringClassId);
        Assert.Equal("Fz", locator.Name);
        Assert.Empty(locator.Sites);
    }
}

using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// A component reference uses the class it goes through: <c>Modelica.Constants.pi</c> uses
/// <c>Modelica.Constants</c>, <c>Types.Init.SteadyState</c> uses <c>Types.Init</c>.
/// <see cref="ComponentReferences.Start"/> says which class and how many segments name it, and
/// dependency analysis and the reference locator ask it.
/// </summary>
/// <remarks>
/// Both had resolved a reference only when the whole of it named a class, so such a use linked to
/// nothing - over MSL, 30 classes used only this way were reported unused (protected enumeration
/// types used by their literals, constant packages) - and renaming <c>Constants</c> left
/// <c>Constants.pi</c> naming a class that no longer existed.
/// </remarks>
public class ReferenceStartTests
{
    private static DirectedGraph Graph()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib\n  constant Real g = 9.81;\nend Lib;"));
        graph.AddNode(new ModelNode("Lib.Constants", "Constants", "package Constants\n  constant Real pi = 3.14;\nend Constants;"));
        graph.AddNode(new ModelNode("Lib.Types", "Types", "package Types\nend Types;"));
        graph.AddNode(new ModelNode("Lib.Types.Init", "Init", "type Init = enumeration(SteadyState, NoInit);"));
        graph.AddNode(new ModelNode("Lib.Part", "Part", "model Part\n  Real phi;\nend Part;"));
        // Classes that happen to share a component's name, and a language operator's.
        graph.AddNode(new ModelNode("Lib.Connections", "Connections", "package Connections\n  constant Real branch = 1;\nend Connections;"));
        graph.AddNode(new ModelNode("Lib.part", "part", "model part\n  Real phi;\nend part;"));
        graph.AddNode(new ModelNode("Lib.M", "M", Model));
        return graph;
    }

    private const string Model =
        "model M\n" +
        "  Real x;\n" +
        "  Part part;\n" +
        "  parameter Types.Init init = Types.Init.SteadyState;\n" +
        "equation\n" +
        "  x = Lib.Constants.pi + Constants.pi + g + part.phi;\n" +
        "end M;";

    private static ComponentReferences References(DirectedGraph graph) =>
        ClassElementResolver.ReferencesIn(graph, graph.GetNode<ModelNode>("Lib.M")!);

    [Theory]
    [InlineData("Lib.Constants.pi", "Lib.Constants", 2)]
    [InlineData("Constants.pi", "Lib.Constants", 1)]
    [InlineData(".Lib.Constants.pi", "Lib.Constants", 2)]
    [InlineData("Types.Init.SteadyState", "Lib.Types.Init", 2)]   // a literal, which no interface lists
    [InlineData("g", "Lib", 0)]                                    // a constant the package lends
    [InlineData("part.phi", "Lib.M", 0)]                           // the class's own component
    public void WhereAReferenceStarts(string reference, string scope, int qualifier)
    {
        var start = References(Graph()).Start(reference);

        Assert.Equal(scope, start?.Scope.Id);
        Assert.Equal(qualifier, start?.QualifierSegments);
    }

    [Theory]
    [InlineData("time")]
    [InlineData("Connections.branch")]     // the operator, though a Lib.Connections is in scope
    [InlineData("nothing.here")]
    [InlineData("Lib.Constants")]          // a class, not a reference through one
    [InlineData("Lib.Typo.X")]             // broken: two unknown names are no enumeration literal
    [InlineData("Types.Init.Nope.More")]   // nor is a literal with something after it
    public void SomeReferencesStartNowhere(string reference)
    {
        Assert.Null(References(Graph()).Start(reference));
    }

    [Fact]
    public void AComponentNamedLikeAClass_IsTheComponent()
    {
        // `part.phi` is M's component, even though a class Lib.part is in scope: the class's own
        // elements come first, and a reference through a component uses no class.
        var start = References(Graph()).Start("part.phi");

        Assert.Equal("Lib.M", start?.Scope.Id);
        Assert.Equal(0, start?.QualifierSegments);
    }

    [Fact]
    public void ANestedEnumerationUsedByItsLiteral_IsUsed()
    {
        // MSL's Fluid.Dissipation functions declare `type TYP = enumeration(...)` and use it only as
        // TYP.MeanTemperature: the class's own nested class, followed by a literal.
        var graph = Graph();
        graph.AddNode(new ModelNode("Lib.F", "F",
            "function F\n  output Integer y;\nprotected\n  type TYP = enumeration(A, B);\nalgorithm\n  y := Integer(TYP.A);\nend F;"));
        graph.AddNode(new ModelNode("Lib.F.TYP", "TYP", "type TYP = enumeration(A, B);"));

        var start = ClassElementResolver.ReferencesIn(graph, graph.GetNode<ModelNode>("Lib.F")!).Start("TYP.A");

        Assert.Equal("Lib.F.TYP", start?.Scope.Id);
        Assert.Equal(1, start?.QualifierSegments);
    }

    [Fact]
    public void AComponentAnEnclosingClassDoesNotLend_IsNotTakenForAClass()
    {
        // Inner's enclosing model declares `part` - not a constant, so not visible from Inner - and a
        // class Lib.part is in scope. `part.phi` is still the component's name, not a use of the class.
        var graph = Graph();
        graph.AddNode(new ModelNode("Lib.Outer", "Outer", "model Outer\n  Part part;\nend Outer;"));
        graph.AddNode(new ModelNode("Lib.Outer.Inner", "Inner", "model Inner\nend Inner;"));

        Assert.Null(ClassElementResolver.ReferencesIn(graph, graph.GetNode<ModelNode>("Lib.Outer.Inner")!).Start("part.phi"));
    }

    [Fact]
    public void ResolveSaysHowManySegmentsNamedTheClass()
    {
        var references = References(Graph());

        Assert.Equal(2, references.Resolve("Lib.Constants.pi")?.QualifierSegments);
        Assert.Equal(0, references.Resolve("part.phi")?.QualifierSegments);
    }

    [Fact]
    public void DependencyAnalysis_LinksTheClassesAReferenceGoesThrough()
    {
        var graph = Graph();
        var analyzer = new ModelAnalyzer("Lib.M", graph, new TypeResolver.AncestorCache(),
            new ClassElementResolver.InterfaceCache());
        analyzer.Visit(ModelicaParserHelper.Parse(Model));

        var used = analyzer.ReferencedModels;
        Assert.Contains("Lib.Constants", used);
        Assert.Contains("Lib.Types.Init", used);
        Assert.Contains("Lib", used);                 // for g
        Assert.Contains("Lib.Part", used);            // the declaration, as before
        Assert.DoesNotContain("Lib.part", used);      // the component is not the class
    }

    [Fact]
    public void DependencyAnalysis_DoesNotLinkABrokenReferenceToThePackageAboveIt()
    {
        // `Lib.Typo.X` names nothing. Taken as a use of Lib - the longest prefix that is a class - it
        // linked a typo to the package, and could hide a library declared in uses but never used.
        var graph = Graph();
        const string code = "model Broken\n  Real x = Lib.Typo.X;\nend Broken;";
        graph.AddNode(new ModelNode("Lib.Broken", "Broken", code));
        var analyzer = new ModelAnalyzer("Lib.Broken", graph, new TypeResolver.AncestorCache(),
            new ClassElementResolver.InterfaceCache());
        analyzer.Visit(ModelicaParserHelper.Parse(code));

        Assert.DoesNotContain("Lib", analyzer.ReferencedModels);
    }

    [Fact]
    public void TheLocator_FindsTheClassPartOfAReference_ForRename()
    {
        var graph = Graph();
        var code = "within Lib;\n" + Model;

        var sites = ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), ["Lib.Constants"]);

        // `Lib.Constants` in the first and `Constants` in the second: each span ends at the class's
        // name, so a rename rewrites it and leaves `pi` alone.
        Assert.Equal(["Lib.Constants", "Constants"],
            sites.Select(s => code.Substring(s.StartIndex, s.StopIndex - s.StartIndex + 1)));
        Assert.All(sites, s => Assert.Equal("Constants", s.Leaf.Text));
    }

    [Fact]
    public void TheLocator_FindsAnEnumerationThroughItsLiteral()
    {
        var graph = Graph();
        var code = "within Lib;\n" + Model;

        var sites = ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), ["Lib.Types.Init"]);

        // The declared type, and the literal's `Types.Init`.
        Assert.Equal(2, sites.Count);
        Assert.All(sites, s => Assert.Equal("Init", s.Leaf.Text));
    }

    [Fact]
    public void TheLocator_DoesNotTakeAComponentForAClass()
    {
        var graph = Graph();
        var code = "within Lib;\n" + Model;

        Assert.Empty(ReferenceLocator.Locate(graph, ModelicaParserHelper.Parse(code), ["Lib.part"]));
    }
}

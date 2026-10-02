using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Name lookup stops at an <c>encapsulated</c> class (MLS §5.3.1): nothing outside it - enclosing
/// packages, their imports, the root - is visible from inside it, which is why such a class imports
/// what it uses. Both resolvers, and the component-reference resolver above them, ask
/// <see cref="ClassImports.IsEncapsulated"/>, which reads it once per class.
/// </summary>
public class EncapsulatedLookupTests
{
    private static DirectedGraph Graph(string sealedCode)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Target", "Target", "model Target\nend Target;"));
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib\n  constant Real k = 1;\nend Lib;"));
        graph.AddNode(new ModelNode("Lib.Near", "Near", "model Near\nend Near;"));
        graph.AddNode(new ModelNode("Lib.Constants", "Constants",
            "package Constants\n  constant Real pi = 3.14;\nend Constants;"));
        graph.AddNode(new ModelNode("Lib.Sealed", "Sealed", sealedCode));
        graph.AddNode(new ModelNode("Lib.Sealed.Own", "Own", "model Own\nend Own;"));
        graph.AddNode(new ModelNode("Lib.Sealed.Inner", "Inner", "model Inner\nend Inner;"));
        return graph;
    }

    private const string Sealed = "encapsulated package Sealed\nend Sealed;";

    private static HashSet<string> Dependencies(DirectedGraph graph, string modelId)
    {
        var analyzer = new ModelAnalyzer(modelId, graph);
        analyzer.Visit(ModelicaParserHelper.Parse(graph.GetNode<ModelNode>(modelId)!.Definition.ModelicaCode));
        return analyzer.ReferencedModels;
    }

    [Fact]
    public void TheFlagIsReadOnce_AndReadAgainWhenTheSourceChanges()
    {
        var definition = new ModelDefinition("P", Sealed);

        Assert.True(ClassImports.IsEncapsulated(definition));
        Assert.True(definition.IsEncapsulated);   // kept, beside the imports it was read with

        definition.ModelicaCode = "package P\nend P;";
        Assert.Null(definition.IsEncapsulated);
        Assert.False(ClassImports.IsEncapsulated(definition));
    }

    [Fact]
    public void AskingForTheImportsFirst_AnswersTheFlagToo()
    {
        var definition = new ModelDefinition("P", "encapsulated package P\n  import A.B;\nend P;");

        Assert.Equal(["A.B"], ClassImports.For(definition));
        Assert.True(definition.IsEncapsulated);
    }

    [Fact]
    public void AClassThatDoesNotParse_IsNotEncapsulated()
    {
        // No source at all, so no tree: the parser recovers from most broken text.
        Assert.False(ClassImports.IsEncapsulated(new ModelDefinition("P", "")));
    }

    [Fact]
    public void FromInsideAnEncapsulatedPackage_OnlyItsOwnClassesAndImportsAreVisible()
    {
        var graph = Graph(Sealed);

        Assert.Equal("Lib.Sealed.Own", TypeResolver.Resolve(graph, "Lib.Sealed.Inner", "Own")?.Id);
        Assert.Null(TypeResolver.Resolve(graph, "Lib.Sealed.Inner", "Near"));      // an enclosing package's
        Assert.Null(TypeResolver.Resolve(graph, "Lib.Sealed.Inner", "Target"));    // the root's

        // The way in: an import, or a global name.
        var importing = Graph("encapsulated package Sealed\n  import Lib.Near;\nend Sealed;");
        Assert.Equal("Lib.Near", TypeResolver.Resolve(importing, "Lib.Sealed.Inner", "Near")?.Id);
        Assert.Equal("Target", TypeResolver.Resolve(graph, "Lib.Sealed.Inner", ".Target")?.Id);
    }

    [Fact]
    public void AnEncapsulatedClassSeesItsOwnImports_AndNothingBeyondThem()
    {
        var graph = Graph("package Sealed\nend Sealed;");
        graph.AddNode(new ModelNode("Lib.Sealed.Fn", "Fn", "encapsulated function Fn\n  import Lib.Near;\nend Fn;"));

        Assert.Equal("Lib.Near", TypeResolver.Resolve(graph, "Lib.Sealed.Fn", "Near", ["Lib.Near"])?.Id);
        Assert.Null(TypeResolver.Resolve(graph, "Lib.Sealed.Fn", "Own", ["Lib.Near"]));
    }

    [Fact]
    public void AnOrdinaryPackage_StillLetsLookupThrough()
    {
        var graph = Graph("package Sealed\nend Sealed;");

        Assert.Equal("Lib.Near", TypeResolver.Resolve(graph, "Lib.Sealed.Inner", "Near")?.Id);
        Assert.Equal("Target", TypeResolver.Resolve(graph, "Lib.Sealed.Inner", "Target")?.Id);
    }

    [Fact]
    public void DependencyAnalysis_StopsThereToo()
    {
        var graph = Graph(Sealed);
        graph.AddNode(new ModelNode("Lib.Sealed.User", "User",
            "model User\n  Own a;\n  Near b;\n  .Target c;\nend User;"));

        var used = Dependencies(graph, "Lib.Sealed.User");

        Assert.Contains("Lib.Sealed.Own", used);
        Assert.Contains("Target", used);   // a global type name keeps its dot through the analysis
        Assert.DoesNotContain("Lib.Near", used);
    }

    [Fact]
    public void DependencyAnalysis_TakesAGlobalTypeNameFromTheTop()
    {
        // Lib.Target is nearer; `.Target` is the top-level one, in a declaration and an extends clause.
        var graph = Graph("package Sealed\nend Sealed;");
        graph.AddNode(new ModelNode("Lib.Target", "Target", "model Target\nend Target;"));
        graph.AddNode(new ModelNode("Lib.User", "User", "model User\n  extends .Target;\n  .Target t;\nend User;"));

        var used = Dependencies(graph, "Lib.User");

        Assert.Contains("Target", used);
        Assert.DoesNotContain("Lib.Target", used);
    }

    [Fact]
    public void ItsBaseClass_IsNotAWayOut()
    {
        // Sealed imports and extends Lib.Base. The inheritance fallback once tried the base's whole
        // scope, so Lib's classes - and Lib.Constants by its bare name - were visible from inside an
        // encapsulated class. What a base lends is its elements; its nested classes still come through.
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("Lib", "Lib", "package Lib\nend Lib;"));
        graph.AddNode(new ModelNode("Lib.Constants", "Constants",
            "package Constants\n  constant Real pi = 3.14;\nend Constants;"));
        graph.AddNode(new ModelNode("Lib.Base", "Base", "model Base\n  package Medium\n  end Medium;\nend Base;"));
        graph.AddNode(new ModelNode("Lib.Base.Medium", "Medium", "package Medium\n  constant Real p = 1;\nend Medium;"));
        graph.AddNode(new ModelNode("Other", "Other", "package Other\nend Other;"));
        graph.AddNode(new ModelNode("Other.Sealed", "Sealed",
            "encapsulated model Sealed\n  import Lib.Base;\n  extends Base;\nend Sealed;"));
        var sealedClass = graph.GetNode<ModelNode>("Other.Sealed")!;

        Assert.Null(TypeResolver.ResolveWithInheritance(graph, "Other.Sealed", "Lib.Constants", null));
        Assert.Null(ClassElementResolver.ResolveReference(graph, sealedClass, "Lib.Constants.pi"));
        Assert.Null(ClassElementResolver.ResolveReference(graph, sealedClass, "Constants.pi"));
        Assert.Equal("Lib.Base.Medium", ClassElementResolver.ResolveReference(graph, sealedClass, "Medium.p")?.Scope.Id);
    }

    [Fact]
    public void AComponentReference_CannotReachPastIt_EvenByQualifiedName()
    {
        var graph = Graph(Sealed);
        var inner = graph.GetNode<ModelNode>("Lib.Sealed.Inner")!;

        Assert.Null(ClassElementResolver.ResolveReference(graph, inner, "k"));
        Assert.Null(ClassElementResolver.ResolveReference(graph, inner, "Constants.pi"));
        Assert.Null(ClassElementResolver.ResolveReference(graph, inner, "Lib.Constants.pi"));
        Assert.Equal("Lib.Constants",
            ClassElementResolver.ResolveReference(graph, inner, ".Lib.Constants.pi")?.Scope.Id);
    }
}

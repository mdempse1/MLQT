using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Tests;

/// <summary>
/// B446: the external clause's annotation is found by where it stands, not as the composition's
/// first annotation - which is the leading class annotation when there is one, and the trailing
/// class annotation when the clause has none of its own.
/// </summary>
public class ModelAnalyzerExternalAnnotationTests
{
    private static List<ExternalResourceInfo> Libraries(string code)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("f", "f", code));
        var analyzer = new ModelAnalyzer("f", graph);
        analyzer.Visit(ModelicaParserHelper.Parse(code));
        return analyzer.Resources.Where(r => r.ReferenceType == ResourceReferenceType.ExternalLibrary).ToList();
    }

    [Fact]
    public void ALeadingClassAnnotation_DoesNotHideTheClausesLibrary()
    {
        var library = Assert.Single(Libraries(
            "function f annotation(Inline=true); input Real x; external \"C\" g(x) annotation(Library=\"lib\"); end f;"));

        Assert.Equal("lib", library.RawPath);
    }

    [Fact]
    public void TheClassAnnotation_IsNotReadAsTheClauses()
    {
        Assert.Empty(Libraries(
            "function f input Real x; external \"C\" g(x); annotation(Library=\"notLinked\"); end f;"));
    }
}

using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// A class is measured for what <b>it</b> fails, not for what a class nested inside it fails.
///
/// <para>Every coverage dimension but two is measured through <c>ClassInterfaceExtractor</c>, which
/// walks only the outermost class. The layout dimensions and the documentation ones are measured by
/// running the rules' own visitors instead, and a rule visitor descends into a nested
/// <c>replaceable</c>/<c>redeclare</c> class — deliberately, because such a class cannot be parsed on
/// its own. Taking every finding those visitors produced recorded the parent as failing what its
/// child failed: a gap on the dashboard that no finding would ever name, and — since the nested class
/// has a node of its own and is measured in its own right — one problem costing two classes in the
/// denominator. B12 found the identical walk corrupting the Unit numbers and fixed it there;
/// backlog B79 is the same fix for the other two families.</para>
/// </summary>
public class NestedClassCoverageTests
{
    private static ModelNode Model(string id, string code) => new(id, id, code);

    private static DirectedGraph GraphOf(params ModelNode[] models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models)
            graph.AddNode(m);
        return graph;
    }

    private static (int Compliant, int Eligible) Row(LibraryMetrics metrics, string dimension)
    {
        var row = metrics.Coverage.Single(c => c.Dimension == dimension);
        return (row.Compliant, row.Eligible);
    }

    // Outer itself is tidy: no imports at all, one equation section, a description. The nested
    // replaceable class is the one with its import after a component.
    private const string OuterWithUntidyNestedClass = """
        model Outer "an outer model"
          Real x "state";
          replaceable model Inner "an inner model"
            Real y "state";
            import Modelica.Units.SI;
          end Inner;
        equation
          x = time;
        end Outer;
        """;

    private static StyleCheckingSettings LayoutRules => new()
    {
        ImportStatementsFirst = true,
        OneOfEachSection = true,
    };

    [Fact]
    public void ALayoutFailureInsideANestedClass_IsNotTheOuterClassesFailure()
    {
        var outer = Model("Outer", OuterWithUntidyNestedClass);

        var metrics = MetricsCalculator.Compute(GraphOf(outer), [outer], _ => LayoutRules);

        Assert.Equal((1, 1), Row(metrics, "Imports first"));
    }

    [Fact]
    public void TheSameFailureInTheClassItself_IsCounted()
    {
        // The guard against the test above passing because nothing is measured at all.
        var untidy = Model("Outer", """
            model Outer "an outer model"
              Real x "state";
              import Modelica.Units.SI;
            equation
              x = time;
            end Outer;
            """);

        var metrics = MetricsCalculator.Compute(GraphOf(untidy), [untidy], _ => LayoutRules);

        Assert.Equal((0, 1), Row(metrics, "Imports first"));
    }

    [Fact]
    public void ANestedClassMeasuredInItsOwnRight_StillCountsItsOwnFailure()
    {
        // The other half of the double-count: the nested class has a node of its own, so the failure
        // is reported once — against the class that has it, and only that class.
        var outer = Model("Outer", OuterWithUntidyNestedClass);
        var inner = Model("Outer.Inner", """
            model Inner "an inner model"
              Real y "state";
              import Modelica.Units.SI;
            end Inner;
            """);

        var metrics = MetricsCalculator.Compute(GraphOf(outer, inner), [outer, inner], _ => LayoutRules);

        Assert.Equal((1, 2), Row(metrics, "Imports first"));
    }

    // Outer documents itself fully; the nested class documents nothing.
    private const string OuterDocumentedNestedNot = """
        model Outer "an outer model"
          replaceable model Inner
            Real y "state";
          end Inner;
          annotation(Documentation(info="<html>outer</html>", revisions="<html>outer</html>"));
        end Outer;
        """;

    private static StyleCheckingSettings DocumentationRules => new()
    {
        ClassHasDocumentationInfo = true,
        ClassHasDocumentationRevisions = true,
    };

    [Fact]
    public void AMissingDocstringInsideANestedClass_IsNotTheOuterClassesGap()
    {
        var outer = Model("Outer", OuterDocumentedNestedNot);

        var metrics = MetricsCalculator.Compute(GraphOf(outer), [outer], _ => DocumentationRules);

        Assert.Equal((1, 1), Row(metrics, "Documentation info"));
        Assert.Equal((1, 1), Row(metrics, "Documentation revisions"));
    }

    [Fact]
    public void AClassThatReallyHasNoDocumentation_IsStillCounted()
    {
        var bare = Model("Outer", "model Outer \"an outer model\"\n  Real x;\nend Outer;");

        var metrics = MetricsCalculator.Compute(GraphOf(bare), [bare], _ => DocumentationRules);

        Assert.Equal((0, 1), Row(metrics, "Documentation info"));
    }

    [Fact]
    public void TheFilterSurvivesAFullyQualifiedName()
    {
        // The visitors have to be given the class's enclosing package or the ids they produce are
        // bare names: "Lib.Sub.Outer" comes back as "Outer", so a filter without the base package
        // would drop every finding rather than only the nested ones — and every class would read as
        // compliant with everything.
        var untidy = Model("Lib.Sub.Outer", """
            model Outer "an outer model"
              Real x "state";
              import Modelica.Units.SI;
            equation
              x = time;
            end Outer;
            """);

        var metrics = MetricsCalculator.Compute(GraphOf(untidy), [untidy], _ => LayoutRules);

        Assert.Equal((0, 1), Row(metrics, "Imports first"));
    }
}

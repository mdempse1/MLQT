using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

public class MetricsCalculatorTests
{
    private static ModelNode Model(string id, string code, string classType = "model")
        => new(id, id, code) { ClassType = classType };

    private static DirectedGraph BuildGraph(System.Collections.Generic.IEnumerable<ModelNode> models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models)
            graph.AddNode(m);
        return graph;
    }

    private static CoverageMetric Cov(LibraryMetrics m, string dim) => m.Coverage.Single(c => c.Dimension == dim);

    [Fact]
    public void CountsClassesByType()
    {
        var models = new[]
        {
            Model("A", "model A end A;", "model"),
            Model("B", "model B end B;", "model"),
            Model("P", "package P end P;", "package"),
        };
        var metrics = MetricsCalculator.Compute(BuildGraph(models), models);

        Assert.Equal(3, metrics.TotalClasses);
        Assert.Equal(2, metrics.ClassesByType["model"]);
        Assert.Equal(1, metrics.ClassesByType["package"]);
    }

    [Fact]
    public void DescriptionCoverage_CountsClassesWithADescription()
    {
        var models = new[]
        {
            Model("A", "model A \"has one\" end A;"),
            Model("B", "model B end B;"),
        };
        var d = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Description");
        Assert.Equal(1, d.Compliant);
        Assert.Equal(2, d.Eligible);
        Assert.Equal(50.0, d.Percent);
    }

    [Fact]
    public void IconCoverage_DetectsIconAnnotationInSource()
    {
        var models = new[]
        {
            Model("A", "model A\n  annotation(Icon(graphics={Rectangle(extent={{-10,-10},{10,10}})}));\nend A;"),
            Model("B", "model B end B;"),
        };
        var icon = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Icon");
        Assert.Equal(1, icon.Compliant);   // only A declares its own icon graphics
        Assert.Equal(2, icon.Eligible);
    }

    [Fact]
    public void IconCoverage_CountsIconsInheritedViaExtends()
    {
        var models = new[]
        {
            Model("Base", "package Base\n  annotation(Icon(graphics={Rectangle(extent={{-1,-1},{1,1}})}));\nend Base;", "package"),
            Model("M", "model M\n  extends Base;\nend M;"),
        };
        // Base has its own icon; M inherits it via extends → both count.
        Assert.Equal(2, Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Icon").Compliant);
    }

    [Fact]
    public void ParameterDescriptionCoverage_CountsPublicParameters()
    {
        var models = new[]
        {
            Model("A", "model A\n  parameter Real k = 1 \"gain\";\n  parameter Real t = 2;\nend A;"),
        };
        var p = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Parameter description");
        Assert.Equal(2, p.Eligible);   // two public parameters
        Assert.Equal(1, p.Compliant);  // only k has a description
    }

    [Fact]
    public void UnitCoverage_PlainReals_InlineUnit()
    {
        var models = new[]
        {
            Model("A", "model A\n  Real x(unit=\"m\");\n  Real y;\nend A;"),
        };
        var u = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Unit");
        Assert.Equal(2, u.Eligible);   // two Real-derived components
        Assert.Equal(1, u.Compliant);  // only x declares a unit
    }

    [Fact]
    public void UnitCoverage_SiTypedVariable_CountsAsUnited()
    {
        // Length carries a unit via its alias, so a Length variable is united even without unit=.
        var models = new[]
        {
            Model("Length", "type Length = Real(final quantity=\"Length\", final unit=\"m\");", "type"),
            Model("A", "model A\n  Length len;\n  Real bare;\nend A;"),
        };
        var u = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Unit");
        Assert.Equal(2, u.Eligible);   // Length var + plain Real (Length's own decl is a type, not a component)
        Assert.Equal(1, u.Compliant);  // the Length var is united; the bare Real is not
    }

    [Fact]
    public void UnitCoverage_AliasChain_ResolvesToRealUnit()
    {
        var models = new[]
        {
            Model("Length", "type Length = Real(final unit=\"m\");", "type"),
            Model("Distance", "type Distance = Length;", "type"),
            Model("A", "model A\n  Distance d;\nend A;"),
        };
        var u = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Unit");
        Assert.Equal(1, u.Eligible);   // the Distance variable
        Assert.Equal(1, u.Compliant);  // Distance -> Length -> Real(unit) → united
    }

    [Fact]
    public void UnitCoverage_IntegerAndBoolean_NotEligible()
    {
        var models = new[] { Model("A", "model A\n  Integer n;\n  Boolean b;\nend A;") };
        var u = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Unit");
        Assert.Equal(0, u.Eligible);   // neither is a Real-derived quantity
    }

    [Fact]
    public void UnitCoverage_ConnectorTypedVariable_NotEligible()
    {
        // A signal connector is Real-derived but is an interface, not a physical scalar → excluded.
        var models = new[]
        {
            Model("RealInput", "connector RealInput = input Real;", "connector"),
            Model("A", "model A\n  RealInput u;\n  Real x(unit=\"m\");\nend A;"),
        };
        var u = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Unit");
        Assert.Equal(1, u.Eligible);   // only x; the RealInput connector is excluded
        Assert.Equal(1, u.Compliant);
    }

    [Fact]
    public void EmptyEligible_IsHundredPercent()
    {
        var models = new[] { Model("A", "model A \"d\" end A;") };
        // No parameters at all → parameter-description coverage is vacuously 100%.
        Assert.Equal(100.0, Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Parameter description").Percent);
    }

    [Fact]
    public void CountsComponents()
    {
        var models = new[] { Model("A", "model A\n  Real x;\n  Real y;\n  parameter Real k = 1;\nend A;") };
        Assert.Equal(3, MetricsCalculator.Compute(BuildGraph(models), models).TotalComponents);
    }
}

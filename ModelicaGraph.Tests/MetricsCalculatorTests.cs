using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

public class MetricsCalculatorTests
{
    private static ModelNode Model(string id, string code, string classType = "model")
        => new(id, id, code) { ClassType = classType };

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
        var metrics = MetricsCalculator.Compute(models);

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
        var d = Cov(MetricsCalculator.Compute(models), "Description");
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
        var icon = Cov(MetricsCalculator.Compute(models), "Own icon");
        Assert.Equal(1, icon.Compliant);   // only A declares its own icon graphics
        Assert.Equal(2, icon.Eligible);
    }

    [Fact]
    public void ParameterDescriptionCoverage_CountsPublicParameters()
    {
        var models = new[]
        {
            Model("A", "model A\n  parameter Real k = 1 \"gain\";\n  parameter Real t = 2;\nend A;"),
        };
        var p = Cov(MetricsCalculator.Compute(models), "Parameter description");
        Assert.Equal(2, p.Eligible);   // two public parameters
        Assert.Equal(1, p.Compliant);  // only k has a description
    }

    [Fact]
    public void UnitCoverage_CountsRealsWithUnit()
    {
        var models = new[]
        {
            Model("A", "model A\n  Real x(unit=\"m\");\n  Real y;\nend A;"),
        };
        var u = Cov(MetricsCalculator.Compute(models), "Real vars w/ unit");
        Assert.Equal(2, u.Eligible);   // two Real components
        Assert.Equal(1, u.Compliant);  // only x has a unit
    }

    [Fact]
    public void EmptyEligible_IsHundredPercent()
    {
        var models = new[] { Model("A", "model A \"d\" end A;") };
        // No parameters at all → parameter-description coverage is vacuously 100%.
        Assert.Equal(100.0, Cov(MetricsCalculator.Compute(models), "Parameter description").Percent);
    }

    [Fact]
    public void CountsComponents()
    {
        var models = new[] { Model("A", "model A\n  Real x;\n  Real y;\n  parameter Real k = 1;\nend A;") };
        Assert.Equal(3, MetricsCalculator.Compute(models).TotalComponents);
    }
}

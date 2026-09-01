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
    public void ClassDescriptionCoverage_CountsClassesWithADescription()
    {
        var models = new[]
        {
            Model("A", "model A \"has one\" end A;"),
            Model("B", "model B end B;"),
        };
        var d = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Class description");
        Assert.Equal(1, d.Compliant);
        Assert.Equal(2, d.Eligible);
        Assert.Equal(50.0, d.Percent);
    }

    [Fact]
    public void ClassDescriptionCoverage_CountsAClassThatHasNoBodyToPutOneIn()
    {
        // A type alias declares its description in the trailing comment, having no composition to
        // hold one. Scoring it as undocumented put the coverage dashboard at odds with the
        // description rule, which does not flag it — and there are 684 such classes in MSL alone.
        var models = new[]
        {
            Model("Gain", "type Gain = Real(min = 0) \"a dimensionless gain\";", "type"),
            Model("Colour", "type Colour = enumeration(red, green) \"a colour\";", "type"),
            Model("Derivative", "function df = der(f, x) \"the derivative\";", "function"),
            Model("Plain", "type Plain = Real;", "type"),
        };

        var d = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Class description");

        Assert.Equal(3, d.Compliant);
        Assert.Equal(4, d.Eligible);
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
    public void ParameterDescriptionCoverage_ExcludesProtectedParametersConstantsAndVariables()
    {
        // The dimension is public parameters only — what a user of the model has to understand. A
        // protected parameter, a constant or a plain variable is outside both numerator and denominator.
        var models = new[]
        {
            Model("A",
                "model A\n" +
                "  parameter Real k = 1 \"gain\";\n" +
                "  constant Real c = 2;\n" +
                "  Real x;\n" +
                "protected\n" +
                "  parameter Real hidden = 3;\n" +
                "end A;"),
        };
        var p = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Parameter description");
        Assert.Equal(1, p.Eligible);   // only the public parameter k
        Assert.Equal(1, p.Compliant);
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
    public void DocumentationCoverage_CountsInfoAndRevisionsSeparately()
    {
        var models = new[]
        {
            Model("Both",
                "model Both\n" +
                "  annotation(Documentation(info=\"<html>x</html>\", revisions=\"<html>v1</html>\"));\n" +
                "end Both;"),
            Model("InfoOnly",
                "model InfoOnly\n" +
                "  annotation(Documentation(info=\"<html>x</html>\"));\n" +
                "end InfoOnly;"),
            Model("Neither", "model Neither end Neither;"),
        };
        var metrics = MetricsCalculator.Compute(BuildGraph(models), models);

        var info = Cov(metrics, "Documentation info");
        Assert.Equal(3, info.Eligible);
        Assert.Equal(2, info.Compliant);

        var revisions = Cov(metrics, "Documentation revisions");
        Assert.Equal(3, revisions.Eligible);
        Assert.Equal(1, revisions.Compliant);
    }

    [Fact]
    public void ConstantDescriptionCoverage_CountsPublicConstantsOnly()
    {
        // The same shape as parameter description, and one rule checks both — the dashboard used to
        // report half of what PublicParametersAndConstantsHaveDescription looks at.
        var models = new[]
        {
            Model("A",
                "model A\n" +
                "  constant Real c1 = 1 \"described\";\n" +
                "  constant Real c2 = 2;\n" +
                "  parameter Real k = 3;\n" +
                "protected\n" +
                "  constant Real hidden = 4;\n" +
                "end A;"),
        };
        var c = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Constant description");
        Assert.Equal(2, c.Eligible);    // c1 and c2; the protected constant and the parameter are not
        Assert.Equal(1, c.Compliant);
    }

    [Fact]
    public void LayoutCoverage_CountsEveryClass_CompliantUnlessTheRuleWouldFire()
    {
        var models = new[]
        {
            Model("Ordered",
                "model Ordered\n" +
                "  import Modelica.Units.SI;\n" +
                "  Real x;\n" +
                "end Ordered;"),
            Model("Late",
                "model Late\n" +
                "  Real x;\n" +
                "  import Modelica.Units.SI;\n" +
                "end Late;"),
            Model("NothingToOrder", "model NothingToOrder\n  Real x;\nend NothingToOrder;"),
        };

        var imports = Cov(MetricsCalculator.Compute(BuildGraph(models), models), "Imports first");

        Assert.Equal(3, imports.Eligible);    // every class checked, not only those with imports
        Assert.Equal(2, imports.Compliant);   // the class with nothing to order is complying
    }

    [Fact]
    public void LayoutCoverage_MeasuresSectionsAndMixedBehaviour()
    {
        var models = new[]
        {
            Model("Twice",
                "model Twice\n" +
                "  Real x;\n" +
                "public\n" +
                "  Real y;\n" +
                "end Twice;"),
            Model("Mixed",
                "model Mixed\n" +
                "  Real x;\n" +
                "equation\n" +
                "  x = 1;\n" +
                "algorithm\n" +
                "  x := 1;\n" +
                "end Mixed;"),
        };
        var metrics = MetricsCalculator.Compute(BuildGraph(models), models);

        var sections = Cov(metrics, "One of each section");
        Assert.Equal(2, sections.Eligible);
        Assert.Equal(1, sections.Compliant);   // Twice has two public sections

        var unmixed = Cov(metrics, "Equation/algorithm not mixed");
        Assert.Equal(2, unmixed.Eligible);
        Assert.Equal(1, unmixed.Compliant);    // Mixed has both an equation and an algorithm section
    }

    [Fact]
    public void WideningTheTrackedSet_ReMeasures_RatherThanReadingAnUnmeasuredFactAsAGap()
    {
        // A class measured for one repository's rules must not answer "no icon" to a report that
        // asks about icons: the fact was never measured, and a kept zero would read as a gap.
        var models = new[]
        {
            Model("A",
                "model A \"d\"\n" +
                "  annotation(Icon(graphics={Rectangle(extent={{-10,-10},{10,10}})}));\n" +
                "end A;"),
        };
        var graph = BuildGraph(models);
        var narrow = new StyleCheckingSettings { ClassHasDescription = true };
        var wide = new StyleCheckingSettings { ClassHasDescription = true, ClassHasIcon = true };

        MetricsCalculator.Compute(graph, models, _ => narrow);
        var metrics = MetricsCalculator.Compute(graph, models, _ => wide);

        var icon = Cov(metrics, "Icon");
        Assert.Equal(1, icon.Eligible);
        Assert.Equal(1, icon.Compliant);
    }
    [Fact]
    public void CountsComponents()
    {
        var models = new[] { Model("A", "model A\n  Real x;\n  Real y;\n  parameter Real k = 1;\nend A;") };
        Assert.Equal(3, MetricsCalculator.Compute(BuildGraph(models), models).TotalComponents);
    }
}

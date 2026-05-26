using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class ExperimentAnnotationCheckerTests
{
    // Subclass used to exercise the protected GetBooleanValue helper. The base
    // class only checks for the `experiment` annotation; subclasses are the
    // documented extension point for vendor-specific flags like `doNotTest=true`.
    private sealed class TestChecker : ExperimentAnnotationChecker
    {
        public bool? DoNotTest { get; private set; }
        public bool? DoNotBuild { get; private set; }

        protected override void CheckAnnotationArgument(
            string? name, modelicaParser.Element_modificationContext elemMod)
        {
            base.CheckAnnotationArgument(name, elemMod);
            if (name == "__Dymola_doNotTest")
                DoNotTest = GetBooleanValue(elemMod);
            else if (name == "__Dymola_doNotBuild")
                DoNotBuild = GetBooleanValue(elemMod);
        }

        public static TestChecker Run(string modelicaCode)
        {
            var tree = ModelicaParserHelper.Parse(modelicaCode);
            var checker = new TestChecker();
            checker.Visit(tree);
            return checker;
        }
    }

    // ── experiment annotation ───────────────────────────────────────────────

    [Fact]
    public void Check_ModelWithExperimentAnnotation_DetectsExperiment()
    {
        var code = @"
within TestLib;
model Demo
  Real x;
  annotation(experiment(StopTime=10));
end Demo;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.True(result.HasExperimentAnnotation);
    }

    [Fact]
    public void Check_ModelWithExperimentAndDocumentation_DetectsExperiment()
    {
        var code = @"
within TestLib;
model Demo ""A test model""
  Real x;
  annotation(
    Documentation(info=""<html><p>Run the experiment.</p></html>""),
    experiment(StopTime=10, NumberOfIntervals=500));
end Demo;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.True(result.HasExperimentAnnotation);
    }

    [Fact]
    public void Check_ModelWithoutExperimentAnnotation_NoExperiment()
    {
        var code = @"
within TestLib;
model Helper
  Real x;
end Helper;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.False(result.HasExperimentAnnotation);
    }

    [Fact]
    public void Check_ModelWithExperimentInDocumentationOnly_NoExperiment()
    {
        var code = @"
within TestLib;
model Helper ""This is not an experiment""
  Real x;
  annotation(Documentation(info=""<html><p>Run the experiment to see results.</p></html>""));
end Helper;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.False(result.HasExperimentAnnotation);
    }

    // ── Nested classes ──────────────────────────────────────────────────────

    [Fact]
    public void Check_NestedClassWithExperiment_DoesNotAffectOuter()
    {
        var code = @"
within TestLib;
model Outer
  model Inner
    Real x;
    annotation(experiment(StopTime=5));
  end Inner;
  Real y;
end Outer;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.False(result.HasExperimentAnnotation,
            "Experiment annotation on nested class should not be attributed to outer class");
    }

    [Fact]
    public void Check_OuterWithExperiment_NestedClassIgnored()
    {
        var code = @"
within TestLib;
model Outer
  model Inner
    Real x;
  end Inner;
  Real y;
  annotation(experiment(StopTime=5));
end Outer;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.True(result.HasExperimentAnnotation);
    }

    // ── Non-experiment class types ──────────────────────────────────────────

    [Fact]
    public void Check_Package_NoExperiment()
    {
        var code = @"
within TestLib;
package Examples
  annotation(Documentation(info=""<html><p>Example models</p></html>""));
end Examples;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.False(result.HasExperimentAnnotation);
    }

    [Fact]
    public void Check_Function_NoExperiment()
    {
        var code = @"
within TestLib;
function MyFunc
  input Real x;
  output Real y;
algorithm
  y := x;
end MyFunc;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.False(result.HasExperimentAnnotation);
    }

    // ── Realistic fluid model ───────────────────────────────────────────────

    // ── GetBooleanValue (protected helper) ──────────────────────────────────

    [Fact]
    public void GetBooleanValue_TrueLiteral_ReturnsTrue()
    {
        // The helper extracts the boolean from `flag=true` style annotation args.
        // Run it via a TestChecker subclass that exposes the value through a
        // vendor annotation it understands.
        var code = @"
within TestLib;
model Demo
  Real x;
  annotation(__Dymola_doNotTest=true, experiment(StopTime=1));
end Demo;";

        var checker = TestChecker.Run(code);

        Assert.True(checker.HasExperimentAnnotation);
        Assert.True(checker.DoNotTest);
    }

    [Fact]
    public void GetBooleanValue_FalseLiteral_ReturnsFalse()
    {
        var code = @"
within TestLib;
model Demo
  Real x;
  annotation(__Dymola_doNotTest=false);
end Demo;";

        var checker = TestChecker.Run(code);

        Assert.False(checker.DoNotTest);
    }

    [Fact]
    public void GetBooleanValue_NumericLiteral_ReturnsFalse()
    {
        // Any expression other than the literal `true` resolves to false — the
        // helper deliberately uses GetText() comparison rather than coercing.
        var code = @"
within TestLib;
model Demo
  Real x;
  annotation(__Dymola_doNotTest=1);
end Demo;";

        var checker = TestChecker.Run(code);

        Assert.False(checker.DoNotTest);
    }

    [Fact]
    public void GetBooleanValue_WhenModificationMissing_ReturnsFalse()
    {
        // An annotation argument with no `=value` modification — e.g. just a
        // bare identifier — has no modification_expression at all. The helper
        // must safely return false instead of throwing on the null access.
        var code = @"
within TestLib;
model Demo
  Real x;
  annotation(__Dymola_doNotBuild);
end Demo;";

        var checker = TestChecker.Run(code);

        Assert.False(checker.DoNotBuild);
    }

    [Fact]
    public void Check_FluidModelWithReplaceablePackage_DetectsExperiment()
    {
        var code = @"
within ModelicaFluid.Examples;
model BranchingDynamicPipes
  ""Multi-way connections of pipes with dynamic momentum balance""
  replaceable package Medium=ModelicaTests.Media.Air.MoistAir constrainedby
      ModelicaTests.Media.Interfaces.PartialMedium;
  Real x;
equation
  x = 1;
  annotation(
    Documentation(info=""<html><p>This model demonstrates dynamic pipes.</p></html>""),
    experiment(StopTime=10),
    __Dymola_Commands(file=""plotResults.mos"" ""plotResults""));
end BranchingDynamicPipes;
";
        var result = ExperimentAnnotationChecker.Check(code);

        Assert.True(result.HasExperimentAnnotation);
    }
}

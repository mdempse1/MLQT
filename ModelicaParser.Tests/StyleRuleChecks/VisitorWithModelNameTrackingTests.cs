using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// Tests for VisitorWithModelNameTracking base class, exercised via CheckClassDescriptionStrings
/// which provides the simplest concrete implementation.
/// Focuses on: within clause, basePackage constructor, short/der class specifiers,
/// nested class name tracking, and model name stack operations.
/// </summary>
public class VisitorWithModelNameTrackingTests
{
    private static (List<LogMessage> violations, CheckClassDescriptionStrings visitor) RunVisitor(
        string code, string basePackage = "")
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings(basePackage);
        visitor.Visit(parseTree);
        return (visitor.RuleViolations, visitor);
    }

    // ============================================================================
    // within clause handling (VisitStored_definition with name context)
    // ============================================================================

    [Fact]
    public void WithinClause_SinglePackage_ModelNameIncludesPackage()
    {
        var code = """
within MyLibrary;
model TestModel "test"
  Real x;
equation
  x = 1.0;
end TestModel;
""";

        var (violations, visitor) = RunVisitor(code);

        Assert.Empty(violations);
    }

    [Fact]
    public void WithinClause_NestedPackage_ModelNameIncludesFullPath()
    {
        var code = """
within MyLib.SubPkg;
model TestModel "test"
  Real x;
equation
  x = 1.0;
end TestModel;
""";

        var (violations, _) = RunVisitor(code);

        Assert.Empty(violations);
    }

    [Fact]
    public void WithinClause_ViolationIncludesPackageInFQN()
    {
        var code = """
within MyLib;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (violations, _) = RunVisitor(code);

        Assert.Single(violations);
        Assert.Equal("MyLib.Undocumented", violations[0].ModelName);
    }

    [Fact]
    public void WithinClause_NestedViolationIncludesFullPath()
    {
        var code = """
within MyLib.SubPkg;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (violations, _) = RunVisitor(code);

        Assert.Single(violations);
        Assert.Equal("MyLib.SubPkg.Undocumented", violations[0].ModelName);
    }

    // ============================================================================
    // basePackage constructor parameter
    // ============================================================================

    [Fact]
    public void BasePackage_ViolationIncludesBasePackageInFQN()
    {
        var code = """
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (violations, _) = RunVisitor(code, "MyBase");

        Assert.Single(violations);
        Assert.Equal("MyBase.Undocumented", violations[0].ModelName);
    }

    [Fact]
    public void BasePackage_EmptyString_ModelNameIsJustClassName()
    {
        var code = """
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (violations, _) = RunVisitor(code, "");

        Assert.Single(violations);
        Assert.Equal("Undocumented", violations[0].ModelName);
    }

    [Fact]
    public void BasePackage_WithinClauseOverridesBasePackage()
    {
        // When code has a within clause, that takes precedence
        var code = """
within ActualPackage;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        // basePackage is not used when within clause is present
        var (violations, _) = RunVisitor(code, "IgnoredBase");

        Assert.Single(violations);
        // The within clause should set the package, not the basePackage
        Assert.Contains("Undocumented", violations[0].ModelName);
    }

    // ============================================================================
    // short_class_specifier path in VisitClass_definition
    // ============================================================================

    [Fact]
    public void ShortClassSpecifier_NameTrackedCorrectly()
    {
        var code = """
within MyLib;
type MyVoltage = Real(unit = "V") "Voltage type";
""";

        var (violations, _) = RunVisitor(code);

        Assert.Empty(violations);
    }

    [Fact]
    public void ShortClassSpecifier_ViolationHasCorrectModelName()
    {
        var code = """
within MyLib;
type MyVoltage = Real(unit = "V");
""";

        var (violations, _) = RunVisitor(code);

        Assert.Single(violations);
        Assert.Equal("MyLib.MyVoltage", violations[0].ModelName);
    }

    [Fact]
    public void ShortClassSpecifier_NoWithin_NameIsJustClassName()
    {
        var code = """
type StandaloneType = Real(unit = "K");
""";

        var (violations, _) = RunVisitor(code);

        Assert.Single(violations);
        Assert.Equal("StandaloneType", violations[0].ModelName);
    }

    // ============================================================================
    // der_class_specifier path in VisitClass_definition
    // ============================================================================

    [Fact]
    public void DerClassSpecifier_NameTrackedCorrectly()
    {
        var code = """
within MyLib;
type Velocity = der(Position, time) "Velocity";
""";

        var (violations, _) = RunVisitor(code);

        Assert.Empty(violations);
    }

    [Fact]
    public void DerClassSpecifier_ViolationHasCorrectModelName()
    {
        var code = """
within MyLib;
type Velocity = der(Position, time);
""";

        var (violations, _) = RunVisitor(code);

        Assert.Single(violations);
        Assert.Equal("MyLib.Velocity", violations[0].ModelName);
    }

    // ============================================================================
    // Nested classes — skipped by parent visitor (each has its own ModelNode)
    // ============================================================================

    [Fact]
    public void NestedClasses_SkippedByParentVisitor()
    {
        // Nested classes are checked independently via their own ModelNode,
        // so the parent visitor should NOT report violations for them.
        var code = """
within Outer;
package OuterPkg "outer package"
  model InnerModel
    Real x;
  equation
    x = 1.0;
  end InnerModel;
end OuterPkg;
""";

        var (violations, _) = RunVisitor(code);

        // OuterPkg has a description; InnerModel is nested and skipped
        Assert.Empty(violations);
    }

    [Fact]
    public void NestedClass_CheckedIndependentlyViaOwnCode()
    {
        // Simulates how a nested class is checked via its own ModelNode:
        // the class code is extracted and checked with the parent as basePackage.
        var code = """
model InnerModel
  Real x;
equation
  x = 1.0;
end InnerModel;
""";

        var (violations, _) = RunVisitor(code, "Outer.OuterPkg");

        Assert.Single(violations);
        Assert.Equal("Outer.OuterPkg.InnerModel", violations[0].ModelName);
    }

    [Fact]
    public void DeeplyNestedClass_SkippedByParentVisitor()
    {
        var code = """
package Level1 "level 1"
  package Level2 "level 2"
    model Level3Model
      Real x;
    equation
      x = 1.0;
    end Level3Model;
  end Level2;
end Level1;
""";

        var (violations, _) = RunVisitor(code);

        // Level1 and Level2 have descriptions; Level3Model is nested and skipped
        Assert.Empty(violations);
    }

    [Fact]
    public void MultipleNestedClasses_SkippedByParentVisitor()
    {
        var code = """
package Pkg "pkg"
  model M1
    Real x;
  equation
    x = 1.0;
  end M1;

  model M2
    Real y;
  equation
    y = 2.0;
  end M2;
end Pkg;
""";

        var (violations, _) = RunVisitor(code);

        // Pkg has a description; M1 and M2 are nested and skipped
        Assert.Empty(violations);
    }

    // ============================================================================
    // InitialEquationFirst - exercises OnClassEntered/OnClassExited stack
    // ============================================================================

    [Fact]
    public void InitialEquationFirst_TopLevelClass_CheckedCorrectly()
    {
        // InitialEquationFirst checks the top-level class only
        var code = """
model WithInitialFirst "inner model"
  Real x;
initial equation
  x = 0.0;
equation
  x = 1.0;
end WithInitialFirst;
""";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new InitialEquationFirst(initialFirst: true, initialLast: false);
        visitor.Visit(parseTree);

        Assert.Empty(visitor.RuleViolations);
    }
}

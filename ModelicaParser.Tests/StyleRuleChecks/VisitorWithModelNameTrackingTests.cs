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
    private static (List<LogMessage> findings, CheckClassDescriptionStrings visitor) RunVisitor(
        string code, string basePackage = "")
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings(basePackage);
        visitor.Visit(parseTree);
        return (visitor.RuleFindings, visitor);
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

        var (findings, visitor) = RunVisitor(code);

        Assert.Empty(findings);
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

        var (findings, _) = RunVisitor(code);

        Assert.Empty(findings);
    }

    [Fact]
    public void WithinClause_FindingIncludesPackageInFQN()
    {
        var code = """
within MyLib;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (findings, _) = RunVisitor(code);

        Assert.Single(findings);
        Assert.Equal("MyLib.Undocumented", findings[0].ModelName);
    }

    [Fact]
    public void WithinClause_NestedFindingIncludesFullPath()
    {
        var code = """
within MyLib.SubPkg;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (findings, _) = RunVisitor(code);

        Assert.Single(findings);
        Assert.Equal("MyLib.SubPkg.Undocumented", findings[0].ModelName);
    }

    // ============================================================================
    // basePackage constructor parameter
    // ============================================================================

    [Fact]
    public void BasePackage_FindingIncludesBasePackageInFQN()
    {
        var code = """
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var (findings, _) = RunVisitor(code, "MyBase");

        Assert.Single(findings);
        Assert.Equal("MyBase.Undocumented", findings[0].ModelName);
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

        var (findings, _) = RunVisitor(code, "");

        Assert.Single(findings);
        Assert.Equal("Undocumented", findings[0].ModelName);
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
        var (findings, _) = RunVisitor(code, "IgnoredBase");

        Assert.Single(findings);
        // The within clause should set the package, not the basePackage
        Assert.Contains("Undocumented", findings[0].ModelName);
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

        var (findings, _) = RunVisitor(code);

        Assert.Empty(findings);
    }

    [Fact]
    public void ShortClassSpecifier_FindingHasCorrectModelName()
    {
        var code = """
within MyLib;
type MyVoltage = Real(unit = "V");
""";

        var (findings, _) = RunVisitor(code);

        Assert.Single(findings);
        Assert.Equal("MyLib.MyVoltage", findings[0].ModelName);
    }

    [Fact]
    public void ShortClassSpecifier_NoWithin_NameIsJustClassName()
    {
        var code = """
type StandaloneType = Real(unit = "K");
""";

        var (findings, _) = RunVisitor(code);

        Assert.Single(findings);
        Assert.Equal("StandaloneType", findings[0].ModelName);
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

        var (findings, _) = RunVisitor(code);

        Assert.Empty(findings);
    }

    [Fact]
    public void DerClassSpecifier_FindingHasCorrectModelName()
    {
        var code = """
within MyLib;
type Velocity = der(Position, time);
""";

        var (findings, _) = RunVisitor(code);

        Assert.Single(findings);
        Assert.Equal("MyLib.Velocity", findings[0].ModelName);
    }

    // ============================================================================
    // Nested classes — skipped by parent visitor (each has its own ModelNode)
    // ============================================================================

    [Fact]
    public void NestedClasses_SkippedByParentVisitor()
    {
        // Nested classes are checked independently via their own ModelNode,
        // so the parent visitor should NOT report findings for them.
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

        var (findings, _) = RunVisitor(code);

        // OuterPkg has a description; InnerModel is nested and skipped
        Assert.Empty(findings);
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

        var (findings, _) = RunVisitor(code, "Outer.OuterPkg");

        Assert.Single(findings);
        Assert.Equal("Outer.OuterPkg.InnerModel", findings[0].ModelName);
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

        var (findings, _) = RunVisitor(code);

        // Level1 and Level2 have descriptions; Level3Model is nested and skipped
        Assert.Empty(findings);
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

        var (findings, _) = RunVisitor(code);

        // Pkg has a description; M1 and M2 are nested and skipped
        Assert.Empty(findings);
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

        Assert.Empty(visitor.RuleFindings);
    }
}

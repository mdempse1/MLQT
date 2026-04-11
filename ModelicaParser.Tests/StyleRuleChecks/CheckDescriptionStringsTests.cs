using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class CheckDescriptionStringsTests
{
    private List<LogMessage> CheckRule(string code, bool everyClass, bool everyPublicParameter, bool everyPublicConstant, bool everyPublicComponent, bool alsoProtected)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings();
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    [Fact]
    public void ClassWithDescription_Correct()
    {
        // Arrange
        var code = """
model SimpleModel "a description"
  Real x;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void ClassWithoutDescription_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void ShortClassWithDescription_Correct()
    {
        // Arrange - short_class_specifier with description
        var code = """
type Voltage = Real(unit = "V") "Voltage in volts";
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void ShortClassWithoutDescription_Wrong()
    {
        // Arrange - short_class_specifier missing description
        var code = """
type Voltage = Real(unit = "V");
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void EnumShortClassWithDescription_Correct()
    {
        // Arrange - enumeration short class with description
        var code = """
type Color = enumeration(Red, Green, Blue) "RGB colors";
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void EnumShortClassWithoutDescription_Wrong()
    {
        // Arrange - enumeration short class without description
        var code = """
type Color = enumeration(Red, Green, Blue);
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void DerClassWithDescription_Correct()
    {
        // Arrange - der_class_specifier with description
        var code = """
type Velocity = der(Position, time) "Velocity in m/s";
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void DerClassWithoutDescription_Wrong()
    {
        // Arrange - der_class_specifier missing description
        var code = """
type Velocity = der(Position, time);
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void MultipleClasses_AllWithDescriptions_Correct()
    {
        // Arrange - package with nested classes, all having descriptions
        var code = """
package TestPackage "test package"
  model Inner "inner model"
    Real x;
  equation
    x = 1.0;
  end Inner;
end TestPackage;
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void NestedClass_SkippedByParentVisitor()
    {
        // Nested classes are checked independently via their own ModelNode,
        // so the parent visitor should not report violations for them.
        var code = """
package TestPackage "test package"
  model InnerWithout
    Real x;
  equation
    x = 1.0;
  end InnerWithout;
end TestPackage;
""";

        // Act
        var ruleViolations = CheckRule(code, true, false, false, false, false);

        // Assert - InnerWithout is nested and skipped; TestPackage has a description
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void NestedClass_CheckedIndependentlyViaOwnCode()
    {
        // Simulates checking a nested class via its own ModelNode
        var code = """
model InnerWithout
  Real x;
equation
  x = 1.0;
end InnerWithout;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Single(ruleViolations);
    }

    [Fact]
    public void WithBasePackage_TracksFQN()
    {
        // Arrange - use basePackage constructor parameter to test that path
        var code = """
model SimpleModel "a description"
  Real x;
equation
  x = 1.0;
end SimpleModel;
""";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings("MyBasePackage");
        visitor.Visit(parseTree);

        // Assert - no violations since description is present
        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void WithinClause_TracksFQN()
    {
        // Arrange - within clause sets the package context
        var code = """
within MyLibrary;
model SimpleModel "a description"
  Real x;
equation
  x = 1.0;
end SimpleModel;
""";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings();
        visitor.Visit(parseTree);

        // Assert - no violations
        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void WithinClause_MissingDescription_ViolationHasFQN()
    {
        // Arrange
        var code = """
within MyLib;
model Undocumented
  Real x;
equation
  x = 1.0;
end Undocumented;
""";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new CheckClassDescriptionStrings();
        visitor.Visit(parseTree);

        // Assert - one violation with correct FQN
        Assert.Single(visitor.RuleViolations);
        Assert.Contains("Undocumented", visitor.RuleViolations[0].ModelName);
    }

    [Fact]
    public void ReplaceableClass_WithDescription()
    {
        // Replaceable classes are checked as elements within their parent class
        var code = """
model Container "A container"
  replaceable model thisModel = Library.Model "Replaceable model description";
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void ReplaceableClass_NoDescription()
    {
        // Replaceable classes are checked as elements within their parent class
        var code = """
model Container "A container"
  replaceable model thisModel = Library.Model;
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Single(ruleViolations);
    }

    [Fact]
    public void ReplaceableClassConstrainingClause_WithDescription()
    {
        // Description on constraining clause comment counts for the class
        var code = """
model Container "A container"
  replaceable model thisModel = Library.Model
    constrainedby Library.BaseClass
    "Replaceable model description";
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void ReplaceableClassConstrainingClause_NoDescription()
    {
        // Replaceable class with constraining clause but no description should flag
        var code = """
model Container "A container"
  replaceable model thisModel = Library.Model
    constrainedby Library.BaseClass;
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Single(ruleViolations);
    }

    [Fact]
    public void RedeclareClass_WithDescription()
    {
        // Redeclare classes are also non-standalone and checked within parent
        var code = """
model Container "A container"
  redeclare model thisModel = Library.Model "Redeclared model";
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void RedeclareClass_NoDescription()
    {
        var code = """
model Container "A container"
  redeclare model thisModel = Library.Model;
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Single(ruleViolations);
    }


    [Fact]
    public void ElementRedeclareClass_NoDescription_IsValid()
    {
        var code = """
model Container "A container"
  extends BaseClass(redeclare model thisModel = Library.Model);
end Container;
""";

        var ruleViolations = CheckRule(code, true, false, false, false, false);

        Assert.Empty(ruleViolations);
    }
}

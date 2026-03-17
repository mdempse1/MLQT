using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class OneOfEachSectionTests
{
  private List<LogMessage> CheckRule(string code, bool allowEquationAndAlgorithm)
  {
    var parseTree = ModelicaParserHelper.Parse(code);
    var visitor = new OneOfEachSection(true, true, true, true, allowEquationAndAlgorithm);
    visitor.Visit(parseTree);
    return visitor.RuleViolations;
  }

  [Fact]
  public void OneOfEachSectionEq_Correct()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial equation
  y=1;
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Empty(ruleViolations);
  }

  [Fact]
  public void OneOfEachSectionAlgo_Correct()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial algorithm
  y=1;
algorithm
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Empty(ruleViolations);
  }

  [Fact]
  public void OneOfEachSectionBoth_Correct()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial equation
  y=1;
equation
  x=2;
initial algorithm
  y=1;
algorithm
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, true);

    // Assert
    Assert.Empty(ruleViolations);
  }

  [Fact]
  public void OneOfEachSectionBoth1_NotAllowed()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial equation
  y=1;
equation
  x=2;
algorithm
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

  [Fact]
  public void OneOfEachSectionBoth2_NotAllowed()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial equation
  y=1;
equation
  x=2;
initial algorithm
  y=1;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

  [Fact]
  public void MultiplePublicSections1_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
public
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }


  [Fact]
  public void MultiplePublicSections2_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
public
  Real y "description here";
public
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }


  [Fact]
  public void MultipleProtectedSections1_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
protected
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }


  [Fact]
  public void MultipleProtectedSections2_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
protected
  Real x "description here";
public
  Real y "description here";
protected
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }


  [Fact]
  public void MultipleEquationSections_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
equation
  x=2;
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

  [Fact]
  public void MultipleAlgorithmSections_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
algorithm
  x=2;
algorithm
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

  [Fact]
  public void MultipleInitialEquationSections_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
initial equation
  x=2;
initial equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

  [Fact]
  public void MultipleInitialAlgorithmSections_Wrong()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
initial algorithm
  x=2;
initial algorithm
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Single(ruleViolations);
  }

    [Fact]
  public void TwoPublicComponents_Correct()
  {
    // Arrange
    var code = """
model SimpleModel
public
  Real x "description here";
protected
  Real y "description here";
initial equation
  y=1;
equation
  x=2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.Empty(ruleViolations);
  }

  [Fact]
  public void InitialEquationAndInitialAlgorithm_NotAllowed_ReportsViolation()
  {
    // Covers CheckEquationSection: tracker.InitialAlgorithmSection == 1 && !_allowEquationAndAlgorithm
    // Arrange
    var code = """
model SimpleModel
  Real x;
  Real y;
initial algorithm
  y := 0;
initial equation
  x = 0;
equation
  x = 1;
end SimpleModel;
""";

    // Act - allowEquationAndAlgorithm = false, so mixing is not allowed
    var ruleViolations = CheckRule(code, false);

    // Assert - mixing initial equation and initial algorithm should raise a violation
    Assert.NotEmpty(ruleViolations);
  }

  [Fact]
  public void InitialAlgorithmAndInitialEquation_NotAllowed_ReportsViolation()
  {
    // Covers CheckAlgorithmSection: tracker.InitialEquationSection == 1 && !_allowEquationAndAlgorithm
    // Arrange
    var code = """
model SimpleModel
  Real x;
  Real y;
initial equation
  x = 0;
initial algorithm
  y := 0;
algorithm
  y := 1;
end SimpleModel;
""";

    // Act - allowEquationAndAlgorithm = false
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.NotEmpty(ruleViolations);
  }

  [Fact]
  public void InitialEquationAndInitialAlgorithm_Allowed_NoViolation()
  {
    // Arrange
    var code = """
model SimpleModel
  Real x;
  Real y;
initial equation
  x = 0;
initial algorithm
  y := 0;
equation
  x = 1;
end SimpleModel;
""";

    // Act - allowEquationAndAlgorithm = true
    var ruleViolations = CheckRule(code, true);

    // Assert - allowed, so no violations from mixing
    Assert.Empty(ruleViolations);
  }

  [Fact]
  public void MultipleInitialEquationSections_ReportsViolation()
  {
    // Covers CheckEquationSection: tracker.InitialEquationSection == 1 && _oneInitialEquationSection
    // Arrange
    var code = """
model SimpleModel
  Real x;
initial equation
  x = 0;
initial equation
  x = 0;
equation
  x = 1;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, true);

    // Assert
    Assert.NotEmpty(ruleViolations);
  }

  [Fact]
  public void MultipleInitialAlgorithmSections_ReportsViolation()
  {
    // Covers CheckAlgorithmSection: tracker.InitialAlgorithmSection == 1 && _oneInitialEquationSection
    // Arrange
    var code = """
model SimpleModel
  Real y;
initial algorithm
  y := 0;
initial algorithm
  y := 0;
algorithm
  y := 1;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, true);

    // Assert
    Assert.NotEmpty(ruleViolations);
  }

  [Fact]
  public void MultipleAlgorithmSections_ReportsViolation()
  {
    // Covers CheckAlgorithmSection: tracker.AlgorithmSection == 1 && _oneEquationSection
    // Arrange
    var code = """
model SimpleModel
  Real y;
algorithm
  y := 1;
algorithm
  y := 2;
end SimpleModel;
""";

    // Act
    var ruleViolations = CheckRule(code, true);

    // Assert
    Assert.NotEmpty(ruleViolations);
  }

  [Fact]
  public void AlgorithmBeforeEquation_NotAllowed_ReportsViolation()
  {
    // Covers CheckEquationSection: tracker.AlgorithmSection == 1 && !_allowEquationAndAlgorithm (lines 117-119)
    // This path only fires when algorithm appears BEFORE equation.
    var code = """
model SimpleModel
  Real x;
  Real y;
algorithm
  y := 1;
equation
  x = 2;
end SimpleModel;
""";

    // Act - mixing not allowed
    var ruleViolations = CheckRule(code, false);

    // Assert
    Assert.NotEmpty(ruleViolations);
  }
}
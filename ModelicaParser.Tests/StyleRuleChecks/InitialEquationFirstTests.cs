using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class InitialEquationFirstTests
{
    private List<LogMessage> CheckRule(string code, bool first)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new InitialEquationFirst(first);
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    [Fact]
    public void InitialEquationFirst_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
initial equation
  x = 1;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void InitialEquationAfter_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  x=2;
initial equation
  x = 1;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, false);

        // Assert
        Assert.Empty(ruleViolations);
    }    


    [Fact]
    public void InitialAlgorithmFirst_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
initial algorithm
  x = 1;
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
    public void InitialAlgorithmAfter_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
algorithm
  x=2;
initial algorithm
  x = 1;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, false);

        // Assert
        Assert.Empty(ruleViolations);
    }    


    [Fact]
    public void InitialEquationBeforeAlgorithm_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
initial equation
  x = 1;
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
    public void InitialEquationAfterAlgorithm_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
algorithm
  x=2;
initial equation
  x = 1;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, false);

        // Assert
        Assert.Empty(ruleViolations);
    }    


    [Fact]
    public void InitialAlgorithmBeforeEquation_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
initial algorithm
  x = 1;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void InitialAlgorithmAfterEquation_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  x=2;
initial algorithm
  x = 1;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, false);

        // Assert
        Assert.Empty(ruleViolations);
    }    

    [Fact]
    public void InitialEquationFirst_FoundSecond()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  x=2;
initial equation
  x = 1;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Single(ruleViolations);
    }    


    [Fact]
    public void InitialEquationLast_FoundFirst()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
initial equation
  x = 1;
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
    public void InitialEquationLast_FoundMiddle()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  x=2;
initial equation
  x = 1;
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
    public void InitialAlgorithmFirst_ViolationWhenInitialLast()
    {
        // Arrange - initial algorithm appears first but rule requires it last
        var code = """
model SimpleModel
  Real x;
initial algorithm
  x := 1;
algorithm
  x := 2;
end SimpleModel;
""";

        // Act - false means initial should be LAST
        var ruleViolations = CheckRule(code, false);

        // Assert - initial algorithm first violates "initial last" rule
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void AlgorithmAfterInitialAlgorithm_ViolationWhenInitialFirst()
    {
        // Arrange - algorithm after initial algorithm when rule requires initial first
        var code = """
model SimpleModel
  Real x;
algorithm
  x := 2;
initial algorithm
  x := 1;
end SimpleModel;
""";

        // Act - true means initial should be FIRST
        var ruleViolations = CheckRule(code, true);

        // Assert - initial algorithm last violates "initial first" rule
        Assert.Single(ruleViolations);
    }

    [Fact]
    public void InitialAlgorithmLast_WithPreviousAlgorithm_ViolationWhenInitialFirst()
    {
        // Covers VisitAlgorithm_section when _foundInitialSection && !_initialFirst
        // Each non-initial algorithm section after an initial section fires a violation
        var code = """
model SimpleModel
  Real x;
initial algorithm
  x := 0;
algorithm
  x := 1;
algorithm
  x := 2;
end SimpleModel;
""";

        // Act - false means initial should be LAST
        var ruleViolations = CheckRule(code, false);

        // Assert - two violations: one per non-initial algorithm after the initial section
        Assert.Equal(2, ruleViolations.Count);
    }

}
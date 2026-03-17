using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class MixConnectionsAndEquationsTests
{
    private List<LogMessage> CheckRule(string code, bool first)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new MixConnectionsAndEquations();
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    [Fact]
    public void OnlyEquations_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  x=2;
  y=3;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void OnlyConnections_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  connect(a.x, b.y);
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }


    [Fact]
    public void MixEquationAndConnections_NotAllowed()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  connect(a.x, b.y);
  x = 2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Single(ruleViolations);
    }   

    [Fact]
    public void ForLoopAndConnections_Allowed()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  for i in 1:10 loop
    connect(a.x, b.y);
  end for;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }       

    [Fact]
    public void IfThenElseAndConnections_Allowed()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  if x == 1 then
    connect(a.x, b.y);
  else
    connect(a.y, b.z);
  end if;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }    

    [Fact]
    public void WhenAndConnections_Allowed()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  when x == 1 then
    connect(a.x, b.y);
  end when;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }      


    [Fact]
    public void AssertEquations_Allowed()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
equation
  assert(x > 0, "Something");
  connect(a.x, b.y);
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void InitialEquation_IsNotCheckedForMixing()
    {
        // Covers VisitEquation_section when first child is "initial" (_isInitial = true path).
        // Connections in initial equation sections are not flagged.
        var code = """
model WithInitialEquation
  Real x;
initial equation
  x = 0;
equation
  connect(a.x, b.y);
end WithInitialEquation;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert - initial equation connect is not checked, regular equation only has connect
        Assert.Empty(ruleViolations);
    }
  }
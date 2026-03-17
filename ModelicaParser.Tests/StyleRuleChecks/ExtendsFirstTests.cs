using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;


namespace ModelicaParser.Tests.StyleRuleChecks;

public class ExtendsFirstTests
{
    private List<LogMessage> CheckRule(string code, bool first)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new ExtendsClausesAtTop(first);
        visitor.Visit(parseTree);
        return visitor.RuleViolations;
    }

    [Fact]
    public void ExtendsFirst_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  extends BaseClass;
  Real x "description here";
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
    public void ExtendsFirst_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
  extends BaseClass;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("This class does not have its extends clauses at the top of the class",ruleViolations[0].Summary);
    }    


    [Fact]
    public void NotAllExtendsFirst_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  extends BaseClass;
  Real x "description here";
  extends BaseClass2;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("This class does not have its extends clauses at the top of the class",ruleViolations[0].Summary);
    }

    [Fact]
    public void ExtendsBeforeImport_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  extends BaseClass;
  import Modelica.Units;
  Real x "description here";
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
    public void ExtendsBeforeImport_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  import Modelica.Units;
  extends BaseClass;
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, true);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("This class does not have its extends clauses at the top of the class", ruleViolations[0].Summary);
    }

    [Fact]
    public void ExtendsAfterImport_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  import Modelica.Units;
  extends BaseClass;
  Real x "description here";
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
    public void ExtendsAfterImport_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  extends BaseClass;
  import Modelica.Units;
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code, false);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("This class does not have its import statements before its extends clauses", ruleViolations[0].Summary);
    }    
}
using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;


namespace ModelicaParser.Tests.StyleRuleChecks;

public class ImportFirstTests
{
    private List<LogMessage> CheckRule(string code, bool first)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new ImportStatementsFirst(first);
        visitor.Visit(parseTree);
        return visitor.RuleFindings;
    }

    [Fact]
    public void ImportFirst_Correct()
    {
        // Arrange
        var code = """
model SimpleModel
  import Modelica.Units;
  Real x "description here";
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void ImportFirst_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
  import Modelica.Units;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code, true);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("This class does not have its import statements before the rest of the class definition",ruleFindings[0].Summary);
    }    


    [Fact]
    public void NotAllImportsFirst_Wrong()
    {
        // Arrange
        var code = """
model SimpleModel
  import Modelica.Units;
  Real x "description here";
  import Modelica.Units.SI;
equation
  x=2;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code, true);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("This class does not have its import statements before the rest of the class definition",ruleFindings[0].Summary);
    }

    [Fact]
    public void ImportBeforeExtends_Correct()
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
        var ruleFindings = CheckRule(code, true);

        // Assert
        Assert.Empty(ruleFindings);
    }    


    [Fact]
    public void ImportBeforeExtends_Wrong()
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
        var ruleFindings = CheckRule(code, true);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("This class does not have its import statements before the rest of the class definition",ruleFindings[0].Summary);
    }

    [Fact]
    public void ImportAfterExtends_Correct()
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
        var ruleFindings = CheckRule(code, false);

        // Assert
        Assert.Empty(ruleFindings);
    }    

    [Fact]
    public void ImportAfterExtends_Wrong()
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
        var ruleFindings = CheckRule(code, false);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("This class does not have its extends clauses before the import statements",ruleFindings[0].Summary);
    }    
}
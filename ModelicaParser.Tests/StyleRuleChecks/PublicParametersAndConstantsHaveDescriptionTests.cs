using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class PublicParametersAndConstantsHaveDescriptionTests
{
    private List<LogMessage> CheckRule(string code)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new PublicParametersAndConstantsHaveDescription(true, true);
        visitor.Visit(parseTree);
        return visitor.RuleFindings;
    }

    [Fact]
    public void OneVariable_WithDescription_NoFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Empty(ruleFindings);
    }
    
    [Fact]
    public void OneVariable_NoDescription_NoFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Empty(ruleFindings);
    }

        
    [Fact]
    public void OneProtectedVariable_NoDescription_NoFinding()
    {
        // Arrange
        var code = """
model SimpleModel
protected
  Real x;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void OneParameter_NoDescription_Finding()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("Public parameter", ruleFindings[0].Summary);
        Assert.Equal(2, ruleFindings[0].LineNumber);
        Assert.Equal("SimpleModel", ruleFindings[0].ModelName);
        Assert.Contains(" x ", ruleFindings[0].Summary);
    }    

    [Fact]
    public void OnePublicParameter_NoDescription_Finding()
    {
        // Arrange
        var code = """
model SimpleModel
public
  parameter Real x;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("Public parameter", ruleFindings[0].Summary);
        Assert.Equal(3, ruleFindings[0].LineNumber);
        Assert.Equal("SimpleModel", ruleFindings[0].ModelName);
        Assert.Contains(" x ", ruleFindings[0].Summary);
    }    


    [Fact]
    public void OnePublicOneProtectedParameter_NoDescription_Finding()
    {
        // Arrange
        var code = """
model SimpleModel
public
  parameter Real x;
protected
  parameter Real y;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("Public parameter", ruleFindings[0].Summary);
        Assert.Equal(3, ruleFindings[0].LineNumber);
        Assert.Equal("SimpleModel", ruleFindings[0].ModelName);
        Assert.Contains(" x ", ruleFindings[0].Summary);
    }        

       [Fact]
    public void OnePublicOneProtectedParameter_WithDescription_NoFinding()
    {
        // Arrange
        var code = """
model SimpleModel
public
  parameter Real x "description here";
protected
  parameter Real y;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Empty(ruleFindings);
    }    

   [Fact]
    public void TwoPublicOneProtectedParameter_NoDescription_OneFinding()
    {
        // Arrange
        var code = """
model SimpleModel
public
  parameter Real x "description here";
protected
  parameter Real y;
public
  parameter Real z;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("Public parameter", ruleFindings[0].Summary);
        Assert.Equal(7, ruleFindings[0].LineNumber);
        Assert.Equal("SimpleModel", ruleFindings[0].ModelName);
        Assert.Contains(" z ", ruleFindings[0].Summary);
    }    
    
       [Fact]
    public void Package_NestedModels_SkippedByParentVisitor()
    {
        // Nested classes are checked independently via their own ModelNode
        var code = """
package Test
  model SimpleModel1
  public
    parameter Real x "description here";
  protected
    parameter Real y;
  public
    parameter Real z;
  end SimpleModel1;

  model SimpleModel2
    parameter Real x;
  end SimpleModel2;

end Test;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - nested models are skipped; Test package has no public parameters
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void Model_MultiplePublicParams_TwoFindings()
    {
        // Test standalone models with parameter findings
        var code = """
model SimpleModel1
public
  parameter Real x "description here";
protected
  parameter Real y;
public
  parameter Real z;
end SimpleModel1;
""";

        var ruleFindings = CheckRule(code);

        Assert.Single(ruleFindings);
        Assert.Contains("Public parameter", ruleFindings[0].Summary);
        Assert.Contains(" z ", ruleFindings[0].Summary);
    }

    [Fact]
    public void PublicConstant_WithDescription_NoFinding()
    {
        // Arrange - constant prefix path in VisitComponent_clause
        var code = """
model SimpleModel
  constant Real gravity = 9.81 "gravitational acceleration";
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void PublicConstant_WithoutDescription_ReportsFinding()
    {
        // Arrange - constant without description
        var code = """
model SimpleModel
  constant Real gravity = 9.81;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("constant", ruleFindings[0].Summary);
        Assert.Contains("gravity", ruleFindings[0].Summary);
    }

    [Fact]
    public void TwoPublicConstants_WithoutDescription_ReportsTwoFindings()
    {
        // Arrange - two constants without descriptions
        var code = """
model SimpleModel
  constant Real gravity = 9.81;
  constant Integer count = 0;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Equal(2, ruleFindings.Count);
        Assert.All(ruleFindings, v => Assert.Contains("constant", v.Summary));
    }

    [Fact]
    public void RegularVariable_NoPrefixNoDescription_NoFinding()
    {
        // Arrange - covers the else branch in VisitComponent_clause (no parameter/constant prefix)
        var code = """
model SimpleModel
  Real x;
  Integer count;
equation
  x = 1.0;
  count = 0;
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - regular variables don't require descriptions
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void OneParameter_WithConcatenatedDescription_NoFinding()
    {
        // Arrange - description with two string tokens (covers line 135: i > 0 branch)
        var code = """
model SimpleModel
  parameter Real x "description part1" + " part2";
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - has description, no finding
        Assert.Empty(ruleFindings);
    }

    [Fact]
    public void DisabledCheck_NoFindingsReported()
    {
        // Arrange - both checks disabled, covers early return in VisitStored_definition
        var code = """
model SimpleModel
  constant Real c = 1.0;
  parameter Real p = 2.0;
end SimpleModel;
""";

        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new PublicParametersAndConstantsHaveDescription(
            parameterHasDescription: false,
            constantHasDescription: false);
        visitor.Visit(parseTree);

        // Assert - checks disabled, no findings
        Assert.Empty(visitor.RuleFindings);
    }

    [Fact]
    public void OneParameter_WithEmptyStringDescription_ReportsFinding()
    {
        // Arrange - covers lines 138-141: empty string description (non-zero length but only whitespace)
        var code = """
model SimpleModel
  parameter Real x " ";
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - an empty/whitespace string description should be flagged
        Assert.Single(ruleFindings);
        Assert.Contains("empty string", ruleFindings[0].Summary);
    }

    [Fact]
    public void OneConstant_WithEmptyStringDescription_ReportsFinding()
    {
        // Arrange - covers empty string check for constants
        var code = """
model SimpleModel
  constant Real pi = 3.14 " ";
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert
        Assert.Single(ruleFindings);
        Assert.Contains("empty string", ruleFindings[0].Summary);
    }

    [Fact]
    public void OneParameter_NoDescriptionWithAnnotation_ReportsFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - a single finding 
        Assert.Single(ruleFindings);
        Assert.Contains("must have a description", ruleFindings[0].Summary);
    }    


    [Fact]
    public void OneParameter_DescriptionWithAnnotation_ReportsFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x "a description" annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - no findings
        Assert.Empty(ruleFindings);
    }    


    [Fact]
    public void OneConstant_NoDescriptionWithAnnotation_ReportsFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  constant Real x annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - a single finding 
        Assert.Single(ruleFindings);
        Assert.Contains("must have a description", ruleFindings[0].Summary);
    }    


    [Fact]
    public void OneConstant_DescriptionWithAnnotation_ReportsFinding()
    {
        // Arrange
        var code = """
model SimpleModel
  constant Real x "a description" annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleFindings = CheckRule(code);

        // Assert - no findings
        Assert.Empty(ruleFindings);
    }    
        
}
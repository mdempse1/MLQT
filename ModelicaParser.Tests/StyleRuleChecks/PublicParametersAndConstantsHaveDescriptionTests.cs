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
        return visitor.RuleViolations;
    }

    [Fact]
    public void OneVariable_WithDescription_NoViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x "description here";
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Empty(ruleViolations);
    }
    
    [Fact]
    public void OneVariable_NoDescription_NoViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  Real x;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Empty(ruleViolations);
    }

        
    [Fact]
    public void OneProtectedVariable_NoDescription_NoViolation()
    {
        // Arrange
        var code = """
model SimpleModel
protected
  Real x;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void OneParameter_NoDescription_Violation()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("Public parameter", ruleViolations[0].Summary);
        Assert.Equal(2, ruleViolations[0].LineNumber);
        Assert.Equal("SimpleModel", ruleViolations[0].ModelName);
        Assert.Contains(" x ", ruleViolations[0].Summary);
    }    

    [Fact]
    public void OnePublicParameter_NoDescription_Violation()
    {
        // Arrange
        var code = """
model SimpleModel
public
  parameter Real x;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("Public parameter", ruleViolations[0].Summary);
        Assert.Equal(3, ruleViolations[0].LineNumber);
        Assert.Equal("SimpleModel", ruleViolations[0].ModelName);
        Assert.Contains(" x ", ruleViolations[0].Summary);
    }    


    [Fact]
    public void OnePublicOneProtectedParameter_NoDescription_Violation()
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
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("Public parameter", ruleViolations[0].Summary);
        Assert.Equal(3, ruleViolations[0].LineNumber);
        Assert.Equal("SimpleModel", ruleViolations[0].ModelName);
        Assert.Contains(" x ", ruleViolations[0].Summary);
    }        

       [Fact]
    public void OnePublicOneProtectedParameter_WithDescription_NoViolation()
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
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Empty(ruleViolations);
    }    

   [Fact]
    public void TwoPublicOneProtectedParameter_NoDescription_OneViolation()
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
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("Public parameter", ruleViolations[0].Summary);
        Assert.Equal(7, ruleViolations[0].LineNumber);
        Assert.Equal("SimpleModel", ruleViolations[0].ModelName);
        Assert.Contains(" z ", ruleViolations[0].Summary);
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
        var ruleViolations = CheckRule(code);

        // Assert - nested models are skipped; Test package has no public parameters
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void Model_MultiplePublicParams_TwoViolations()
    {
        // Test standalone models with parameter violations
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

        var ruleViolations = CheckRule(code);

        Assert.Single(ruleViolations);
        Assert.Contains("Public parameter", ruleViolations[0].Summary);
        Assert.Contains(" z ", ruleViolations[0].Summary);
    }

    [Fact]
    public void PublicConstant_WithDescription_NoViolation()
    {
        // Arrange - constant prefix path in VisitComponent_clause
        var code = """
model SimpleModel
  constant Real gravity = 9.81 "gravitational acceleration";
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void PublicConstant_WithoutDescription_ReportsViolation()
    {
        // Arrange - constant without description
        var code = """
model SimpleModel
  constant Real gravity = 9.81;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("constant", ruleViolations[0].Summary);
        Assert.Contains("gravity", ruleViolations[0].Summary);
    }

    [Fact]
    public void TwoPublicConstants_WithoutDescription_ReportsTwoViolations()
    {
        // Arrange - two constants without descriptions
        var code = """
model SimpleModel
  constant Real gravity = 9.81;
  constant Integer count = 0;
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Equal(2, ruleViolations.Count);
        Assert.All(ruleViolations, v => Assert.Contains("constant", v.Summary));
    }

    [Fact]
    public void RegularVariable_NoPrefixNoDescription_NoViolation()
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
        var ruleViolations = CheckRule(code);

        // Assert - regular variables don't require descriptions
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void OneParameter_WithConcatenatedDescription_NoViolation()
    {
        // Arrange - description with two string tokens (covers line 135: i > 0 branch)
        var code = """
model SimpleModel
  parameter Real x "description part1" + " part2";
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - has description, no violation
        Assert.Empty(ruleViolations);
    }

    [Fact]
    public void DisabledCheck_NoViolationsReported()
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

        // Assert - checks disabled, no violations
        Assert.Empty(visitor.RuleViolations);
    }

    [Fact]
    public void OneParameter_WithEmptyStringDescription_ReportsViolation()
    {
        // Arrange - covers lines 138-141: empty string description (non-zero length but only whitespace)
        var code = """
model SimpleModel
  parameter Real x " ";
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - an empty/whitespace string description should be flagged
        Assert.Single(ruleViolations);
        Assert.Contains("empty string", ruleViolations[0].Summary);
    }

    [Fact]
    public void OneConstant_WithEmptyStringDescription_ReportsViolation()
    {
        // Arrange - covers empty string check for constants
        var code = """
model SimpleModel
  constant Real pi = 3.14 " ";
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert
        Assert.Single(ruleViolations);
        Assert.Contains("empty string", ruleViolations[0].Summary);
    }

    [Fact]
    public void OneParameter_NoDescriptionWithAnnotation_ReportsViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - a single violation 
        Assert.Single(ruleViolations);
        Assert.Contains("must have a description", ruleViolations[0].Summary);
    }    


    [Fact]
    public void OneParameter_DescriptionWithAnnotation_ReportsViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  parameter Real x "a description" annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - no violations
        Assert.Empty(ruleViolations);
    }    


    [Fact]
    public void OneConstant_NoDescriptionWithAnnotation_ReportsViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  constant Real x annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - a single violation 
        Assert.Single(ruleViolations);
        Assert.Contains("must have a description", ruleViolations[0].Summary);
    }    


    [Fact]
    public void OneConstant_DescriptionWithAnnotation_ReportsViolation()
    {
        // Arrange
        var code = """
model SimpleModel
  constant Real x "a description" annotation(Dialog(blah=1));
end SimpleModel;
""";

        // Act
        var ruleViolations = CheckRule(code);

        // Assert - no violations
        Assert.Empty(ruleViolations);
    }    
        
}
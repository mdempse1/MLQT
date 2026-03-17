using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class RedeclareTests
{
#region Redeclare Statements
    [Fact]
    public void RedeclareModel_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare AnotherModel.Type modelName);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void RedeclarePackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare package Medium = AnotherModel.Type);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);        
    }

    [Fact]
    public void RedeclareEachPackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare each package Medium = AnotherModel.Type);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);        
    }    

    [Fact]
    public void RedeclareEachFinalPackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare each final package Medium = AnotherModel.Type);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);        
    }

    [Fact]
    public void RedeclareMultipleEachPackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(
            redeclare package Medium = AnotherModel.Type, 
            each allowFlowReversal=true,
            each use_p_in=false
          );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);        
    }    

    [Fact]
    public void RedeclareMultipleEachFinalPackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(
            redeclare each final package Medium = AnotherModel.Type, 
            each final allowFlowReversal=true,
            each final use_p_in=false
          );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);        
    }    
#endregion

#region Redeclare with Element Replaceable

    [Fact]
    public void RedeclareReplaceablePackage_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare replaceable package Medium = NewMedium);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void RedeclareReplaceableComponent_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare replaceable Real x=2.0);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void RedeclareReplaceableWithConstraint_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends BaseModel(redeclare replaceable package Medium = NewMedium
            constrainedby BaseMedium);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }


    [Fact]
    public void RedeclareRecordExtends_FormatsCorrectly()
    {
        // redeclare is only valid inside an element (within a package/model),
        // not at the top level of a stored_definition
        var testModel = """
        model Container

          redeclare record extends BaseModel "Some base model"
            Real a;
          end BaseModel;
        end Container;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void PackageRedeclareRecordExtends_FormatsCorrectly()
    {
        var testModel = """
        package Library
        
          redeclare record extends BaseModel "Some base model"
            Real a;
          end BaseModel;
        end Library;
        """;
        TestHelpers.AssertClass(testModel);
    }
    
#endregion
}
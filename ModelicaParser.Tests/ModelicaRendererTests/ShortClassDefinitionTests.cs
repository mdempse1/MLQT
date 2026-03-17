using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class ShortClassDefinitionTests
{
    //Test cases still needed for:
    // - replacable models
    // - conditional components
    // - redeclarations
    // - annotations
    // - external functions with annotations & class annotations
    
    [Fact]
    public void ShortClass_FormatsCorrectly()
    {
        var testModel = """ 
        type NewType = Real;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ShortClassWithDescription_FormatsCorrectly()
    {
        var testModel = """ 
        type NewType = Real "A new type defined as Real";
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ShortClassOneModifier_FormatsCorrectly()
    {
        var testModel = """ 
        type NewType = Real(quantity="Something") "A new type defined as Real";
        """;
        TestHelpers.AssertClass(testModel);
    }
    
    [Fact]
    public void ShortClassMultipleModifier_FormatsCorrectly()
    {
        var testModel = """
        type NewType = Real(
          quantity="Something",
          unit="m/s",
          displayUnit="km/h"
        ) "A new type defined as Real";
        """;
        TestHelpers.AssertClass(testModel);
    }
        
    [Fact]
    public void ShortClassFinalModifier_FormatsCorrectly()
    {
        var testModel = """
        type NewType = Real(
          quantity="Something",
          final unit="m/s",
          displayUnit="km/h"
        ) "A new type defined as Real";
        """;
        TestHelpers.AssertClass(testModel);
    }
        
    [Fact]
    public void ShortClassAllFinalModifier_FormatsCorrectly()
    {
        var testModel = """
        type NewType = Real(
          final quantity="Something",
          final unit="m/s",
          final displayUnit="km/h"
        ) "A new type defined as Real";
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void TypeNameIssue_FormatsCorrectly()
    {
        var testModel = """ 
        type CircularWaveNumber = Real(final quantity="CircularWaveNumber", final unit="rad/m");        
        """;
        TestHelpers.AssertClass(testModel);
    }
     
    [Fact]
    public void ShortPackageDefinition_FormatsCorrectly()
    {
        var testModel = """ 
        package StandardWaterOnePhase = WaterIF97_pT "Water using the IF97 standard, explicit in p and T. Recommended for one-phase applications";        
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void BasicEnumeration_FormatsCorrectly()
    {
        var testModel = """ 
        type Test = enumeration(
          Option1, 
          Option2
        );
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void BasicEnumerationWithComments_FormatsCorrectly()
    {
        var testModel = """ 
        type Test = enumeration(
          Option1 "Option 1", 
          Option2 "Option 2"
        ) "Enumeration of something";
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void BasicEnumerationWithAnnotation_FormatsCorrectly()
    {
        var testModel = """ 
        type Test = enumeration(
          Option1 "Option 1", 
          Option2 "Option 2"
        ) "Enumeration of something"
          annotation (Evaluate=true);
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void EnumerationUnknownOption_FormatsCorrectly()
    {
        var testModel = """ 
        type Test = enumeration(:);
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ShortClassArraySubscripts_FormatsCorrectly()
    {
        //IDENT '=' base_prefix type_specifier (array_subscripts)? (class_modification)? comment
        var testModel = """ 
        type NewType = Real[4];
        """;
        TestHelpers.AssertClass(testModel);
    }


    [Fact]
    public void ShortClassWithAllElements_FormatsCorrectly()
    {
        //IDENT '=' base_prefix type_specifier (array_subscripts)? (class_modification)? comment
        var testModel = """
        type NewType = input Real[4](a=4) "A comment";
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleConsecutiveTypes_NoBlankLines()
    {
        var testModel = """
        model TestModel

          encapsulated package Types
            type Foo = Real;
            type Bar = Real;
            type Baz = Real(unit="s", quantity="Time");
          end Types;
        end TestModel;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleConsecutiveTypes_Idempotent()
    {
        var testModel = """
        model TestModel
          encapsulated package Types
            type Foo = Real;
            type Bar = Real;
            type Baz = Real(unit="s", quantity="Time");
          end Types;
        end TestModel;
        """;
        var firstPass = TestHelpers.FormatCode(testModel);
        var secondPass = TestHelpers.FormatCode(firstPass);
        Assert.Equal(firstPass, secondPass);
    }
}

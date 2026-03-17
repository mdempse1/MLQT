using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for C-style comments (// and /* */) in Modelica code.
/// These comments are now handled through the grammar rather than the hidden channel.
/// </summary>
public class CStyleCommentTests
{
    [Fact]
    public void CommentSameLineAsDeclaration_BeforeDeclaration()
    {
        var testModel = """
        model Test          
          Real x; // This is a comment
        end Test;
        """;
        var expectedOutput = """
        model Test          
          Real x; 
          // This is a comment
        end Test;
        """;
        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput);
    }

    [Fact]
    public void CommentSameLineAsEquation_BeforeDeclaration()
    {
        var testModel = """
        model Test

        equation
          x = 1; // This is a comment
        end Test;
        """;
        var expectedOutput = """
        model Test

        equation
          x = 1; 
          // This is a comment
        end Test;
        """;
        TestHelpers.AssertClass(testModel, expectedOutput: expectedOutput);
    }

    [Fact]
    public void SingleLineComment_BeforeDeclaration()
    {
        var testModel = """
        model Test
          // This is a comment
          Real x;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultiLineComment_BeforeDeclaration()
    {
        var testModel = """
        model Test
          /*
          This is a
          multi-line comment
          */
          Real x;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void SingleLineCommentInline_SameLine()
    {
        var testModel = """
        model Test
          /*Single line block comment*/
          Real x;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Comment_BeforeEquation()
    {
        var testModel = """
        model Test
          Real x;

        equation
          // Set x to zero
          x = 0;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultipleComments_BetweenDeclarations()
    {
        var testModel = """
        model Test
          // First variable
          Real x;
          // Second variable
          Real y;
          // Third variable
          Real z;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Comment_InEquationSection()
    {
        var testModel = """
        model Test
          Real x;
          Real y;

        equation
          // First equation
          x = 1;
          // Second equation
          y = 2;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MultiLineComment_InEquationSection()
    {
        var testModel = """
        model Test
          Real x;

        equation
          /*
          This equation sets x
          to the value 5
          */
          x = 5;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Comment_BeforeImport()
    {
        var testModel = """
        model Test
          // Import statement
          import Modelica.Constants;
          Real pi=Constants.pi;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Comment_BeforeExtends()
    {
        var testModel = """
        model Test
          // Extend base model
          extends BaseModel;
          Real x;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void MixedComments_SingleAndMultiLine()
    {
        var testModel = """
        model Test
          // Single line comment
          Real x;
          /*
          Multi-line comment
          */
          Real y;
          // Another single line
          Real z;
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void Comment_WithCodeEditor_WrapsInTags()
    {
        var testModel = """
        model Test
          // This is a comment
          Real x;
        end Test;
        """;

        var testModelCode = "within;\n" + testModel;
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor = new ModelicaRenderer(renderForCodeEditor: true, showAnnotations: true, excludeClassDefinitions: false, tokenStream);
        visitor.Visit(parseTree);

        var output = string.Join("\n", visitor.Code);

        // Should contain COMMENT tags when rendering for code editor
        Assert.Contains("<COMMENT>", output);
        Assert.Contains("</COMMENT>", output);
        Assert.Contains("// This is a comment", output);
    }

    [Fact]
    public void Comment_WithoutCodeEditor_NoTags()
    {
        var testModel = """
        model Test
          // This is a comment
          Real x;
        end Test;
        """;

        var testModelCode = "within;\n" + testModel;
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false, tokenStream);
        visitor.Visit(parseTree);

        var output = string.Join("\n", visitor.Code);

        // Should NOT contain COMMENT tags when not rendering for code editor
        Assert.DoesNotContain("<COMMENT>", output);
        Assert.DoesNotContain("</COMMENT>", output);
        Assert.Contains("// This is a comment", output);
    }

    [Fact]
    public void Comment_AfterExternal_NoFormatting()
    {
        var testModel = """
        function Test
          input Real x;
          output Real y;
        external "C" y = something(x);
        //A comment after external
        end Test;
        """;

        TestHelpers.AssertClass(testModel);
    }    

    [Fact]
    public void Comment_AfterExternal_ApplyFormatting()
    {
        var testModel = """
        function Test
          input Real x;
          output Real y;
        external "C" y = something(x);
        //A comment after external
        end Test;
        """;

        TestHelpers.AssertClass(testModel, onlyOneOfEachSection: true);
    }    
    
  }

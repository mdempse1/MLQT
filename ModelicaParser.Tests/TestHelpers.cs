using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests for ModelicaSyntaxVisitor that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class TestHelpers
{
    /// <summary>
    /// Helper method to test a Modelica class.
    /// </summary>
    /// <param name="testModel">Input Modelica code in expected formating</param>
    /// <param name="renderForCodeEditor">Whether to render with markup tags</param>
    public static string AssertClass(
        string testModel, 
        bool renderForCodeEditor = false, 
        string expectedOutput = "", 
        int maxLineLength = 100,
        bool onlyOneOfEachSection = false, 
        bool? importsFirst = null,
        bool? componentsBeforeClasses = null)
    {
        string testModelCode;
        if (testModel.StartsWith("within"))
            testModelCode = testModel;
        else
            testModelCode = "within;\n" + testModel;

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor = new ModelicaRenderer(
            renderForCodeEditor, 
            showAnnotations: true, 
            excludeClassDefinitions: false, 
            tokenStream, 
            classNamesToExclude: null, 
            maxLineLength: maxLineLength,
            oneOfEachSection: onlyOneOfEachSection,
            importsFirst: importsFirst==null ? onlyOneOfEachSection : importsFirst.Value,
            componentsBeforeClasses: componentsBeforeClasses==null ? onlyOneOfEachSection : componentsBeforeClasses.Value);
        visitor.Visit(parseTree);

        // Remove trailing empty lines from actual output
        var actualOutput = visitor.Code.ToList();
        while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
        {
            actualOutput.RemoveAt(actualOutput.Count - 1);
        }
        actualOutput.RemoveAt(0); // Remove "within" line

        if (string.IsNullOrEmpty(expectedOutput))
            expectedOutput = testModel;

        // Parse expected output and trim empty lines from beginning and end
        var testModelLines = expectedOutput.Split('\n').ToList();
        while (testModelLines.Count > 0 && string.IsNullOrWhiteSpace(testModelLines[0]))
            testModelLines.RemoveAt(0);
        while (testModelLines.Count > 0 && string.IsNullOrWhiteSpace(testModelLines[testModelLines.Count - 1]))
            testModelLines.RemoveAt(testModelLines.Count - 1);

        Assert.Equal(testModelLines.Count, actualOutput.Count);
        if (testModelLines.Count == actualOutput.Count)
        {
            for (int i = 0; i < testModelLines.Count; i++)
            {
                Assert.Equal(testModelLines[i].TrimEnd(), actualOutput[i].TrimEnd());
            }
        }

        return string.Join('\n', actualOutput);
    }

    /// <summary>
    /// Formats Modelica code and returns the result without asserting against expected output.
    /// Useful for idempotency tests where the formatted output is fed back in.
    /// </summary>
    public static string FormatCode(
        string testModel,
        int maxLineLength = 100,
        bool onlyOneOfEachSection = false,
        bool? importsFirst = null,
        bool? componentsBeforeClasses = null)
    {
        string testModelCode;
        if (testModel.StartsWith("within"))
            testModelCode = testModel;
        else
            testModelCode = "within;\n" + testModel;

        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(testModelCode);
        var visitor = new ModelicaRenderer(
            renderForCodeEditor: false,
            showAnnotations: true,
            excludeClassDefinitions: false,
            tokenStream,
            classNamesToExclude: null,
            maxLineLength: maxLineLength,
            oneOfEachSection: onlyOneOfEachSection,
            importsFirst: importsFirst ?? onlyOneOfEachSection,
            componentsBeforeClasses: componentsBeforeClasses ?? onlyOneOfEachSection);
        visitor.Visit(parseTree);

        var actualOutput = visitor.Code.ToList();
        while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
            actualOutput.RemoveAt(actualOutput.Count - 1);
        actualOutput.RemoveAt(0); // Remove "within" line

        return string.Join('\n', actualOutput);
    }
}
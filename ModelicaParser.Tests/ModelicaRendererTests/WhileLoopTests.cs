using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class WhileLoopTests
{
#region While Loop Statements
    [Fact]
    public void WhileStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test
        
        algorithm 
          while x < 10 loop
            y[i] := 1; 
          end while; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }
#endregion

#region While Loop Equations
//While loops are not allowed in equation sections so no tests

#endregion
}

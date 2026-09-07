using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class BehaviorExtractorTests
{
    [Fact]
    public void SeparatesEquationsConnectionsAndStatements()
    {
        const string code = """
            model M
              Real x;
              RealInput u;
            equation
              x = 2*u;
              connect(u, x);
            algorithm
              x := 3;
            end M;
            """;
        var b = BehaviorExtractor.ExtractFromCode(code);

        Assert.True(b.HasEquationSection);
        Assert.True(b.HasAlgorithmSection);
        Assert.Contains(b.Equations, e => e.Text == "x = 2*u");
        Assert.DoesNotContain(b.Equations, e => e.Text.Contains("connect")); // connect is separated out
        var conn = Assert.Single(b.Connections);
        Assert.Equal("u", conn.PortA);
        Assert.Equal("x", conn.PortB);
        Assert.Contains(b.Statements, s => s.Text == "x := 3");
        Assert.True(b.HasAny);
    }

    [Fact]
    public void CapturesLeadingComments_OnEquations()
    {
        const string code = """
            model M
              Real x;
            equation
              // set x from time
              x = time;
            end M;
            """;
        var b = BehaviorExtractor.ExtractFromCode(code);
        var eq = b.Equations.Single(e => e.Text == "x = time");
        Assert.Contains("// set x from time", eq.LeadingComments);
    }

    [Fact]
    public void HandlesCrlfLineEndings()
    {
        // Explicit CRLF: the parser normalizes line endings internally, so the sliced text must too,
        // otherwise every equation slice is shifted by the stripped '\r' characters.
        const string code = "model M\r\n  Real x;\r\n  RealInput u;\r\nequation\r\n  x = 2*u;\r\nend M;";
        var b = BehaviorExtractor.ExtractFromCode(code);
        Assert.Contains(b.Equations, e => e.Text == "x = 2*u");
    }

    [Fact]
    public void NoBehavior_IsEmpty()
    {
        var b = BehaviorExtractor.ExtractFromCode("model M\n  Real x;\nend M;");
        Assert.False(b.HasAny);
        Assert.False(b.HasEquationSection);
    }

    [Fact]
    public void AClassWithNoBody_HasNoBehaviour()
    {
        // A short class definition (`type Gain = Real`) has no composition to read at all, and the
        // renderer must not be handed a null to guard against on every call.
        Assert.False(BehaviorExtractor.ExtractFromCode("type Gain = Real;").HasAny);
    }

    [Fact]
    public void SourceThatIsNotAClass_HasNoBehaviour()
    {
        Assert.False(BehaviorExtractor.ExtractFromCode("this is not Modelica").HasAny);
    }

    [Fact]
    public void ABlockCommentAboveAStatement_StaysWithIt()
    {
        // The formatter rewrites the algorithm section from what comes back here. A comment that got
        // dropped would be deleted from the file on the next save.
        const string code = """
            model M
              Real x;
            algorithm
              /* why this is done first */
              x := 3;
            end M;
            """;

        var behavior = BehaviorExtractor.ExtractFromCode(code);

        var statement = Assert.Single(behavior.Statements);
        Assert.Contains("why this is done first", string.Join("\n", statement.LeadingComments));
    }

    [Fact]
    public void ABlockCommentAboveAnEquation_StaysWithIt()
    {
        const string code = """
            model M
              Real x;
            equation
              /* the balance */
              x = 3;
            end M;
            """;

        var equation = Assert.Single(BehaviorExtractor.ExtractFromCode(code).Equations);

        Assert.Contains("the balance", string.Join("\n", equation.LeadingComments));
    }

    [Fact]
    public void AnEmptyAlgorithmSection_CountsAsPresentWithNoStatements()
    {
        var behavior = BehaviorExtractor.ExtractFromCode("model M\n  Real x;\nalgorithm\nend M;");

        Assert.True(behavior.HasAlgorithmSection);
        Assert.Empty(behavior.Statements);
        Assert.False(behavior.HasAny);
    }
}

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
    public void AConnectInAForLoop_IsAConnection()
    {
        // How an array of components is wired. This came back as one opaque equation and no
        // connections, so get_diagram_layout reported such a model as unwired.
        const string code = """
            model N
              Pin a[3], b[3];
            equation
              for i in 1:3 loop
                connect(a[i], b[i]);
              end for;
            end N;
            """;

        var b = BehaviorExtractor.ExtractFromCode(code);

        var connection = Assert.Single(b.Connections);
        Assert.Equal("a[i]", connection.PortA);
        Assert.Equal("b[i]", connection.PortB);
        Assert.Equal(new[] { "for i in 1:3" }, connection.Within);
        // The loop is still an equation of the class, verbatim - its text is where the connect is.
        Assert.Contains("connect(a[i], b[i]);", Assert.Single(b.Equations).Text);
    }

    [Fact]
    public void AConnectMissingAPort_IsStillShown_AsAnEquation()
    {
        // The parser recovers `connect(a)` into a connect clause with one port. It is not a
        // connection, and it must not disappear from the class's behaviour either.
        var b = BehaviorExtractor.ExtractFromCode("model M\nequation\n  connect(a);\n  x = 1;\nend M;");

        Assert.Empty(b.Connections);
        Assert.Contains(b.Equations, e => e.Text.StartsWith("connect(a", StringComparison.Ordinal));
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
        // A short class definition (`type Gain = Real`) has no composition to read at all. The answer
        // is the empty behaviour, not a null: get_class_behavior and get_diagram_layout read its lists
        // directly, and get_class_behavior asks HasAny of every base class it walks, short ones included.
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
        // get_class_behavior hands each statement to an agent with the comments above it, in place of
        // the source. A comment dropped here is one the agent never sees - often the reason the
        // statement is written the way it is.
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

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
        Assert.Contains("x = 2*u", b.Equations);
        Assert.DoesNotContain(b.Equations, e => e.Contains("connect")); // connect is separated out
        var conn = Assert.Single(b.Connections);
        Assert.Equal("u", conn.PortA);
        Assert.Equal("x", conn.PortB);
        Assert.Contains("x := 3", b.Statements);
        Assert.True(b.HasAny);
    }

    [Fact]
    public void NoBehavior_IsEmpty()
    {
        var b = BehaviorExtractor.ExtractFromCode("model M\n  Real x;\nend M;");
        Assert.False(b.HasAny);
        Assert.False(b.HasEquationSection);
    }
}

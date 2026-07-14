using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class ClassBodyLocatorTests
{
    [Fact]
    public void Components_Captured_WithTypeAndModification()
    {
        const string code = "model M\n  parameter Real k = 2 \"gain\";\n  Real x;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.True(layout.Found);
        Assert.Equal(2, layout.Components.Count);

        var k = layout.Components.Single(c => c.Name == "k");
        Assert.Equal("Real", k.TypeText);
        Assert.True(k.SoleInClause);
        Assert.NotNull(k.ModStart);
        // The modification span covers "= 2".
        Assert.Equal("= 2", code.Substring(k.ModStart!.Value, k.ModStop!.Value - k.ModStart.Value + 1));
    }

    [Fact]
    public void PublicAppendOffset_IsAfterLastElement()
    {
        const string code = "model M\n  Real x;\nend M;";
        var layout = ClassBodyLocator.Analyze(code);
        // Inserting at the append offset should place new text right after "Real x;".
        var edited = code.Insert(layout.PublicAppendOffset, "\n  Real y;");
        Assert.Contains("Real x;\n  Real y;", edited);
    }

    [Fact]
    public void MultipleComponentsInClause_NotSole()
    {
        var layout = ClassBodyLocator.Analyze("model M\n  Real a, b, c;\nend M;");
        Assert.Equal(3, layout.Components.Count);
        Assert.All(layout.Components, c => Assert.False(c.SoleInClause));
    }

    [Fact]
    public void EquationSection_Offset_AndConnections()
    {
        const string code =
            "model M\n  RealInput u;\n  RealOutput y;\nequation\n  connect(u, y);\nend M;";
        var layout = ClassBodyLocator.Analyze(code);

        Assert.NotNull(layout.EquationAppendOffset);
        var conn = Assert.Single(layout.Connections);
        Assert.Equal("u", conn.PortA);
        Assert.Equal("y", conn.PortB);
        Assert.Equal("connect(u, y)", code.Substring(conn.Start, conn.Stop - conn.Start + 1));
    }

    [Fact]
    public void NoEquationSection_OffsetNull()
    {
        var layout = ClassBodyLocator.Analyze("model M\n  Real x;\nend M;");
        Assert.Null(layout.EquationAppendOffset);
        Assert.Null(layout.AlgorithmAppendOffset);
    }

    [Fact]
    public void AlgorithmSection_OffsetCaptured()
    {
        var layout = ClassBodyLocator.Analyze("function f\n  input Real x;\n  output Real y;\nalgorithm\n  y := x;\nend f;");
        Assert.NotNull(layout.AlgorithmAppendOffset);
    }
}

using System.Collections.Generic;
using System.Linq;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class MissingUnitsTests
{
    private static List<Finding> Check(string code)
    {
        var visitor = new MissingUnits();
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Findings.Where(f => f.RuleId == RuleIds.MissingUnit).ToList();
    }

    [Fact]
    public void PlainReal_WithoutUnit_IsFlagged()
    {
        var f = Assert.Single(Check("model M\n  Real x;\nend M;"));
        Assert.Equal("x", f.ElementPath);
    }

    [Fact]
    public void ParameterReal_WithoutUnit_IsFlagged()
    {
        Assert.Single(Check("model M\n  parameter Real k = 1;\nend M;"), f => f.ElementPath == "k");
    }

    [Fact]
    public void Real_WithUnit_IsNotFlagged()
    {
        Assert.Empty(Check("model M\n  Real x(unit=\"m\") = 1;\nend M;"));
    }

    [Fact]
    public void Real_WithOtherAttributeButNoUnit_IsFlagged()
    {
        Assert.Single(Check("model M\n  Real x(min=0);\nend M;"), f => f.ElementPath == "x");
    }

    [Fact]
    public void SiTypedComponent_IsNotFlagged()
    {
        // An SI type carries its own unit — not a plain Real, so it is left alone (no resolution needed).
        Assert.Empty(Check("model M\n  Modelica.Units.SI.Length len;\nend M;"));
    }

    [Fact]
    public void NonRealTypes_AreNotFlagged()
    {
        Assert.Empty(Check("model M\n  Integer n;\n  Boolean b;\n  String s;\nend M;"));
    }

    [Fact]
    public void MultipleRealsInOneClause_EachChecked()
    {
        // `Real a, b;` — both lack a unit.
        var f = Check("model M\n  Real a, b;\nend M;");
        Assert.Equal(2, f.Count);
        Assert.Contains(f, x => x.ElementPath == "a");
        Assert.Contains(f, x => x.ElementPath == "b");
    }
}

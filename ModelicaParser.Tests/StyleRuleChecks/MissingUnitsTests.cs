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

    // ---- with a type lookup (a caller that has the dependency graph) ---------------------------

    /// <summary>A stand-in for the graph: the types it knows, and whether each fixes a unit.</summary>
    private static List<Finding> CheckWith(string code, params (string Type, bool IsReal, bool HasUnit)[] types)
    {
        var visitor = new MissingUnits(
            basePackage: string.Empty,
            unitLookup: (_, typeName) =>
            {
                foreach (var (type, isReal, hasUnit) in types)
                    if (type == typeName)
                        return (isReal, hasUnit);
                return (false, false);
            });
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Findings.Where(f => f.RuleId == RuleIds.MissingUnit).ToList();
    }

    [Fact]
    public void ATypeThatFixesNoUnit_IsFlaggedWhenTheTypesCanBeResolved()
    {
        // The gap the Unit coverage dimension has always counted and the rule never reported: an
        // alias of Real that fixes nothing leaves the quantity as unitless as a bare Real does.
        var finding = Assert.Single(
            CheckWith("model M\n  Fraction f;\nend M;", ("Fraction", true, false)));

        Assert.Equal("f", finding.ElementPath);
        Assert.Contains("Fraction f does not declare a unit", finding.Message);
    }

    [Fact]
    public void ATypeThatFixesAUnit_IsLeftAlone()
    {
        Assert.Empty(CheckWith("model M\n  Length ell;\nend M;", ("Length", true, true)));
    }

    [Fact]
    public void ATypeThatIsNotAQuantity_IsLeftAlone()
    {
        // Integer, Boolean, a connector, a model: nothing to put a unit on.
        Assert.Empty(CheckWith("model M\n  Pin p;\nend M;", ("Pin", false, false)));
    }

    [Fact]
    public void AnInlineUnitSatisfiesAUnitlessType()
    {
        // The declaration is the other place a unit can be written, whatever the type does.
        Assert.Empty(CheckWith(
            "model M\n  Fraction f(unit=\"1\");\nend M;", ("Fraction", true, false)));
    }

    [Fact]
    public void WithoutALookup_OnlyPlainRealIsJudged()
    {
        // A snippet check has no graph, so it cannot know what Fraction is — and guessing would
        // report a library's SI types as unitless.
        Assert.Empty(Check("model M\n  Fraction f;\nend M;"));
        Assert.Single(Check("model M\n  Real x;\nend M;"));
    }

    [Fact]
    public void PlainReal_IsStillJudgedWhenALookupIsGiven()
    {
        Assert.Single(CheckWith("model M\n  Real x;\nend M;", ("Fraction", true, false)));
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

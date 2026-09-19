using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// B181 — the rule behind <c>FormattingOptions.ComponentsBeforeClasses</c>, which until now was the
/// one layout choice MLQT could apply and never report.
///
/// <para>The cases that matter are the ones where "components before classes" is not simply a
/// comparison over the whole class: each <c>public</c>/<c>protected</c> section is ordered on its
/// own, because the renderer never moves an element across that boundary.</para>
/// </summary>
public class ComponentsBeforeClassesTests
{
    private static List<LogMessage> CheckRule(string code)
    {
        var visitor = new ComponentsBeforeClasses();
        visitor.Visit(ModelicaParserHelper.Parse(code));
        return visitor.RuleFindings;
    }

    [Fact]
    public void ComponentsFirst_IsAccepted()
    {
        var code = """
model M
  Real x "a component";
  package Inner
  end Inner;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void AComponentAfterAClass_IsReported()
    {
        var code = """
model M
  package Inner
  end Inner;
  Real x "a component";
end M;
""";

        var findings = CheckRule(code);

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.ComponentsBeforeClasses, finding.RuleId);
        Assert.Contains("'x'", finding.Summary);
    }

    [Fact]
    public void EveryMisplacedComponent_IsReported_NotJustTheFirst()
    {
        // One finding per component, because the finding is about the component that is out of
        // place. Reporting only the first would hide the rest until it was fixed.
        var code = """
model M
  package Inner
  end Inner;
  Real x;
  Real y;
end M;
""";

        var findings = CheckRule(code);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal(RuleIds.ComponentsBeforeClasses, f.RuleId));
    }

    [Fact]
    public void AClauseDeclaringSeveralNames_IsOneFinding()
    {
        // `Real x, y;` is one clause and moves as one, so it is one finding named for the first.
        var code = """
model M
  package Inner
  end Inner;
  Real x, y;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("'x'", finding.Summary);
    }

    [Fact]
    public void EachSectionIsOrderedOnItsOwn()
    {
        // The case that makes a whole-class comparison wrong: the protected component comes after
        // the public class definition in the text, and the renderer will never move it across the
        // section boundary, so the class is correctly ordered and nothing may be reported.
        var code = """
model M
  Real a;
  package Inner
  end Inner;
protected
  Real b;
  package Hidden
  end Hidden;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void AMisplacedComponentInTheProtectedSection_IsReported()
    {
        // ...and the section is still checked, rather than being skipped along with the boundary.
        var code = """
model M
  Real a;
protected
  package Hidden
  end Hidden;
  Real b;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("'b'", finding.Summary);
    }

    [Fact]
    public void ImportsAndExtends_TakeNoPartInThisOrdering()
    {
        // They are neither components nor classes. MLQT.Style.ImportStatementsFirst is the rule with
        // an opinion about where they go; this one must not report them or be confused by them.
        var code = """
model M
  import Modelica.Units.SI;
  extends Base;
  package Inner
  end Inner;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void AClassWithNoNestedClasses_IsAccepted()
    {
        var code = """
model M
  Real x;
  Real y;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void TheFindingCarriesTheComponentsLine()
    {
        var code = """
model M
  package Inner
  end Inner;
  Real x;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Equal(4, finding.LineNumber);
    }
}

using Xunit;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.StyleRuleChecks;

/// <summary>
/// B252 — the rule that extends <c>ComponentsBeforeClasses</c> from one boundary to four: inputs
/// and outputs, constants, parameters, variables, components.
/// </summary>
public class DeclarationOrderTests
{
    private static List<LogMessage> CheckRule(string code, Func<string, string, bool>? isSimpleType = null)
    {
        var visitor = new DeclarationOrder("", isSimpleType);
        visitor.Visit(ModelicaParserHelper.Parse(code));
        return visitor.RuleFindings;
    }

    [Fact]
    public void TheConventionalOrder_IsAccepted()
    {
        var code = """
model M
  input Real u;
  output Real y;
  constant Real g = 9.81;
  parameter Real m = 1;
  Real x;
  Resistor r;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void AParameterAfterAVariable_IsReported()
    {
        var code = """
model M
  Real x;
  parameter Real m = 1;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("Parameter 'm'", finding.Summary);
        Assert.Contains("after variables", finding.Summary);
    }

    [Fact]
    public void AConstantAfterAParameter_IsReported()
    {
        // The boundary the row calls out as the one some teams write the other way round. It is
        // fixed, and this is the finding that says so.
        var code = """
model M
  parameter Real m = 1;
  constant Real g = 9.81;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("Constant 'g'", finding.Summary);
        Assert.Contains("after parameters", finding.Summary);
    }

    [Fact]
    public void AnInputAfterAnythingElse_IsReported()
    {
        var code = """
function f
  Real localTemp;
  input Real x;
  output Real y;
end f;
""";

        var findings = CheckRule(code);
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Summary.Contains("Input/output 'x'"));
        Assert.Contains(findings, f => f.Summary.Contains("Input/output 'y'"));
    }

    [Fact]
    public void EveryDeclarationOutOfPlace_IsReported_NotJustTheFirst()
    {
        // The finding is about the declaration that is out of place and carries its line; reporting
        // only the first would leave the rest invisible until it was fixed.
        var code = """
model M
  Resistor r;
  parameter Real m = 1;
  constant Real g = 9.81;
end M;
""";

        var findings = CheckRule(code);
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Summary.Contains("Parameter 'm'"));
        Assert.Contains(findings, f => f.Summary.Contains("Constant 'g'"));
    }

    [Fact]
    public void EachSectionIsOrderedOnItsOwn()
    {
        // The renderer never moves an element across the public/protected boundary, so a component
        // in the public section followed by a parameter in the protected one is correctly ordered.
        // Comparing over the whole class would report exactly what formatting produces.
        var code = """
model M
  Resistor r;
protected
  parameter Real m = 1;
  Capacitor c;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void ANestedClassDoesNotResetTheSequence_AndIsNotReportedHere()
    {
        // ComponentsBeforeClasses is the rule with an opinion about a declaration that follows a
        // class. This one says nothing about it, and does not start counting again either.
        var code = """
model M
  parameter Real m = 1;
  package Inner
  end Inner;
  constant Real g = 9.81;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("Constant 'g'", finding.Summary);
    }

    [Fact]
    public void ImportsAndExtends_TakeNoPartInIt()
    {
        var code = """
model M
  import Modelica.Units.SI;
  extends Base;
  parameter Real m = 1;
  Real x;
end M;
""";

        Assert.Empty(CheckRule(code));
    }

    [Fact]
    public void WithoutAResolver_ADerivedTypeIsAComponent()
    {
        // All a check with no library loaded can honestly say — and the same answer the renderer
        // gives, which is what stops the rule reporting an arrangement the formatter will not make.
        // So SI.Length sorts where a component goes, and a plain Real before it is in order.
        var inOrder = """
model M
  Real x;
  SI.Length len;
end M;
""";
        Assert.Empty(CheckRule(inOrder));

        var outOfOrder = """
model M
  SI.Length len;
  Real x;
end M;
""";
        Assert.Contains(CheckRule(outOfOrder), f => f.Summary.Contains("Variable 'x'"));
    }

    [Fact]
    public void WithAResolver_ADerivedTypeIsAVariable()
    {
        // The case the whole item turns on: with the graph behind it, SI.Length is a variable and
        // belongs before the components.
        var code = """
model M
  Resistor r;
  SI.Length len;
end M;
""";

        var finding = Assert.Single(CheckRule(code, (_, type) => type == "SI.Length"));
        Assert.Contains("Variable 'len'", finding.Summary);
        Assert.Contains("after components", finding.Summary);
    }

    [Fact]
    public void TheResolverIsAskedAboutTheClassBeingChecked()
    {
        // The same type name means different things in different scopes, so the lookup is keyed by
        // both — and the renderer is given the same key, which is why it tracks nested classes.
        var asked = new List<(string Model, string Type)>();
        var code = """
model M
  SI.Length len;
end M;
""";

        CheckRule(code, (model, type) => { asked.Add((model, type)); return false; });

        Assert.Contains(("M", "SI.Length"), asked);
    }

    [Fact]
    public void AClauseDeclaringSeveralNames_IsOneFinding()
    {
        // The whole clause moves as one, so it is one finding rather than one per name.
        var code = """
model M
  Real x;
  parameter Real a = 1, b = 2;
end M;
""";

        var finding = Assert.Single(CheckRule(code));
        Assert.Contains("'a'", finding.Summary);
    }
}

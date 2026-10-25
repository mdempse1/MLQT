using System.Collections.Generic;
using System.Linq;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaParser.Tests.StyleRuleChecks;

public class GrammarConformanceTests
{
    private static List<Finding> Check(string code, string basePackage = "")
    {
        var visitor = new GrammarConformance(basePackage);
        visitor.VisitStored_definition(ModelicaParserHelper.Parse(code));
        return visitor.Findings.Where(f => f.RuleId == RuleIds.GrammarConformance).ToList();
    }

    private static string Kind(Finding f) => f.Discriminator!.Split(':')[0];

    [Fact]
    public void StrictCode_IsNotReported()
    {
        Assert.Empty(Check("""
            model M "A model written to the letter of MLS 3.7"
              import Modelica.Constants.pi;
              type Gain = input Real;
              parameter Real k(min = 0) = 2 "Gain";
              Real x(start = 1, fixed = true);
              Real y[3] = {i for i in 1:3};
              Real z = sum(y[i] for i in 1:3);
              String s = "quote \" tab \t newline \n backslash \\ query \? apostrophe \' bell \a";
              Real 'quoted name';
            equation
              der(x) = -k * x + time;
            algorithm
              der(x) := 1;
              annotation (Documentation(info = "<html>ok</html>"));
            end M;
            """));
    }

    [Fact]
    public void AClassAnnotationBeforeTheElements_IsReported()
    {
        var finding = Assert.Single(Check("model M\n  annotation (Icon());\n  Real x;\nend M;"));

        Assert.Equal("leading-annotation", finding.Discriminator);
        Assert.Equal(2, finding.LineNumber);
        Assert.Equal("M", finding.ModelId);
        Assert.Null(finding.ElementPath);
    }

    [Fact]
    public void AClassAnnotationStandingAlone_IsWhereMlsPutsIt()
    {
        // The grammar matches it as the leading annotation, but with nothing after it it is the
        // trailing one - the only place MLS has a class annotation.
        Assert.Empty(Check("model M\n  annotation (Icon());\nend M;"));
    }

    [Fact]
    public void AClassAnnotationBeforeAnEquationSection_IsReported()
    {
        Assert.Equal("leading-annotation",
            Kind(Assert.Single(Check("model M\n  annotation (Icon());\nequation\nend M;"))));
    }

    [Theory]
    [InlineData("parameter")]
    [InlineData("constant")]
    [InlineData("discrete")]
    [InlineData("flow")]
    [InlineData("stream")]
    public void AShortClassWithAPrefixOtherThanInputOrOutput_IsReported(string prefix)
    {
        var finding = Assert.Single(Check($"type T = {prefix} Real;"));

        Assert.Equal($"base-prefix:{prefix}", finding.Discriminator);
        Assert.Equal("T", finding.ElementPath);
        Assert.Contains($"'{prefix}'", finding.Message);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    public void AShortClassWithInputOrOutput_IsStrict(string prefix)
    {
        Assert.Empty(Check($"connector C = {prefix} Real;"));
    }

    [Fact]
    public void AModificationWrittenWithAssignment_IsReported()
    {
        var findings = Check("model M\n  Real x := 1;\n  Real y(start := 2);\nend M;");

        Assert.Equal(["x", "start"], findings.Select(f => f.ElementPath));
        Assert.All(findings, f => Assert.Equal("assignment-modification", f.Discriminator));
        Assert.Equal([2, 3], findings.Select(f => f.LineNumber));
    }

    [Theory]
    [InlineData("der(x, y) := 1;")]
    [InlineData("der(x + 1) := 1;")]
    [InlineData("der(x)(1);")]
    [InlineData("der(x for i in 1:2) := 1;")]
    [InlineData("der(n = x) := 1;")]
    public void ADerStatementThatIsNotAnAssignmentToOneVariable_IsReported(string statement)
    {
        Assert.Equal("der-statement",
            Kind(Assert.Single(Check($"function f\n  output Real x;\nalgorithm\n  {statement}\nend f;"))));
    }

    [Theory]
    [InlineData("der(x) := 1;")]
    [InlineData("der(a.b[1]) := 1;")]
    public void DerOfAVariableAssigned_IsStrict(string statement)
    {
        Assert.Empty(Check($"function f\n  output Real x;\nalgorithm\n  {statement}\nend f;"));
    }

    [Fact]
    public void AComprehensionAfterSeveralArguments_IsReported()
    {
        var findings = Check("model M\n  Real a = f(1, 2 for i in 1:3);\n  Real b[2] = {1, 2 for i in 1:3};\nend M;");

        Assert.Equal(["comprehension:arguments", "comprehension:array"], findings.Select(f => f.Discriminator));
    }

    [Fact]
    public void AComprehensionAsTheSubscriptOfAParenthesisedExpression_IsReported()
    {
        var finding = Assert.Single(Check("model M\n  Real a = (b)[i for i in 1:2];\nend M;"));
        Assert.Equal("comprehension:subscript", finding.Discriminator);
    }

    [Fact]
    public void ASubscriptOfAParenthesisedExpression_IsStrict()
    {
        Assert.Empty(Check("model M\n  Real a = (b)[1];\nend M;"));
    }

    [Theory]
    [InlineData(@"<\p>", @"\p")]
    [InlineData(@"C:\Users", @"\U")]
    [InlineData(@"a\ b", @"\<whitespace>")]
    public void AnEscapeMlsDoesNotDefine_IsReported(string text, string sequence)
    {
        var finding = Assert.Single(Check($"model M \"{text}\"\nend M;"));

        Assert.Equal($"string-escape:{sequence}", finding.Discriminator);
        Assert.Contains("in a string", finding.Message);
    }

    [Fact]
    public void AnEscapeOnALaterLineOfAString_IsReportedOnThatLine()
    {
        var finding = Assert.Single(Check("model M\n  annotation (Documentation(info = \"<html>\nfirst\nsecond \\q\n</html>\"));\nend M;"));
        Assert.Equal(4, finding.LineNumber);
    }

    [Fact]
    public void AnEscapedBackslashBeforeALetter_IsStrict()
    {
        // `\\p` is an escaped backslash and then a p, not the escape `\p`.
        Assert.Empty(Check("model M \"C:\\\\path\"\nend M;"));
    }

    [Fact]
    public void AnUndefinedEscapeInAQuotedIdentifier_IsReported()
    {
        var finding = Assert.Single(Check("model M\n  Real 'a\\qb';\nend M;"));
        Assert.Equal(@"string-escape:\q", finding.Discriminator);
        Assert.Contains("in a quoted identifier", finding.Message);
    }

    [Fact]
    public void ATabInAQuotedIdentifier_IsReported()
    {
        var finding = Assert.Single(Check("model M\n  Real 'a\tb';\nend M;"));
        Assert.Equal("quoted-identifier-whitespace", finding.Discriminator);
        Assert.Equal("'a\tb'", finding.ElementPath);
    }

    [Fact]
    public void ALineBreakInAQuotedIdentifier_IsReportedOnTheLineItEnds()
    {
        var finding = Assert.Single(Check("model M\n  Real x;\n  Real 'a\nb';\nend M;"));
        Assert.Equal("quoted-identifier-whitespace", finding.Discriminator);
        Assert.Equal(3, finding.LineNumber);
    }

    [Theory]
    [InlineData("model M\n  Real time;\nend M;")]
    [InlineData("model M\n  Real y = a.time;\nend M;")]
    [InlineData("model M\n  R r(time = 1);\nend M;")]
    [InlineData("type E = enumeration(time);")]
    public void TimeUsedAsAnIdentifier_IsReported(string code)
    {
        var finding = Assert.Single(Check(code));
        Assert.Equal("reserved-name:time", finding.Discriminator);
    }

    [Fact]
    public void TimeAsTheTimeVariable_IsStrict()
    {
        Assert.Empty(Check("model M\n  Real y = time + der(time) + sin(time);\nend M;"));
    }

    [Theory]
    [InlineData("model M\n  Real Real;\nend M;", "Real")]
    [InlineData("model M\n  Integer String = 1;\nend M;", "String")]
    [InlineData("type E = enumeration(Boolean, other);", "Boolean")]
    public void AnElementNamedAfterAPredefinedType_IsReported(string code, string name)
    {
        var finding = Assert.Single(Check(code));
        Assert.Equal($"reserved-name:{name}", finding.Discriminator);
        Assert.Equal(name, finding.ElementPath);
    }

    [Fact]
    public void ANestedClass_IsLeftToItsOwnCheck()
    {
        // Each nested class has a node of its own and is checked from there, as for every rule.
        Assert.Empty(Check("package P\n  model M\n    Real x := 1;\n  end M;\nend P;"));
    }

    [Fact]
    public void AReplaceableNestedClass_IsCheckedWithItsParent()
    {
        var finding = Assert.Single(Check("package P\n  replaceable model M\n    Real x := 1;\n  end M;\nend P;", "Lib"));
        Assert.Equal("Lib.P.M", finding.ModelId);
    }

    [Fact]
    public void EveryKind_HasItsOwnCode()
    {
        var kinds = System.Enum.GetValues<GrammarConformance.Kind>();
        var codes = kinds.Select(GrammarConformance.Code).ToList();

        Assert.Equal(kinds.Length, codes.Distinct().Count());
        Assert.Throws<System.ArgumentOutOfRangeException>(() => GrammarConformance.Code((GrammarConformance.Kind)99));
    }
}

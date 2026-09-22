using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// B252 — the renderer writing the finer declaration order, and the thing that actually matters:
/// that it writes exactly the arrangement <c>MLQT.Style.DeclarationOrder</c> asks for. A finding the
/// formatter does not clear is worse than no rule at all.
/// </summary>
public class DeclarationOrderRenderingTests
{
    private const string Jumbled = """
        model Test
          Resistor r;
          Real x;
          parameter Real m;
          constant Real g;
          input Real u;
          output Real y;
        end Test;
        """;

    [Fact]
    public void TheGroupIsWrittenInOrder()
    {
        var expected = """
            model Test
              input Real u;
              output Real y;
              constant Real g;
              parameter Real m;
              Real x;
              Resistor r;
            end Test;
            """;

        TestHelpers.AssertClass(Jumbled, expectedOutput: expected,
            onlyOneOfEachSection: true, declarationOrder: true);
    }

    [Fact]
    public void WithinAGroup_SourceOrderIsKept()
    {
        // The convention is about the groups, not about what is inside them: a class that writes its
        // outputs before its inputs, or its components in a deliberate order, keeps that.
        var source = """
            model Test
              output Real y;
              input Real u;
              Capacitor c;
              Resistor r;
            end Test;
            """;

        TestHelpers.AssertClass(source, onlyOneOfEachSection: true, declarationOrder: true);
    }

    [Fact]
    public void WithTheOptionOff_TheGroupKeepsItsSourceOrder()
    {
        // It refines components-before-classes the way that refines imports-first: off, it changes
        // nothing at all, and every existing repository's output is untouched.
        TestHelpers.AssertClass(Jumbled, onlyOneOfEachSection: true, declarationOrder: false);
    }

    [Fact]
    public void WithComponentsBeforeClassesOff_ItChangesNothing()
    {
        // The renderer reads this only inside the branch that option selects, which is what its
        // prerequisite in the rule catalogue states.
        TestHelpers.AssertClass(Jumbled, onlyOneOfEachSection: true,
            componentsBeforeClasses: false, declarationOrder: true);
    }

    [Fact]
    public void EachSectionIsOrderedOnItsOwn()
    {
        var source = """
            model Test
              Resistor r;
              parameter Real m;
            protected
              Capacitor c;
              parameter Real k;
            end Test;
            """;

        var expected = """
            model Test
              parameter Real m;
              Resistor r;
            protected
              parameter Real k;
              Capacitor c;
            end Test;
            """;

        TestHelpers.AssertClass(source, expectedOutput: expected,
            onlyOneOfEachSection: true, declarationOrder: true);
    }

    [Fact]
    public void DeclarationsStillComeBeforeNestedClasses()
    {
        var source = """
            model Test
              package Inner
              end Inner;
              Resistor r;
              parameter Real m;
            end Test;
            """;

        var expected = """
            model Test
              parameter Real m;
              Resistor r;

              package Inner
              end Inner;
            end Test;
            """;

        TestHelpers.AssertClass(source, expectedOutput: expected,
            onlyOneOfEachSection: true, declarationOrder: true);
    }

    [Fact]
    public void AResolvedSimpleType_IsWrittenWithTheVariables()
    {
        var source = """
            model Test
              Resistor r;
              SI.Length len;
            end Test;
            """;

        var expected = """
            model Test
              SI.Length len;
              Resistor r;
            end Test;
            """;

        TestHelpers.AssertClass(source, expectedOutput: expected,
            onlyOneOfEachSection: true, declarationOrder: true,
            isSimpleType: (_, type) => type == "SI.Length", rootClassId: "Lib.Test");
    }

    [Fact]
    public void WithoutAResolver_ADerivedTypeStaysWithTheComponents()
    {
        // The same answer the rule gives with no graph, which is the point: the two must agree.
        var source = """
            model Test
              Resistor r;
              SI.Length len;
            end Test;
            """;

        TestHelpers.AssertClass(source, onlyOneOfEachSection: true, declarationOrder: true);
    }

    [Fact]
    public void ANestedClassIsResolvedInItsOwnScope()
    {
        // The renderer tracks the class it is inside so the lookup is asked the same question the
        // rule asks — the rule checks the nested class on its own, with its own id, and a nested
        // class has its own imports.
        var asked = new List<string>();
        var source = """
            model Test
              SI.Length outerLength;

              model Inner
                SI.Length innerLength;
              end Inner;
            end Test;
            """;

        TestHelpers.AssertClass(source, onlyOneOfEachSection: true, declarationOrder: true,
            isSimpleType: (classId, _) => { asked.Add(classId); return false; },
            rootClassId: "Lib.Test");

        Assert.Contains("Lib.Test", asked);
        Assert.Contains("Lib.Test.Inner", asked);
    }

    [Fact]
    public void TheRendererAndTheRuleAgree()
    {
        // The guard that makes the pair safe: format the class, then check the result. Whatever the
        // renderer produced, the rule must have nothing to say about it.
        Func<string, string, bool> isSimpleType = (_, type) => type == "SI.Length";

        var formatted = TestHelpers.FormatCode("""
            model Test
              Resistor r;
              SI.Length len;
              Real x;
              parameter Real m;
              constant Real g;
              output Real y;
              input Real u;
            end Test;
            """,
            onlyOneOfEachSection: true, declarationOrder: true,
            isSimpleType: isSimpleType, rootClassId: "Lib.Test");

        var rule = new StyleRules.DeclarationOrder("Lib", isSimpleType);
        rule.Visit(ModelicaParserHelper.Parse(formatted));

        Assert.Empty(rule.RuleFindings);
    }
}

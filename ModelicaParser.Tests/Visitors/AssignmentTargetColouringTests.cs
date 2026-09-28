using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.Visitors;

/// <summary>
/// B254 — the variable on the left of an assignment is a variable, not a function call.
///
/// <para>Reported against <c>y_dd</c> in
/// <c>Modelica.Mechanics.MultiBody.Frames.Internal.maxWithoutEvent_dd</c>, whose whole algorithm
/// section is one line: <c>y_dd := if u1 &gt; u2 then u1_dd else u2_dd;</c>. The target came out in
/// the function colour.</para>
///
/// <para>The grammar is <c>statement : component_reference (':=' expression | function_call_args)</c>
/// — so the reference is the thing being called in one form and the thing being assigned to in the
/// other, and both were being marked as a call. The equation path next to it had always asked
/// whether the call arguments were there; the statement path had not.</para>
///
/// <para><b>Fixed on both sides deliberately.</b> The viewer is coloured by
/// <see cref="ModelicaTokenClassifier"/> since B215, so that is what a user sees — but the renderer
/// is the independent implementation the classifier is measured against, and a baseline with a known
/// defect in it has to be explained every time it is used. The classifier had mirrored this one on
/// purpose, with a comment saying so.</para>
/// </summary>
public class AssignmentTargetColouringTests
{
    /// <summary>The reported class, reduced to the part that matters.</summary>
    private const string MaxWithoutEventDd = """
        function maxWithoutEvent_dd "First derivative"
          input Real u1;
          input Real u2;
          input Real u1_dd;
          input Real u2_dd;
          output Real y_dd;
        algorithm
          y_dd := if u1 > u2 then u1_dd else u2_dd;
        end maxWithoutEvent_dd;
        """;

    private static string Rendered(string source)
    {
        var tree = ModelicaParserHelper.Parse(source);
        Assert.NotNull(tree);

        var renderer = new ModelicaRenderer(
            renderForCodeEditor: true, showAnnotations: true, excludeClassDefinitions: false,
            tokenStream: null, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.Visit(tree);
        return string.Join("\n", renderer.Code);
    }

    private static string Classified(string source) =>
        string.Join("\n", ModelicaTokenClassifier.Highlight(source));

    [Fact]
    public void TheAssignmentTargetIsAnIdentifier_InTheViewer()
    {
        var classified = Classified(MaxWithoutEventDd);

        Assert.Contains("<IDENT>y_dd</IDENT>", classified);
        Assert.DoesNotContain("<FUNCTION>y_dd</FUNCTION>", classified);
    }

    [Fact]
    public void TheAssignmentTargetIsAnIdentifier_InTheRenderer()
    {
        // The measurement baseline agrees, so the two do not have to be explained apart.
        var rendered = Rendered(MaxWithoutEventDd);

        Assert.Contains("<IDENT>y_dd</IDENT>", rendered);
        Assert.DoesNotContain("<FUNCTION>y_dd</FUNCTION>", rendered);
    }

    [Fact]
    public void ACallStatementIsStillAFunction()
    {
        // The control, and the distinction the grammar draws: with no `:=` the reference *is* the
        // thing being called, and colouring it as a variable would be the same bug inverted.
        const string Source = """
            function F
            algorithm
              doSomething(1, 2);
            end F;
            """;

        Assert.Contains("<FUNCTION>doSomething</FUNCTION>", Classified(Source));
        Assert.Contains("<FUNCTION>doSomething</FUNCTION>", Rendered(Source));
    }

    [Fact]
    public void AFunctionCalledOnTheRightOfAnAssignmentIsStillAFunction()
    {
        // Both in one statement: the target is a variable and the callee is not.
        const string Source = """
            function F
            algorithm
              y := someFunction(x);
            end F;
            """;

        var classified = Classified(Source);
        Assert.Contains("<IDENT>y</IDENT>", classified);
        Assert.Contains("<FUNCTION>someFunction</FUNCTION>", classified);
        Assert.DoesNotContain("<FUNCTION>y</FUNCTION>", classified);
    }

    [Fact]
    public void AMultipleOutputCallIsStillAFunction()
    {
        // `(a, b) := f(x)` is the third form of the statement rule, and there the reference after
        // the := really is the callee.
        const string Source = """
            function F
            algorithm
              (a, b) := split(x);
            end F;
            """;

        var classified = Classified(Source);
        Assert.Contains("<FUNCTION>split</FUNCTION>", classified);
        Assert.DoesNotContain("<FUNCTION>a</FUNCTION>", classified);
    }

    [Fact]
    public void AnEquationTargetWasNeverAffected()
    {
        // The equation path always asked whether the call arguments were there. Asserted so that a
        // later tidy-up of the two paths into one cannot quietly take the statement answer.
        const string Source = """
            model M
              Real y;
            equation
              y = 1;
            end M;
            """;

        Assert.Contains("<IDENT>y</IDENT>", Classified(Source));
        Assert.DoesNotContain("<FUNCTION>y</FUNCTION>", Classified(Source));
    }
}

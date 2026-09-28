using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.Visitors;

/// <summary>
/// B232 — <c>ModelicaRenderer</c> coloured array subscripts as function calls.
///
/// <para><c>_isFunction</c> was a mutable field used as though it were a scoped fact, and it showed
/// in two ways. It <b>leaked into subscripts</b>: a component reference visits its
/// <c>array_subscripts</c> while the flag is still set, so the <c>i</c> in <c>den2[i] := …</c> was
/// marked as a call — 377 tokens in the Modelica Standard Library and 512 in Buildings. And a
/// <b>nested call cleared it</b> for the rest of the reference, because the inner call reset the
/// flag to false on the way out instead of restoring what it found.</para>
///
/// <para>Found by the measurement behind B213: the token classifier agrees with the renderer's
/// colouring on 99.998% of 2.2M word tokens, and this was the entire residue. The classifier was
/// written not to reproduce it, so fixing the renderer is what brings the two into agreement rather
/// than leaving one of them deliberately wrong.</para>
///
/// <para><b>The renderer's colouring is no longer what the user sees</b> — the viewer and both diff
/// views are coloured by the classifier since B215, and every production call site passes
/// <c>renderForCodeEditor: false</c>. It is kept because it is the independent second implementation
/// the classifier is measured against, and a baseline with a known defect in it is a baseline that
/// has to be explained every time it is used.</para>
/// </summary>
public class SubscriptColouringTests
{
    private static string Render(string code)
    {
        var tree = ModelicaParserHelper.Parse(code);
        Assert.NotNull(tree);

        var renderer = new ModelicaRenderer(
            renderForCodeEditor: true,
            showAnnotations: true,
            excludeClassDefinitions: false,
            tokenStream: null,
            classNamesToExclude: null,
            formatting: FormattingOptions.None);
        renderer.Visit(tree);
        return string.Join("\n", renderer.Code);
    }

    [Fact]
    public void ASubscriptOnAnAssignmentTarget_IsNotAFunctionCall()
    {
        var rendered = Render("""
function F
algorithm
  den2[i] := 1;
end F;
""");

        Assert.Contains("<IDENT>i</IDENT>", rendered);
        Assert.DoesNotContain("<FUNCTION>i</FUNCTION>", rendered);
    }

    [Fact]
    public void ASubscriptInAFunctionCall_IsNotAFunctionCall()
    {
        var rendered = Render("""
function F
algorithm
  y := g(x[i]);
end F;
""");

        Assert.Contains("<IDENT>i</IDENT>", rendered);
        Assert.DoesNotContain("<FUNCTION>i</FUNCTION>", rendered);
    }

    [Fact]
    public void ACallInsideASubscript_IsStillAFunctionCall()
    {
        // The subscript is not part of the outer call, but a call written inside one is still a
        // call: clearing the flag must not go so far as to stop colouring what is really there.
        var rendered = Render("""
function F
algorithm
  m[integer(i)] := 1;
end F;
""");

        Assert.Contains("<FUNCTION>integer</FUNCTION>", rendered);
    }

    [Fact]
    public void ACallInsideASubscript_DoesNotDisturbWhatFollowsIt()
    {
        // The second facet: the inner call used to reset the flag to false on the way out rather
        // than restoring it, so everything after it in the outer reference changed colour.
        var rendered = Render("""
function F
algorithm
  m[integer(i), j] := 1;
end F;
""");

        Assert.Contains("<IDENT>j</IDENT>", rendered);
        Assert.DoesNotContain("<FUNCTION>j</FUNCTION>", rendered);
    }

    [Fact]
    public void TheFunctionBeingCalled_IsStillColouredAsOne()
    {
        // The control. Every assertion above is about something *not* being marked as a call, so
        // without this they would all pass if the colouring stopped working altogether.
        var rendered = Render("""
function F
algorithm
  y := someFunction(x);
end F;
""");

        Assert.Contains("<FUNCTION>someFunction</FUNCTION>", rendered);
    }
}

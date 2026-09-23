using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.Visitors;

/// <summary>
/// B233 — taking annotations out of the text, including the ones that share a line with code.
///
/// <para><see cref="ElisionFinder.Annotations"/> hides a construct as a unit or not at all, which
/// left every <c>connect(...) annotation (Line(...))</c> on screen for a user who had asked for no
/// annotations. Measured over 8,367 files of real Modelica, that was <b>62% of the
/// annotation-bearing lines in equation sections</b> — the section holding most of the noise.</para>
///
/// <para>These pin the two properties the viewer depends on: what comes back is still Modelica, and
/// every surviving line keeps the number it had.</para>
/// </summary>
public class WithoutAnnotationsTests
{
    /// <summary>Line endings normalised — these files are CRLF and a raw literal picks that up.</summary>
    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static (string[] Spliced, ModelicaParser.Helpers.SourceElision Elision) Without(string source)
    {
        var lf = Lf(source);
        var (spliced, elision) = ElisionFinder.WithoutAnnotations(ModelicaParserHelper.Parse(lf), lf);
        return (spliced.Split('\n'), elision);
    }

    [Fact]
    public void AnAnnotationSharingALineWithCodeIsCutOutOfIt()
    {
        var (spliced, elision) = Without("""
            model M "m"
            equation
              connect(a.p, b.n) annotation (Line(points={{-10,0},{10,0}}));
            end M;
            """);

        Assert.Equal("  connect(a.p, b.n);", spliced[2]);

        // Nothing was dropped — the line is still there, just shorter.
        Assert.Empty(elision.Ranges);
    }

    [Fact]
    public void AnAnnotationOnItsOwnLinesIsReportedRatherThanCut()
    {
        // The existing mechanism still does this half: the line goes entirely, so there is nothing
        // to splice and the caller drops it.
        var (spliced, elision) = Without("""
            model M "m"
              Real x;
              annotation (Documentation(info="<html>x</html>"));
            end M;
            """);

        Assert.Equal(4, spliced.Length);
        Assert.Equal([new ElidedRange(3, 3, null)], elision.Ranges);
    }

    [Fact]
    public void AnAnnotationRunningAcrossLinesIsJoinedOntoItsFirst()
    {
        var (spliced, elision) = Without("""
            model M "m"
            equation
              connect(a.p, b.n) annotation (Line(
                points={{-10,0},{10,0}},
                color={0,0,255}));
            end M;
            """);

        Assert.Equal("  connect(a.p, b.n);", spliced[2]);
        Assert.Equal([new ElidedRange(4, 5, null)], elision.Ranges);
    }

    /// <summary>
    /// The defect that the corpus measurement found and no small test would have: the continuation
    /// lines were reported as elided but left in the text, so what came back was the orphaned tail
    /// of an annotation whose head had gone. <b>3,199 of 8,367 real files stopped parsing</b>, which
    /// in the viewer means silently dropping from parse-tree colouring to lexer-only.
    /// </summary>
    [Fact]
    public void WhatComesBackIsStillModelica()
    {
        var lf = Lf("""
            model M "m"
            equation
              connect(a.p, b.n) annotation (Line(
                points={{-10,0},{10,0}},
                color={0,0,255}));
            end M;
            """);

        var (spliced, _) = ElisionFinder.WithoutAnnotations(ModelicaParserHelper.Parse(lf), lf);

        var (_, _, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(spliced);
        Assert.True(errors.Count == 0,
            "the spliced text must parse — the colouring comes from the tree: "
            + string.Join("; ", errors.Select(e => $"{e.Line}: {e.Message}")));
    }

    [Fact]
    public void EverySurvivingLineKeepsTheNumberItHad()
    {
        // What makes a finding still clickable. The line count never changes, so the caller's
        // existing map from display line to source line is the only arithmetic involved.
        var lf = Lf("""
            model M "m"
            equation
              connect(a.p, b.n) annotation (Line(
                points={{-10,0},{10,0}}));
              der(x) = -x;
            end M;
            """);

        var (spliced, _) = ElisionFinder.WithoutAnnotations(ModelicaParserHelper.Parse(lf), lf);

        Assert.Equal(lf.Split('\n').Length, spliced.Split('\n').Length);
        Assert.Equal("  der(x) = -x;", spliced.Split('\n')[4]);
    }

    [Fact]
    public void TwoAnnotationsOnOneLineBothGo()
    {
        // Unusual but legal, and the reason the splice runs right to left: cutting the first would
        // move the columns the second was measured at.
        var (spliced, _) = Without("""
            model M "m"
              Real x annotation (Dialog(group="a")); Real y annotation (Dialog(group="b"));
            end M;
            """);

        Assert.Equal("  Real x; Real y;", spliced[1]);
    }

    [Fact]
    public void AClassWithNoAnnotationsComesBackUnchanged()
    {
        var lf = Lf("""
            model M "m"
              Real x;
            equation
              der(x) = -x;
            end M;
            """);

        var (spliced, elision) = ElisionFinder.WithoutAnnotations(ModelicaParserHelper.Parse(lf), lf);

        Assert.Equal(lf, spliced);
        Assert.Empty(elision.Ranges);
    }

    [Fact]
    public void NoTreeMeansNoChange()
    {
        var (spliced, elision) = ElisionFinder.WithoutAnnotations(null, "model M \"m\" end M;");

        Assert.Equal("model M \"m\" end M;", spliced);
        Assert.Empty(elision.Ranges);
    }

    // ---------------------------------------------------------------------------------------
    // elidedTextMustParse — B218. The viewer never parses the elided text, only the spliced
    // source; get_class_source hands the elided text to an agent that edits it and gives it back.
    // The difference between the two is one character: who owns the semicolon after an annotation.
    // ---------------------------------------------------------------------------------------

    private static (string[] Spliced, SourceElision Elision) MustParse(string source)
    {
        var lf = Lf(source);
        var (spliced, elision) =
            ElisionFinder.WithoutAnnotations(ModelicaParserHelper.Parse(lf), lf, elidedTextMustParse: true);
        return (spliced.Split('\n'), elision);
    }

    /// <summary>What comes out once the elided lines really are dropped.</summary>
    private static string Elided(string source)
    {
        var (spliced, elision) = MustParse(source);
        return string.Join('\n', elision.Apply(spliced));
    }

    [Fact]
    public void ADeclarationKeepsTheSemicolonItsHiddenAnnotationSatOn()
    {
        // The whole of B218's remaining sting. `Real y "output"` followed by a line holding only
        // `annotation (...);` loses its terminator when that line goes, and runs into whatever is
        // declared next.
        var (spliced, elision) = MustParse("""
            model M "m"
              Real y "output"
                annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
              Real z;
            end M;
            """);

        Assert.Equal("    ;", spliced[2]);
        Assert.Empty(elision.Ranges);
    }

    [Fact]
    public void AClassLevelAnnotationTakesItsOwnSemicolonWithIt()
    {
        // The other side of the same decision: here the semicolon is the annotation's, and leaving
        // it behind puts a bare `;` in the class body, which is not an element.
        var (_, elision) = MustParse("""
            model M "m"
              Real x;
              annotation (Icon(graphics={Rectangle(extent={{-1,-1},{1,1}})}));
            end M;
            """);

        Assert.Equal([new ElidedRange(3, 3, null)], elision.Ranges);
    }

    [Theory]
    [InlineData("""
        model M "m"
          Real y "output"
            annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
          Real z;
          annotation (Icon(graphics={Rectangle(extent={{-1,-1},{1,1}})}));
        end M;
        """)]
    [InlineData("""
        model M "m"
          extends Base
            annotation (choicesAllMatching=true);
          Real z;
        end M;
        """)]
    [InlineData("""
        function F "f"
          input Real u;
          output Real v;
        external "C" v = f_impl(u)
          annotation (Library="mylib",
            Include="#include <f_impl.h>");
        end F;
        """)]
    [InlineData("""
        model M "m"
        equation
          connect(a.p, b.n) annotation (Line(
            points={{-10,0},{10,0}}));
        end M;
        """)]
    public void TheElidedTextParses(string source)
    {
        var (_, _, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(Elided(source));

        Assert.True(errors.Count == 0,
            "dropping the elided lines must leave Modelica: "
            + string.Join("; ", errors.Select(e => $"{e.Line}: {e.Message}")));
    }

    [Fact]
    public void AClassAnnotationSharingItsLineWithCodeTakesItsSemicolonToo()
    {
        // Spliced rather than elided, because the line is not the annotation's alone - and the
        // semicolon still has to go with it, or the composition is left holding a bare `;`.
        var (spliced, _) = MustParse("model M \"m\" Real x; annotation (Icon()); end M;");

        Assert.Equal("model M \"m\" Real x; end M;", spliced[0]);
    }

    [Fact]
    public void TheViewerStillDropsTheLineWholeByDefault()
    {
        // Left as it was on purpose: keeping the semicolon puts a line holding nothing but `;` on
        // screen for every hidden declaration annotation, which is the per-annotation noise B233
        // was asked to remove.
        var (spliced, elision) = Without("""
            model M "m"
              Real y "output"
                annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
              Real z;
            end M;
            """);

        Assert.Equal("    annotation (Placement(transformation(extent={{-10,-10},{10,10}})));", spliced[2]);
        Assert.Equal([new ElidedRange(3, 3, null)], elision.Ranges);
    }
}

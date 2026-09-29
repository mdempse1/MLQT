using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B185 — a very large class was reported as still not rendered after five minutes.
///
/// <para>The backlog named two candidate costs and told us to measure which one before addressing
/// either: "the syntax highlighting over a very large token stream, and the reformat the page
/// performs in order to colour it". <b>It is neither.</b> B215 removed the reformat, and the
/// measurement then showed the highlighting costs 20–46 ms at every size tried. What costs is the
/// <b>parse</b>, and it is quadratic in the number of annotated declarations in one class: 1.5 s at
/// 1,503 lines, 4.6 s at 3,003, 18 s at 6,003 and 72 s at 12,003. The same declarations without
/// annotations are linear (86 ms for 4,000 of them).</para>
///
/// <para>So the answer is not to stop highlighting a large class but to stop <em>waiting</em> for
/// its parse: the lexer paints it at once and the tree's colouring replaces that when it lands.
/// These tests hold the two halves of that — that the unparsed paint is the class, and that it is
/// the same text the parsed one shows.</para>
/// </summary>
public class CodeReviewLargeClassTests
{
    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static (ModelNode Model, DirectedGraph Graph) Load(string source)
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "Big.mo", source);
        return (graph.GetNode<ModelNode>("Big")!, graph);
    }

    private static readonly string Annotated = Lf("""
        model Big "a large class"
          parameter Real p0 = 0.0 "p0" annotation (
            Placement(transformation(extent={{-10,-10},{10,10}})));
          parameter Real p1 = 1.0 "p1" annotation (
            Placement(transformation(extent={{-10,-10},{10,10}})));
        end Big;
        """);

    [Fact]
    public void PaintingWithoutParsingShowsTheSameTextAsPaintingWithIt()
    {
        // The text is the user's either way - only the colouring differs - which is what makes it
        // safe to show one and then the other.
        var (model, graph) = Load(Annotated);

        var withParse = CodeReview.Show(model, graph, true, showAnnotations: true,
            hideClassDefinitions: false, parse: true);
        var withoutParse = CodeReview.Show(model, graph, true, showAnnotations: true,
            hideClassDefinitions: false, parse: false);

        Assert.Equal(Strip(withParse.Lines), Strip(withoutParse.Lines));
    }

    [Fact]
    public void PaintingWithoutParsingStillColoursWhatTheLexerKnows()
    {
        var (model, graph) = Load(Annotated);

        var shown = CodeReview.Show(model, graph, true, showAnnotations: true,
            hideClassDefinitions: false, parse: false);

        // Keywords, strings and numbers are lexical and come out coloured.
        Assert.Contains("<KEYWORD>model</KEYWORD>", shown.Lines[0]);
        Assert.Contains("<STRING>\"a large class\"</STRING>", shown.Lines[0]);
    }

    [Fact]
    public void WhatOnlyTheTreeKnowsIsWhatArrivesLate()
    {
        // The honest statement of what the first paint is missing: `Real` is a type and `Placement`
        // is a call, and neither is knowable without the tree. Everything else is already right.
        var (model, graph) = Load(Annotated);

        var withParse = CodeReview.Show(model, graph, true, true, false, parse: true);
        var withoutParse = CodeReview.Show(model, graph, true, true, false, parse: false);

        Assert.Contains("<TYPE>Real</TYPE>", string.Join("\n", withParse.Lines));
        Assert.DoesNotContain("<TYPE>Real</TYPE>", string.Join("\n", withoutParse.Lines));
        Assert.Contains("<IDENT>Real</IDENT>", string.Join("\n", withoutParse.Lines));
    }

    [Fact]
    public void NothingIsHiddenUntilThereIsATreeToFindItIn()
    {
        // The lexer cannot hide anything, which is why FirstPaint declines to paint a class that
        // has something to hide (B402, below) rather than showing it and collapsing it later.
        var (model, graph) = Load(Annotated);

        var withoutParse = CodeReview.Show(model, graph, true, showAnnotations: false,
            hideClassDefinitions: true, parse: false);

        Assert.True(withoutParse.Elision.IsEmpty);
    }

    // ── B402: the first paint never shows what the parse is about to hide ─────────────

    private static readonly string PackageWithNestedClass = Lf("""
        package Big "a large package"
          model Inner "nested"
            Real x;
          end Inner;
          constant Real k = 1;
        end Big;
        """);

    private static readonly string PackageWithoutNestedClass = Lf("""
        package Big "a large package"
          constant Real k = 1;
        end Big;
        """);

    [Fact]
    public void APackageWhoseNestedClassesAreHiddenIsNotPaintedBeforeTheParse()
    {
        // Painted, Inner appeared in full and then collapsed to one line when the parse landed.
        var (model, graph) = Load(PackageWithNestedClass);

        Assert.Null(CodeReview.FirstPaint(model, graph, true, showAnnotations: true, hideClassDefinitions: true));
    }

    [Fact]
    public void APackageWithNothingNestedIsStillPaintedAtOnce()
    {
        var (model, graph) = Load(PackageWithoutNestedClass);

        Assert.NotNull(CodeReview.FirstPaint(model, graph, true, showAnnotations: true, hideClassDefinitions: true));
    }

    [Fact]
    public void NestedClassesThatAreShownAreNoReasonToWait()
    {
        var (model, graph) = Load(PackageWithNestedClass);

        Assert.NotNull(CodeReview.FirstPaint(model, graph, true, showAnnotations: true, hideClassDefinitions: false));
    }

    [Fact]
    public void AClassWhoseAnnotationsAreHiddenIsNotPaintedBeforeTheParse()
    {
        var (model, graph) = Load(Annotated);

        Assert.Null(CodeReview.FirstPaint(model, graph, true, showAnnotations: false, hideClassDefinitions: false));
    }

    [Fact]
    public void TheCaseB185WasForStillPaintsAtOnce()
    {
        // A model with annotations shown hides nothing, and its paint is the same one Show gives.
        var (model, graph) = Load(Annotated);

        var first = CodeReview.FirstPaint(model, graph, true, showAnnotations: true, hideClassDefinitions: false);

        Assert.NotNull(first);
        Assert.Equal(CodeReview.Show(model, graph, true, true, false, parse: false).Lines, first.Lines);
    }

    [Fact]
    public void TheWordAnnotationInACommentOrStringIsNotAnAnnotation()
    {
        var (model, graph) = Load(Lf("""
            model Big "no annotation here"
              // an annotation would go here
              Real x;
            end Big;
            """));

        Assert.NotNull(CodeReview.FirstPaint(model, graph, true, showAnnotations: false, hideClassDefinitions: false));
    }

    [Fact]
    public void TheThresholdLeavesOrdinaryClassesOnTheDirectPath()
    {
        // 64 KB. Measured against two real libraries: 52 classes in the Modelica Standard Library
        // and 13 in Buildings are bigger than this, out of 13,997.
        Assert.Equal(64 * 1024, CodeReview.PaintBeforeParsingAbove);
        Assert.True(Annotated.Length < CodeReview.PaintBeforeParsingAbove);
    }

    // ---- a class too long for the browser to lay out -------------------------------------------
    // A 106,354-line Dymola FMU import model left the webview's renderer at a full core for over
    // five minutes, with the app frozen behind it; MLQT's own part took 1.7 s.

    /// <summary>A model of <paramref name="lines"/> lines: its header, declarations, and its end.</summary>
    private static (ModelNode Model, DirectedGraph Graph) OfLines(int lines) =>
        Load("model Big\n"
             + string.Concat(Enumerable.Range(0, lines - 2).Select(i => $"  Real x{i};\n"))
             + "end Big;\n");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClassPastTheLimit_IsCutToItsFirstLines_AndSaysHowManyWereLeftOff(bool parse)
    {
        // Both paths: the first paint puts the class on screen too, and it was the first of the two
        // renders the stalled class went through.
        var (model, graph) = OfLines(CodeReview.MaxShownLines + 25);

        var shown = CodeReview.Show(model, graph, true, true, false, parse);

        Assert.Equal(CodeReview.MaxShownLines, shown.Lines.Count);
        Assert.Equal(25, shown.OmittedLines);
        // The first lines, still where they were: a finding's line maps to the same place.
        Assert.Equal("model Big", Strip([shown.Lines[0]])[0].Trim());
        Assert.Equal($"Real x{CodeReview.MaxShownLines - 2};", Strip([shown.Lines[^1]])[0].Trim());
    }

    [Fact]
    public void AClassAtTheLimit_IsShownWhole()
    {
        var (model, graph) = OfLines(CodeReview.MaxShownLines);

        var shown = CodeReview.Show(model, graph, true, true, false, parse: false);

        Assert.Equal(CodeReview.MaxShownLines, shown.Lines.Count);
        Assert.Equal(0, shown.OmittedLines);
        Assert.Equal("end Big;", Strip([shown.Lines[^1]])[0].Trim());
    }

    [Fact]
    public void TheNotice_SaysHowLongTheClassIsAndWhereTheRestIs()
    {
        var notice = CodeReview.OmittedLinesNotice(5_000, 101_354, "Engines/Examples/Big_fmu.mo");

        Assert.NotNull(notice);
        Assert.Contains($"{106_354:N0} lines", notice);   // in the reader's own number format
        Assert.Contains($"first {5_000:N0}", notice);
        Assert.Contains("Engines/Examples/Big_fmu.mo", notice);
    }

    [Fact]
    public void TheNotice_IsOnlyForAClassThatWasCutShort()
    {
        Assert.Null(CodeReview.OmittedLinesNotice(500, 0, "Big.mo"));
        Assert.Contains("the file", CodeReview.OmittedLinesNotice(5_000, 1, null));
    }

    private static List<string> Strip(List<string> lines) =>
        [.. lines.Select(l => System.Text.RegularExpressions.Regex.Replace(
            l, @"</?(KEYWORD|TYPE|IDENT|NAME|FUNCTION|OPERATOR|NUMBER|STRING|COMMENT)>", ""))];
}

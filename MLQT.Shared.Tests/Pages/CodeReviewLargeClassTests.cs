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
        return (graph.ModelNodes.First(), graph);
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
        // The first paint of a package shows its nested classes, and they collapse when the parse
        // lands. Stated here so the jump is a decision rather than a surprise.
        var (model, graph) = Load(Annotated);

        var withoutParse = CodeReview.Show(model, graph, true, showAnnotations: false,
            hideClassDefinitions: true, parse: false);

        Assert.True(withoutParse.Elision.IsEmpty);
    }

    [Fact]
    public void TheThresholdLeavesOrdinaryClassesOnTheDirectPath()
    {
        // 64 KB. Measured against two real libraries: 52 classes in the Modelica Standard Library
        // and 13 in Buildings are bigger than this, out of 13,997.
        Assert.Equal(64 * 1024, CodeReview.PaintBeforeParsingAbove);
        Assert.True(Annotated.Length < CodeReview.PaintBeforeParsingAbove);
    }

    private static List<string> Strip(List<string> lines) =>
        [.. lines.Select(l => System.Text.RegularExpressions.Regex.Replace(
            l, @"</?(KEYWORD|TYPE|IDENT|NAME|FUNCTION|OPERATOR|NUMBER|STRING|COMMENT)>", ""))];
}

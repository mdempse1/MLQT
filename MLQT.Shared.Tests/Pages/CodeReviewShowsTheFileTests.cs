using System.Text.RegularExpressions;
using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// What B215 changed, asserted where it can be: the Code Review page shows the class <b>as the user
/// wrote it</b> rather than a reformatted copy.
///
/// <para>Measured before the change, against two libraries MLQT has never formatted: only 3.7–5.8%
/// of displayed lines were the file's line with the file's text on it, and the document was 17–24%
/// longer than the file. That is why a finding's line number never matched what was on screen
/// (B182) and why nothing could scroll to it (B183) — both of which these tests now pin as
/// identities rather than mappings.</para>
/// </summary>
public class CodeReviewShowsTheFileTests
{
    /// <summary>
    /// Deliberately not laid out the way the formatter would lay it out — extra spaces, an odd
    /// indent, a blank line — because a class the renderer happens to reproduce cannot tell the old
    /// behaviour from the new.
    /// </summary>
    private static readonly string LooselyFormatted = Lf("""
        model M "a model"
          parameter Real    k =  1   "loosely spaced";

              Real x;
        equation
          x = k*time;
        end M;
        """);

    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static readonly Regex TagPair =
        new(@"<(KEYWORD|TYPE|IDENT|NAME|FUNCTION|OPERATOR|NUMBER|STRING|COMMENT)>(.*?)</\1>",
            RegexOptions.Compiled);

    /// <summary>The markup as the user sees it: tags gone, entities undone outside them.</summary>
    private static string Text(string markup)
    {
        var text = new System.Text.StringBuilder();
        var last = 0;
        foreach (Match match in TagPair.Matches(markup))
        {
            text.Append(System.Net.WebUtility.HtmlDecode(markup[last..match.Index]));
            text.Append(match.Groups[2].Value);
            last = match.Index + match.Length;
        }
        text.Append(System.Net.WebUtility.HtmlDecode(markup[last..]));
        return text.ToString();
    }

    private static (ModelNode Model, DirectedGraph Graph) Load(string source, string file = "M.mo")
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, file, source);
        return (graph.ModelNodes.First(), graph);
    }

    // ── the class, as written ─────────────────────────────────────────────────────

    [Fact]
    public void EveryLineOnScreenIsTheLineTheUserWrote()
    {
        var (model, graph) = Load(LooselyFormatted);

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: false);

        Assert.Equal(Lf(LooselyFormatted).Split('\n'), shown.Lines.Select(Text));
    }

    [Fact]
    public void TheSameIsTrueWithHighlightingTurnedOff()
    {
        var (model, graph) = Load(LooselyFormatted);

        var shown = CodeReview.Show(model, graph, showHighlighted: false, showAnnotations: true,
            hideClassDefinitions: false);

        Assert.Equal(Lf(LooselyFormatted).Split('\n'), shown.Lines.Select(Text));
    }

    /// <summary>
    /// The identity B182 was asking for. A finding's line is counted against the class's own source,
    /// and now that is what is on screen — so line <c>n</c> of the finding is line <c>n</c> of the
    /// viewer, with no map in between.
    /// </summary>
    [Fact]
    public void AFindingsLineIsTheLineItIsShowingAt()
    {
        var (model, graph) = Load(LooselyFormatted);

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: false);

        // Line 2 of the class is the parameter declaration, and it is line 2 of the display.
        Assert.Contains("parameter Real", Text(shown.Lines[1]));
        Assert.Equal(2, shown.Elision.ToDisplayLine(2));
        Assert.True(shown.Elision.IsEmpty);
    }

    [Fact]
    public void AClassThatDoesNotParseIsStillShownExactly()
    {
        // This used to take a separate path that showed the file with no colour at all.
        const string broken = "model Broken \"unterminated\n  Real x;\nend Broken;";
        var (model, graph) = Load(broken, "Broken.mo");

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: false);

        Assert.Equal(Lf(model.Definition.ModelicaCode!).Split('\n'), shown.Lines.Select(Text));
    }

    /// <summary>
    /// When the stored text is no longer the file's, the page re-reads the file — and when it
    /// cannot, it shows the stored text rather than nothing. The re-read itself is
    /// <c>ClassSourceTests</c>; this is the fallback, which is a behaviour and not an accident:
    /// stale text beats a blank pane, and an exception in a viewer beats neither.
    /// </summary>
    [Fact]
    public void StoredTextIsShownWhenTheFileCannotBeReadAgain()
    {
        var (model, graph) = Load(LooselyFormatted);
        var asWritten = model.Definition.ModelicaCode!;

        // What the package trimmer and the formatter do to a class's stored source.
        model.Definition.ModelicaCode = "model M \"rewritten\"\nend M;";
        model.SourceMatchesFile = false;

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: false);

        // No file on disk here, so it falls back to the stored text rather than throwing — the
        // fallback being the behaviour, not an accident.
        Assert.Equal("model M \"rewritten\"", Text(shown.Lines[0]));
        Assert.NotEqual(asWritten, string.Join("\n", shown.Lines.Select(Text)));
    }

    // ── hiding, and finding your way back ─────────────────────────────────────────

    private static readonly string Package = Lf("""
        package P "a package"
          model A "first"
            Real x;
          end A;
          model B "second"
            Real y;
          end B;
        end P;
        """);

    [Fact]
    public void APackageShowsItsNestedClassesCollapsed()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "P.mo", Package);
        var package = graph.GetNode<ModelNode>("P")!;

        var shown = CodeReview.Show(package, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: true);

        Assert.Equal(
            ["package P \"a package\"", "  // A …", "  // B …", "end P;"],
            shown.Lines.Select(Text));
    }

    [Fact]
    public void ACollapsedPackageStillKnowsWhereEachLineCameFrom()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "P.mo", Package);
        var package = graph.GetNode<ModelNode>("P")!;
        var source = Package.Split('\n');

        var shown = CodeReview.Show(package, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: true);

        // The last line on screen is the last line of the class, not the fourth.
        Assert.Equal(source.Length, shown.Elision.ToSourceLine(shown.Lines.Count));

        // And a finding on a line inside a collapsed class points at its marker rather than
        // nowhere, which is what makes it clickable.
        Assert.Equal(2, shown.Elision.ToDisplayLine(3));
    }

    [Fact]
    public void HidingAnnotationsTakesOutTheOnesWrittenOnTheirOwnLines()
    {
        var source = Lf("""
            model M "m"
              Real x;
              annotation (Documentation(info="<html>gone</html>"));
            end M;
            """);
        var (model, graph) = Load(source);

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: false,
            hideClassDefinitions: false);

        // No marker. There used to be a `// annotation …` line here so the reader could see that
        // something was hidden, and it cost a line per annotation — noisier than the thing it hid,
        // which is what was asked for in B233. The toolbar button shows the toggle is on.
        Assert.Equal(
            ["model M \"m\"", "  Real x;", "end M;"],
            shown.Lines.Select(Text));
    }

    [Fact]
    public void HidingAnnotationsAlsoTakesOutTheOnesSharingALineWithCode()
    {
        // The case B233 was reported for, and the common one: a `connect` writes its annotation on
        // the same line, so hiding annotations used to change nothing at all in an equation section
        // — measured, 62% of the annotation-bearing equation lines in MSL and Buildings stayed.
        var source = Lf("""
            model M "m"
              Real x annotation (Dialog(group="g"));
            equation
              connect(a.p, b.n) annotation (Line(points={{-10,0},{10,0}}));
            end M;
            """);
        var (model, graph) = Load(source);

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: false,
            hideClassDefinitions: false);

        Assert.Equal(
            ["model M \"m\"", "  Real x;", "equation", "  connect(a.p, b.n);", "end M;"],
            shown.Lines.Select(Text));
    }

    [Fact]
    public void AnInlineAnnotationRunningAcrossLinesTakesItsOwnLinesWithIt()
    {
        // The awkward shape: it starts beside code and ends beside a semicolon, so it is neither
        // whole-line nor confined to one. What is left of the first line is joined to what is left
        // of the last, and the lines between are dropped — which keeps every surviving line at the
        // number it had, so a finding still points at the right place.
        var source = Lf("""
            model M "m"
            equation
              connect(a.p, b.n) annotation (Line(
                points={{-10,0},{10,0}},
                color={0,0,255}));
            end M;
            """);
        var (model, graph) = Load(source);

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: false,
            hideClassDefinitions: false);

        Assert.Equal(
            ["model M \"m\"", "equation", "  connect(a.p, b.n);", "end M;"],
            shown.Lines.Select(Text));

        // `end M;` is line 6 of the file and line 4 on screen.
        Assert.Equal(6, shown.Elision.ToSourceLine(4));
    }

    [Fact]
    public void HidingBothAtOnceDoesNotCollide()
    {
        // The annotation is inside the nested class, so the two range sets overlap and only the
        // wider one may survive. Without that, this throws rather than rendering.
        var source = Lf("""
            package P "p"
              model A "a"
                annotation (Documentation(info="<html>x</html>"));
              end A;
            end P;
            """);
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "P.mo", source);
        var package = graph.GetNode<ModelNode>("P")!;

        var shown = CodeReview.Show(package, graph, showHighlighted: true, showAnnotations: false,
            hideClassDefinitions: true);

        Assert.Equal(["package P \"p\"", "  // A …", "end P;"], shown.Lines.Select(Text));
    }

    [Fact]
    public void AnElementPrefixIsPutBackInFrontOfTheClass()
    {
        // The stored slice starts at the class keyword, so `replaceable` and `redeclare` - which sit
        // before it in the file - have to be written back on.
        var (model, graph) = Load(LooselyFormatted);
        model.ElementPrefix = "replaceable";

        var shown = CodeReview.Show(model, graph, showHighlighted: true, showAnnotations: true,
            hideClassDefinitions: false);

        Assert.StartsWith("replaceable model M", Text(shown.Lines[0]));
    }
}

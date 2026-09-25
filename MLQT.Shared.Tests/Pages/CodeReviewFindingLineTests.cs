using System.Text.RegularExpressions;
using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B339: a finding's line in a trimmed package is counted against the <b>trimmed</b> text — the
/// checker is handed the package with its inline standalone children cut out — while the viewer
/// shows the class as the file has it. The page mapped the line only through its own elision, so
/// every finding after the first inline child landed that child's length too high.
/// </summary>
public class CodeReviewFindingLineTests
{
    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static readonly string PackageWithInlineChild = Lf("""
        package P "a package"
          model A "a child stored inline"
            Real x;
            Real y;
          end A;
          constant Real k = 1;
          constant Real j = 2;
        end P;
        """);

    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    private static string Text(string markup) => System.Net.WebUtility.HtmlDecode(Tags.Replace(markup, ""));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFindingAfterAnInlineChildScrollsToItsOwnLine(bool hideClassDefinitions)
    {
        var path = Path.Combine(Path.GetTempPath(), $"FindingLineTests_{Guid.NewGuid():N}.mo");
        File.WriteAllText(path, PackageWithInlineChild);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, PackageWithInlineChild);
            var package = graph.GetNode<ModelNode>("P")!;
            PackageCodeTrimmer.TrimStandaloneChildren(graph);

            // The premise: the checker sees the trimmed text, where `constant Real j` is some line n.
            Assert.NotNull(package.TrimElision);
            var trimmedLines = package.Definition.ModelicaCode!.Split('\n');
            var findingLine = Array.FindIndex(trimmedLines, l => l.Contains("constant Real j")) + 1;
            Assert.True(findingLine > 0);

            var shown = CodeReview.Show(package, graph, showHighlighted: true, showAnnotations: true,
                hideClassDefinitions: hideClassDefinitions);

            var displayLine = CodeReview.DisplayLineOfFinding(findingLine, package, shown.Elision);

            Assert.NotNull(displayLine);
            Assert.Contains("constant Real j", Text(shown.Lines[displayLine!.Value - 1]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUntrimmedClassMapsOnlyThroughTheViewersOwnElision()
    {
        // The control: with no trim, the finding's line is the class's line and the view's elision
        // is the only map.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "M.mo", Lf("""
            model M "m"
              Real x;
            end M;
            """));
        var model = graph.ModelNodes.First();
        var elision = SourceElision.Of([new ElidedRange(1, 1, null)]);

        Assert.Null(model.TrimElision);
        Assert.Equal(1, CodeReview.DisplayLineOfFinding(2, model, elision));
        Assert.Equal(2, CodeReview.DisplayLineOfFinding(2, model, SourceElision.None));
    }
}

using MLQT.Shared.Pages;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using Xunit;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B217, which was recorded as predicted rather than observed: the class diff fed
/// <c>Definition.ModelicaCode</c> in as the working copy and a slice of the <b>file</b> at HEAD, so
/// for any class whose stored text is no longer the file's the two sides were not comparable
/// documents and the diff reported changes the user had not made.
///
/// <para>Confirmed here, and the confirmation is the test: a package whose inline standalone
/// children the trimmer has removed holds text that is missing those children entirely, so every one
/// of them would have shown as deleted.</para>
/// </summary>
public class CodeReviewDiffSourceTests
{
    private static string Lf(string s) => ModelicaParserHelper.NormalizeLineEndings(s);

    private static readonly string PackageWithInlineChildren = Lf("""
        package P "a package"
          model A "a child stored inline"
            Real x;
          end A;
          constant Real k = 1;
        end P;
        """);

    [Fact]
    public void ATrimmedPackageDiffsAgainstItsFileNotAgainstTheTrim()
    {
        var path = Path.Combine(Path.GetTempPath(), $"DiffSourceTests_{Guid.NewGuid():N}.mo");
        File.WriteAllText(path, PackageWithInlineChildren);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, PackageWithInlineChildren);
            var package = graph.GetNode<ModelNode>("P")!;

            PackageCodeTrimmer.TrimStandaloneChildren(graph);

            // The premise: the trimmer really did take the child out of the stored text, so the two
            // sides of the diff really were different documents. Since B216 the stored text is the
            // file's own lines minus the child's, which the node records as an elision — so what
            // this test is about is unchanged, and the difference is still a whole class.
            Assert.NotNull(package.TrimElision);
            Assert.DoesNotContain("model A", package.Definition.ModelicaCode);

            var workingCopy = CodeReview.WorkingCopyText(package, graph);

            // The HEAD side is the class as the file has it, so this side has to be as well - or
            // `model A` shows as a deletion nobody made.
            Assert.Contains("model A", workingCopy);
            Assert.Equal(PackageWithInlineChildren, Lf(workingCopy));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnOrdinaryClassStillDiffsAgainstItsStoredText()
    {
        // The control. For the large majority of classes the stored text *is* the file's, and this
        // change must not send them on a trip through the filesystem to find that out.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "M.mo", Lf("""
            model M "m"
              Real x;
            end M;
            """));
        var model = graph.ModelNodes.First();

        Assert.True(model.SourceMatchesFile);
        Assert.Equal(model.Definition.ModelicaCode, CodeReview.WorkingCopyText(model, graph));
    }

    [Fact]
    public void AnElementPrefixIsPutBackOnBothForTheDiff()
    {
        // The prefix sits before the class in the file and outside the stored slice, so without it
        // the working-copy side is missing a word the HEAD side has.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "M.mo", Lf("""
            model M "m"
            end M;
            """));
        var model = graph.ModelNodes.First();
        model.ElementPrefix = "replaceable";

        Assert.StartsWith("replaceable model M", CodeReview.WorkingCopyText(model, graph));
    }
}

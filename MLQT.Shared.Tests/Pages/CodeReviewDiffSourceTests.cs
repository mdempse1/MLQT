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

    // ── the HEAD side (B344) ──────────────────────────────────────────────────────

    /// <summary>Two nested classes of one short name, the shape of MSL's <c>Media/package.mo</c>.</summary>
    private static readonly string TwoOfOneName = Lf("""
        package Media "media"
          package Air "air"
            function setState "air's"
              input Real p;
            end setState;
          end Air;
          package Water "water"
            function setState "water's"
              input Real p;
            end setState;
          end Water;
        end Media;
        """);

    [Theory]
    [InlineData("Media.Air.setState", "air's")]
    [InlineData("Media.Water.setState", "water's")]
    public void TheHeadSideIsTheClassOfTheSameFullName(string id, string description)
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "package.mo", TwoOfOneName);
        var node = graph.GetNode<ModelNode>(id)!;

        var head = CodeReview.HeadSideOf(TwoOfOneName, node);

        Assert.StartsWith("function setState \"" + description + "\"", head);
    }

    [Fact]
    public void AClassMovedSinceHeadIsFoundByItsShortNameWhenThatIsUnambiguous()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "package.mo", Lf("""
            package Q
              model M "moved here"
              end M;
            end Q;
            """));
        var node = graph.GetNode<ModelNode>("Q.M")!;

        var head = CodeReview.HeadSideOf(Lf("""
            package P
              model M "was here"
              end M;
            end P;
            """), node);

        Assert.StartsWith("model M \"was here\"", head);
    }

    [Fact]
    public void ANewFileHasAnEmptyHeadSide()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "M.mo", "model M end M;");

        Assert.Equal("", CodeReview.HeadSideOf(null, graph.ModelNodes.First()));
    }

    // ── a class HEAD does not have (B410) ──────────────────────────────────────────

    /// <summary>
    /// Neither the full name nor the short name is in the file at HEAD. This returned the whole HEAD
    /// file, which for a 7.5 MB file against a ten-line class was 157,852 removed rows.
    /// </summary>
    [Fact]
    public void AClassNotInTheFileAtHeadHasNoHeadSide()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "package.mo", Lf("""
            package P
              model Renamed "new name"
              end Renamed;
            end P;
            """));
        var node = graph.GetNode<ModelNode>("P.Renamed")!;

        Assert.Null(CodeReview.HeadSideOf(Lf("""
            package P
              model Original "old name"
              end Original;
            end P;
            """), node));
    }

    [Fact]
    public void AnAmbiguousShortNameIsNotTakenForTheClass()
    {
        // Moved out of Media.Air, and HEAD has two classes of its short name: neither is it.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "package.mo", Lf("""
            package Media
              function setState "moved up"
                input Real p;
              end setState;
            end Media;
            """));
        var node = graph.GetNode<ModelNode>("Media.setState")!;

        Assert.Null(CodeReview.HeadSideOf(TwoOfOneName, node));
    }

    [Fact]
    public void AHeadFileThatDoesNotParseHasNoHeadSideForTheClass()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "M.mo", "model M end M;");

        Assert.Null(CodeReview.HeadSideOf("this is not Modelica at all ((((", graph.ModelNodes.First()));
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

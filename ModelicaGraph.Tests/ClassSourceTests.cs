using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The re-slice rule, which the design that asked for it got wrong in two ways at once — and both
/// were silent. Slicing a file the way <c>ModelNode.StartIndex</c> used to describe reproduced
/// <b>0 of 13,997 classes</b> across the Modelica Standard Library and Buildings (backlog B231).
/// </summary>
public class ClassSourceTests
{
    /// <summary>
    /// Normalised at declaration, so the rest of this class can take offsets from it and build a
    /// CRLF variant of it without either operation depending on how this file happens to be stored.
    /// </summary>
    private static readonly string File = Lf("""
        within Some.Package;

        model First "the first"
          Real x;
        end First;

        model Second "the second"
          Real y;
        end Second;
        """);

    /// <summary>
    /// The source with LF endings — which is the only text an offset means anything against, and
    /// the reason these constants are put through it: this file is CRLF in the working tree, so a
    /// raw string literal carries CRLF and offsets taken from it are wrong by a character a line.
    /// That is B231 in miniature, and it caught these tests before it caught anything else.
    /// </summary>
    private static string Lf(string source) =>
        ModelicaParser.Helpers.ModelicaParserHelper.NormalizeLineEndings(source);

    /// <summary>Where a class starts and where its rule stops, as the parser records them.</summary>
    private static (int Start, int Stop) RangeOf(string file, string declaration, string endName)
    {
        var text = Lf(file);
        var start = text.IndexOf(declaration, StringComparison.Ordinal);
        // The class_definition rule stops at the IDENT of `end X`, not at the `;` after it.
        var stop = text.IndexOf("end " + endName, StringComparison.Ordinal) + ("end " + endName).Length - 1;
        return (start, stop);
    }

    [Fact]
    public void TheSliceIsTheClassAndItsTerminator()
    {
        var (start, stop) = RangeOf(File, "model Second", "Second");

        Assert.Equal(
            Lf("""
            model Second "the second"
              Real y;
            end Second;
            """),
            ClassSource.SliceFromFile(File, start, stop));
    }

    [Fact]
    public void ACrLfFileSlicesToTheSameClassAsAnLfOne()
    {
        // The whole of B231: the offsets are into the normalised text, so a file read with its CRLF
        // intact is one character per line longer and the slice walks off into another class. Every
        // file in both measured libraries is CRLF, so this is the ordinary case, not an edge one.
        var (start, stop) = RangeOf(File, "model Second", "Second");

        var fromLf = ClassSource.SliceFromFile(File, start, stop);
        var fromCrLf = ClassSource.SliceFromFile(File.Replace("\n", "\r\n"), start, stop);

        Assert.Equal(fromLf, fromCrLf);
        Assert.StartsWith("model Second", fromCrLf);
    }

    [Fact]
    public void TheTerminatorIsTakenAcrossSpaces()
    {
        const string spaced = "model Cylinder = Other  ;\n";

        Assert.Equal("model Cylinder = Other  ;",
            ClassSource.SliceFromFile(spaced, 0, "model Cylinder = Other".Length - 1));
    }

    [Fact]
    public void ASemicolonSomethingElseOwnsIsNotTakenIn()
    {
        // The measured case this guards: a short class definition followed by an element annotation
        // on the next line. The annotation belongs to the element, not to the class, so the rule
        // ends at the comment and the ';' after the annotation is not this class's to take.
        const string source = """
            replaceable package Medium = Other "a medium"
                annotation (choicesAllMatching = true);
            """;
        var stop = source.IndexOf("\"a medium\"", StringComparison.Ordinal) + "\"a medium\"".Length - 1;

        Assert.Equal("replaceable package Medium = Other \"a medium\"",
            ClassSource.SliceFromFile(source, 0, stop));
    }

    [Fact]
    public void AClassWithNoTerminatorIsStillSliced()
    {
        const string bare = "model Only \"only\"\nend Only";

        Assert.Equal(bare, ClassSource.SliceFromFile(bare, 0, bare.Length - 1));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(5, 4)]
    [InlineData(0, 100000)]
    public void OffsetsThatDoNotDescribeARangeGiveNothing(int start, int stop) =>
        Assert.Null(ClassSource.SliceFromFile(File, start, stop));

    [Fact]
    public void AnEmptyFileGivesNothing() =>
        Assert.Null(ClassSource.SliceFromFile("", 0, 1));

    // ── choosing between the stored text and the file ─────────────────────────────

    [Fact]
    public void TheStoredTextIsUsedWhileItIsStillTheFiles()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "F.mo", File);
        var model = graph.GetNode<ModelNode>("Some.Package.Second")!;

        Assert.True(model.SourceMatchesFile);
        Assert.Equal(model.Definition.ModelicaCode, ClassSource.For(model, graph));
    }

    [Fact]
    public void TheFileIsReadAgainOnceSomethingHasRewrittenTheStoredText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, File.Replace("\n", "\r\n"));
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, File);
            var model = graph.GetNode<ModelNode>("Some.Package.Second")!;

            // What the trimmer and the formatter do: the stored text stops being the file's.
            model.Definition.ModelicaCode = "model Second \"rewritten\" end Second;";
            model.SourceMatchesFile = false;

            Assert.Equal(
                Lf("""
                model Second "the second"
                  Real y;
                end Second;
                """),
                ClassSource.For(model, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void AFileSomethingElseIsWritingLeavesTheStoredTextShowing()
    {
        // The formatter writing the file while the viewer reads it, or a VCS operation mid-flight.
        // A viewer that throws here is worse than one showing a moment-old copy.
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, File);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, File);
            var model = graph.GetNode<ModelNode>("Some.Package.Second")!;
            model.Definition.ModelicaCode = "model Second \"stale\" end Second;";
            model.SourceMatchesFile = false;

            using var exclusive = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Assert.Equal("model Second \"stale\" end Second;", ClassSource.For(model, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void AFileThatIsNoLongerThereLeavesTheStoredTextShowing()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "Gone.mo", File);
        var model = graph.GetNode<ModelNode>("Some.Package.Second")!;
        model.SourceMatchesFile = false;

        // Stale text beats a blank pane, and the alternative is an exception in a viewer.
        Assert.Equal(model.Definition.ModelicaCode, ClassSource.For(model, graph));
    }
}

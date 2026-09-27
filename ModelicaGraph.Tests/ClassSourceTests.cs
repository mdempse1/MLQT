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
    public void ASavedClassShowsWhatTheSaveWroteNotASliceAtItsOldOffsets()
    {
        // B400. A save rewrites the file and stores what it wrote on the class, clearing
        // SourceMatchesFile - and updates no offsets. This used to read the file again whenever the
        // flag was down, which is a cut through a file the offsets no longer describe; the stored
        // text is what the save wrote, so it is what the file says.
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, File.Replace("\n", "\r\n"));
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, File);
            var model = graph.GetNode<ModelNode>("Some.Package.Second")!;

            model.Definition.ModelicaCode = "model Second \"rewritten\" end Second;";
            model.SourceMatchesFile = false;

            Assert.Equal("model Second \"rewritten\" end Second;", ClassSource.For(model, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ASaveThatShortenedTheClassAboveDoesNotShowAChunkThatStartsMidDeclaration()
    {
        // B400, the shape that got past B343's name check. The save shortened First by six
        // characters, so Second's load-time offsets now start six characters into its declaration
        // and run six past its end. The slice names Second on its first line and does not end with
        // an `end X` to disagree with - and it is not Second.
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, File);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, File);
            var model = graph.GetNode<ModelNode>("Some.Package.Second")!;

            var written = Lf("""
                model Second "the second"
                  Real y;
                end Second;
                """);
            System.IO.File.WriteAllText(path,
                File.Replace("\"the first\"", "\"1st\"") + "\n// a comment after it\n");
            model.Definition.ModelicaCode = written;
            model.SourceMatchesFile = false;

            Assert.Equal(written, ClassSource.For(model, graph));
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

    // ── the file changed after the offsets were taken (B343) ────────────────────────

    private static readonly string PackageWithInlineChild = Lf("""
        package P "a package"
          model A "a child stored inline"
            Real x;
          end A;
          constant Real k = 1;
        end P;
        """);

    [Fact]
    public void ATrimmedPackageStillReadsItsFileWhileTheFileIsUnchanged()
    {
        // The control for the next test: the check must not refuse the ordinary case.
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, PackageWithInlineChild.Replace("\n", "\r\n"));
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, PackageWithInlineChild);
            var package = graph.GetNode<ModelNode>("P")!;
            PackageCodeTrimmer.TrimStandaloneChildren(graph);
            Assert.NotNull(package.TrimElision);

            Assert.Equal(PackageWithInlineChild, ClassSource.For(package, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ATrimmedPackageThatWasSavedShowsWhatTheSaveWrote()
    {
        // B400: the trim's elision describes the file as it was loaded. Once a save has written the
        // package and stored the result, neither it nor the offsets describe the file any more.
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, PackageWithInlineChild);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, PackageWithInlineChild);
            var package = graph.GetNode<ModelNode>("P")!;
            PackageCodeTrimmer.TrimStandaloneChildren(graph);
            Assert.NotNull(package.TrimElision);

            const string written = "package P \"a package\"\n  constant Real k = 1;\nend P;";
            System.IO.File.WriteAllText(path, written + "\n");
            package.Definition.ModelicaCode = written;
            package.SourceMatchesFile = false;

            Assert.Equal(written, ClassSource.For(package, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ATrimmedPackageWhoseFileWasEditedShowsItsStoredTextNotAChunkOfTheNewFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, PackageWithInlineChild);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, PackageWithInlineChild);
            var package = graph.GetNode<ModelNode>("P")!;
            PackageCodeTrimmer.TrimStandaloneChildren(graph);
            var stored = package.Definition.ModelicaCode;

            // Edited outside MLQT, and not yet reloaded: the offsets from load time now cut out
            // something else - here starting three characters into the class.
            System.IO.File.WriteAllText(path, "// x\n" + PackageWithInlineChild);

            Assert.Equal(stored, ClassSource.For(package, graph));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ARewrittenClassWhoseFileWasEditedShowsItsStoredTextNotAChunkOfTheNewFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ClassSourceTests_{Guid.NewGuid():N}.mo");
        System.IO.File.WriteAllText(path, File);
        try
        {
            var graph = new DirectedGraph();
            GraphBuilder.LoadModelicaFile(graph, path, File);
            var model = graph.GetNode<ModelNode>("Some.Package.Second")!;
            model.Definition.ModelicaCode = "model Second \"rewritten\" end Second;";
            model.SourceMatchesFile = false;

            // `First` grew by a line, so Second's offsets now land inside First and Second.
            System.IO.File.WriteAllText(path, File.Replace("  Real x;", "  Real x;\n  Real extra;"));

            Assert.Equal("model Second \"rewritten\" end Second;", ClassSource.For(model, graph));
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

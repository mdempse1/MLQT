using System.Text;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// B430 - a comment after the <c>within</c> clause, or after a top-level class's <c>end X;</c>, was a
/// parse error. Both are ordinary: <c>within P; // note</c>, and a trailer after the last line.
///
/// <para>Taken on B409's terms (see <see cref="DescriptionCommentTests"/>): nothing a comment run could
/// be confused with at file level starts with a comment, so each run is decided one token ahead and
/// never rescanned (B235). The text is outside every class's source span, so it is never in a stored
/// <c>ModelicaCode</c>; <see cref="FileLevelText"/> carries it for the writers (B445), and the grammar
/// could only accept it once they did.</para>
/// </summary>
public class FileLevelCommentTests
{
    public static TheoryData<string> Positions() => new()
    {
        "within P; // after\nmodel M\nend M;\n",
        "within P;\nmodel M\nend M; // trailer\n",
        "within P;\nmodel M\nend M;\n// trailer on its own line\n",
        "within P; /* a */\n// b\nmodel M\nend M; // c\n/* d */\n",
        "within; // top level\npackage L\nend L; // trailer\n",
        "// header\nwithin P; // after\nmodel M\nend M; // trailer",
        "model M\nend M; // no clause\n",
        "within P;\nfinal model M\nend M; // final\n",
        "within P;\nmodel A\nend A;\nmodel B\nend B; // last\n",
        "within P; // nothing else\n",
        "// only a header\n",
    };

    private static List<string> CommentsIn(string source) =>
        System.Text.RegularExpressions.Regex.Matches(source, @"//[^\n]*|/\*.*?\*/")
            .Select(m => m.Value.TrimEnd()).ToList();

    [Theory]
    [MemberData(nameof(Positions))]
    public void EachPositionParses(string source)
    {
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.Empty(errors);
    }

    [Fact]
    public void TheClassesKeepTheirPackage_AndTheirSourceHasNoneOfIt()
    {
        // What a parse error here cost: recovery lost the within clause's package for the classes.
        const string source = "within Lib; // after\npackage P\n  model A\n  end A;\nend P; // trailer\n";

        var models = ModelicaParserHelper.ExtractModels(source);

        Assert.Equal("Lib", models.Single(m => m.Name == "P").ParentModelName);
        Assert.Equal("Lib.P", models.Single(m => m.Name == "A").ParentModelName);
        Assert.All(models, m => Assert.DoesNotContain("//", m.SourceCode));
    }

    [Fact]
    public void TheRendererPutsEachCommentOnALineOfItsOwn()
    {
        Assert.Equal("within P;\n// after\nmodel M\nend M;\n// trailer",
            Render("within P; // after\nmodel M\nend M; // trailer\n"));
    }

    [Fact]
    public void TheRendererWritesWhatFileLevelTextWritesForTheSameFile()
    {
        // Format All writes the file from the class's stored source plus FileLevelText.Formatted();
        // the incremental formatter renders the file from disk. They must agree (B236, B445).
        const string file = "// header\n\nwithin P;   // after\n/* more */\nmodel M\nend M;  // trailer\n\n// last\n";
        var classStart = file.IndexOf("model M", StringComparison.Ordinal);
        var classStop = file.IndexOf("end M", StringComparison.Ordinal) + "end M".Length - 1;
        var text = FileLevelText.Read(file, classStart, classStop)!;

        Assert.Equal(Render(file), text.Formatted().ApplyTo(Render("within P;\nmodel M\nend M;\n")));
    }

    [Theory]
    [InlineData("within P;\nmodel A\nend A; // between\nmodel B\nend B;\n")]
    [InlineData("within P;\nmodel A\nend A;\n// between\nmodel B\nend B;\n")]
    public void ACommentBetweenTwoTopLevelClassesIsStillRefused(string source)
    {
        // Nothing carries it: FileLevelText holds the header, the text after the clause and the
        // trailer, and Format All writes each top-level class to a file of its own from its stored
        // source. Accepted, it would be deleted by the next Format All without a word; refused, it
        // is a visible syntax error and the file is not formatted.
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void RenderingKeepsTheCommentsAndIsStable(string source)
    {
        var once = Render(source);

        foreach (var comment in CommentsIn(source))
            Assert.Contains(comment, once);
        Assert.Equal(once, Render(once));
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void TheHighlighterGivesBackTheSource(string source)
    {
        var lines = ModelicaTokenClassifier.Highlight(source);
        var stripped = System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Replace(string.Join("\n", lines), "</?[A-Z_]+>", ""));

        Assert.Equal(source, stripped);
        Assert.Contains(lines, l => l.Contains("<COMMENT>", StringComparison.Ordinal));
    }

    // PreprocessCode appends the ';' a class's stored source lacks. Appended to a trailing line
    // comment it became part of the comment, and every save wrote `// trailer;`.
    [Theory]
    [InlineData("within P;\nmodel M\nend M; // trailer\n", "within P;\nmodel M\nend M; // trailer")]
    [InlineData("within P;\nmodel M\nend M; /* t */", "within P;\nmodel M\nend M; /* t */")]
    [InlineData("// only a comment", "// only a comment")]
    [InlineData("model M\nend M // c", "model M\nend M // c\n;")]
    [InlineData("model M\nend M /* c */", "model M\nend M /* c */\n;")]
    [InlineData("model M \"http://x\"\nend M", "model M \"http://x\"\nend M;")]
    [InlineData("model M\n  Real x \"a // b\"", "model M\n  Real x \"a // b\";")]
    [InlineData("model M\nend M", "model M\nend M;")]
    public void PreprocessCode_AppendsTheSemicolonOnlyWhereItIsMissing_AndNeverInsideAComment(string code, string expected)
        => Assert.Equal(expected, ModelicaParserHelper.PreprocessCode(code));

    [Fact]
    public void AClassSourceEndingInACommentStillReportsTheMissingSemicolon()
    {
        // The ';' is on a line of its own, so the comment keeps its text and the error is where it was.
        var (_, errors) = ModelicaParserHelper.ParseWithErrors("within P;\nmodel M\nend M // c");
        Assert.NotEmpty(errors);
    }

    private static string Render(string source)
    {
        var (tree, stream, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(source);
        Assert.Empty(errors);
        var renderer = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true,
            excludeClassDefinitions: false, stream, classNamesToExclude: null);
        renderer.VisitStored_definition(tree);
        var lines = renderer.Code.ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static string Comments(int count, string position)
    {
        var run = string.Concat(Enumerable.Range(0, count).Select(i => $"// a comment {i}\n"));
        return position switch
        {
            "a header" => run + "within P;\nmodel M\nend M;\n",
            "a header alone" => run,
            "after within" => "within P;\n" + run + "model M\nend M;\n",
            "after within alone" => "within P;\n" + run,
            _ => "within P;\nmodel M\nend M;\n" + run,
        };
    }

    /// <summary>
    /// Growth, as in <see cref="DescriptionCommentTests"/>, but counted rather than timed. Each run
    /// must be decided once, not once per comment - which it is not if two loops can both claim it:
    /// a trailing loop outside the class group can claim a run after the clause, or a header, in a
    /// file with no class after it, and deciding that scans to the end of the run for every comment.
    /// What B235's rescan multiplies is the tokens the parser looks ahead over, so that is what is
    /// measured: ANTLR's profiler counts it exactly, and a timed version of this test read as
    /// superlinear about one full, parallel test run in three while the same parse measured linear on
    /// its own.
    /// </summary>
    [Theory]
    [InlineData("a header")]
    [InlineData("a header alone")]
    [InlineData("after within")]
    [InlineData("after within alone")]
    [InlineData("after the class")]
    public void TwiceTheCommentsIsNotFourTimesTheLookahead(string position)
        => ParseGrowth.AssertLinear(count => Comments(count, position), $"comments {position}", "stored_definition");
}

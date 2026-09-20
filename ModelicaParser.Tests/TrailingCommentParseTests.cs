using System.Diagnostics;
using System.Text;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

/// <summary>
/// B235 — a run of comment lines no longer makes the parse quadratic.
///
/// <para><b>What it was.</b> A block of commented-out equations cost four times the work for twice
/// the text: 500 comments parsed in 1.5s, 1,000 in 4.4s, 2,000 in 17s, 4,000 in 69s — on legal
/// Modelica with zero parse errors, at 323 KB. Every surface parses, so a library with
/// commented-out equations cost the CLI and the MCP server the same minutes it cost the page.</para>
///
/// <para><b>What it actually was.</b> Not comments, and not equation sections: a <em>trailing run</em>
/// of comments in any <c>(c_comment | X)*</c> loop. Measuring the same comments with one real
/// equation after them gave 22ms against 16,813ms — 764× — because the loop then resolves each
/// decision immediately instead of scanning the whole run to decide whether to exit. The element
/// list had it too, at 17,933ms, which is why the fix is in all three loops and not only the one
/// that was reported.</para>
///
/// <para><b>The fix</b> is one character in each: <c>c_comment</c> became <c>c_comment+</c>, so the
/// run is consumed in a single iteration and the ambiguous decision is taken once rather than once
/// per comment. Verified over 8,367 real files: every rendered file byte-identical, the classifier's
/// round trip still exact, and the finding counts on MSL and Buildings unchanged.</para>
/// </summary>
public class TrailingCommentParseTests
{
    private static string WithTrailingComments(int count, string section) =>
        new StringBuilder()
            .Append("model Big \"a large class\"\n")
            .Append("  parameter Real p = 1.0;\n")
            .Append(section)
            .Append(string.Concat(Enumerable.Range(0, count).Select(i => $"  // a comment about p{i}\n")))
            .Append("end Big;\n")
            .ToString();

    /// <summary>
    /// Growth, not a threshold. A time in milliseconds is a property of the machine; **four times
    /// the work for twice the text** is a property of the parser, and it is what was wrong.
    /// </summary>
    [Theory]
    [InlineData("equation\n")]
    [InlineData("")]
    public void TwiceTheCommentsIsNotFourTimesTheWork(string section)
    {
        // Warm ANTLR's prediction cache, or the first measurement includes building it.
        ModelicaParserHelper.ParseWithTokens(WithTrailingComments(50, section));

        // 1,000 and 4,000 rather than a smaller pair, so neither measurement sits near the
        // stopwatch's granularity — a 2ms baseline makes any ratio meaningless on a loaded machine.
        var small = Time(WithTrailingComments(1000, section));
        var large = Time(WithTrailingComments(4000, section));

        // Four times the text. Linear is ~4x and quadratic ~16x; measured at 3.6x after the fix and
        // 16x before it, so the bar sits where only the defect can reach it.
        Assert.True(large < small * 8,
            $"1,000 comments took {small}ms and 4,000 took {large}ms — that is superlinear, which is "
            + "what B235 was. See `c_comment+` in equation_or_comment, statement_or_comment and "
            + "element_list.");
    }

    private static long Time(string source)
    {
        var clock = Stopwatch.StartNew();
        var (tree, _, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(source);
        clock.Stop();

        // The premise, and the thing that made this worth chasing: the input is legal Modelica. A
        // measurement over input the parser is recovering from would be measuring error recovery.
        Assert.NotNull(tree);
        Assert.Empty(errors);
        return Math.Max(1, clock.ElapsedMilliseconds);
    }

    [Fact]
    public void EveryCommentIsStillInTheTree()
    {
        // The fix groups a run into one `equation_or_comment` instead of one each. Nothing may be
        // lost by that — the renderer writes these back into the user's file.
        var tree = ModelicaParserHelper.Parse(WithTrailingComments(5, "equation\n"));
        Assert.NotNull(tree);

        var section = tree!.class_definition()[0].class_specifier().long_class_specifier()
                          .composition().equation_section();
        var comments = section.SelectMany(s => s.equation_or_comment())
                              .SelectMany(e => e.c_comment())
                              .Select(c => c.GetText())
                              .ToList();

        Assert.Equal(5, comments.Count);
        Assert.Equal("// a comment about p0", comments[0].Trim());
        Assert.Equal("// a comment about p4", comments[4].Trim());
    }

    [Fact]
    public void CommentsBetweenEquationsAreStillSeparate()
    {
        // The other arrangement: a comment, an equation, a comment. Grouping must not swallow the
        // equation between them or reorder anything.
        var tree = ModelicaParserHelper.Parse("""
            model M "m"
            equation
              // before
              x = 1;
              // after
            end M;
            """.Replace("\r\n", "\n"));
        Assert.NotNull(tree);

        var parts = tree!.class_definition()[0].class_specifier().long_class_specifier()
                        .composition().equation_section()[0].equation_or_comment();

        Assert.Equal(3, parts.Length);
        Assert.Equal("// before", parts[0].c_comment()[0].GetText().Trim());
        Assert.Equal("x=1", parts[1].equation().GetText());
        Assert.Equal("// after", parts[2].c_comment()[0].GetText().Trim());
    }
}

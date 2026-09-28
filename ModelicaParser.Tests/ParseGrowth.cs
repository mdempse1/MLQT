using Antlr4.Runtime;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

/// <summary>
/// How a test asks whether a run of something makes the parse superlinear - the question B235 was
/// (a trailing run of comments rescanned at every comment), and every comment position since has had
/// to be asked again (B409, B430, B432).
///
/// <para><b>Counted, not timed</b> (B459). What a rescan multiplies is the tokens the parser looks
/// ahead over, and ANTLR's profiler counts that exactly, so the answer is the same on every machine
/// and under any load. The timed versions of these tests read as superlinear about one full, parallel
/// test run in three while the same parse measured linear on its own - and CI runs in parallel on
/// shared runners.</para>
///
/// <para>Each test built on this has been run against the quadratic grammar it was written to catch
/// and fails there by an order of magnitude; the ratio is not near the bar either way. Four times the
/// run is about four times the lookahead when each decision is taken one token ahead, and about
/// sixteen when every element of the run rescans the rest, so the bar sits at eight.</para>
/// </summary>
internal static class ParseGrowth
{
    /// <summary>
    /// Asserts that parsing <paramref name="source"/> with four times the run costs less than eight
    /// times the lookahead. <paramref name="what"/> names the run in the message ("comments in a class
    /// body") and <paramref name="where"/> the rule whose comment in modelica.g4 explains it.
    /// </summary>
    public static void AssertLinear(Func<int, string> source, string what, string where, int small = 400)
    {
        var large = small * 4;
        var smallLook = Lookahead(source(small));
        var largeLook = Lookahead(source(large));

        // A run that needs no lookahead at all counts none, which is linear too.
        Assert.True(largeLook < Math.Max(smallLook, 1) * 8,
            $"{small:N0} {what} cost {smallLook:N0} tokens of lookahead and {large:N0} cost {largeLook:N0} - "
            + $"that is superlinear (B235). See the comment on {where} in modelica.g4.");
    }

    /// <summary>
    /// The total tokens the parser looked ahead over to take every decision in parsing
    /// <paramref name="source"/> as a file, which must be legal: a measurement over input the parser is
    /// recovering from would be measuring error recovery.
    /// </summary>
    public static long Lookahead(string source)
    {
        var lexer = new modelicaLexer(new AntlrInputStream(ModelicaParserHelper.PreprocessCode(source)));
        var parser = new modelicaParser(new CommonTokenStream(lexer)) { Profile = true };
        var errors = 0;
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CountingListener(() => errors++));

        parser.stored_definition();

        Assert.Equal(0, errors);
        return parser.ParseInfo.getDecisionInfo().Sum(d => d.SLL_TotalLook + d.LL_TotalLook);
    }

    private sealed class CountingListener(Action onError) : BaseErrorListener
    {
        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e) => onError();
    }
}

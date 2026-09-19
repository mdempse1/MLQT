using System.Diagnostics;
using MLQT.Shared.Components;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// Where the time goes when the Code Review page opens a very large class (B185). An imported FMU —
/// <c>Engines Examples I2 fmu</c> — was reported as still not rendered after five minutes.
///
/// <para>The backlog named two candidate costs, "the syntax highlighting over a very large token
/// stream, and the reformat the page performs in order to colour it", and said to measure which half
/// costs the time before addressing either. <b>It is neither.</b> B215 removed the reformat, and the
/// highlighting turns out to cost 20–148 ms at every size tried. What costs is the <b>parse</b>, and
/// not because of size: a run of comment lines inside an <c>equation</c> section makes it
/// quadratic (<b>B235</b>).</para>
///
/// <para>Opt-in through <c>MLQT_LARGE_CLASS_COST</c>, because it is a measurement rather than an
/// assertion: it prints a table and passes. Timings in CI would be noise, and a threshold chosen
/// from a number nobody can reproduce is the thing this repository keeps warning about.</para>
/// </summary>
public class LargeClassCostTests(ITestOutputHelper output)
{
    /// <summary>
    /// <paramref name="shape"/> is the one variable. The first version of this measurement changed
    /// two things at once — it added annotations to the declarations <em>and</em> a block of
    /// comments after them — and so blamed the annotations for what the comments were doing. Vary
    /// one thing.
    /// </summary>
    private static string ClassOf(int count, string shape)
    {
        var text = new System.Text.StringBuilder();
        text.Append("model Big \"a large class\"\n");

        for (var i = 0; i < count; i++)
        {
            if (shape == "annotated")
            {
                text.Append($"  parameter Real p{i} = {i}.0 \"parameter number {i}\" annotation (\n");
                text.Append($"    Placement(transformation(extent={{{{-10,-10}},{{10,10}}}}, rotation={i % 360})));\n");
            }
            else
            {
                text.Append($"  parameter Real p{i} = {i}.0 \"parameter number {i}\";\n");
            }
        }

        if (shape == "comments-in-equations")
        {
            text.Append("equation\n");
            for (var i = 0; i < count; i++)
                text.Append($"  // a comment about p{i}\n");
        }

        text.Append("end Big;\n");
        return text.ToString();
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("annotated")]
    [InlineData("comments-in-equations")]
    public void WhereTheTimeGoesOnAVeryLargeClass(string shape)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MLQT_LARGE_CLASS_COST")))
            return;

        // Warm the paths, and ANTLR's own prediction cache, so the first row is not measuring JIT.
        ModelicaTokenClassifier.Highlight(ClassOf(50, shape));

        output.WriteLine($"{shape}");
        output.WriteLine($"{"lines",10} {"KB",8} {"lex",8} {"parse",9} {"classify",10} {"toHtml",8} {"errors",8}");

        // Small numbers on purpose: the point is the shape of the curve. The comment case quadruples
        // for every doubling, so 8,000 lines of it already takes over a minute.
        foreach (var count in new[] { 250, 500, 1_000, 2_000 })
        {
            var source = ClassOf(count, shape);
            var lines = source.Count(c => c == '\n');

            var sw = Stopwatch.StartNew();
            ModelicaTokenClassifier.TokensOnly(source);
            var lex = sw.ElapsedMilliseconds;

            sw.Restart();
            var (tree, stream, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(source);
            var parse = sw.ElapsedMilliseconds;

            sw.Restart();
            var markup = ModelicaTokenClassifier.Highlight(tree, stream, source);
            var classify = sw.ElapsedMilliseconds;

            sw.Restart();
            CodeViewer.ToHtml(markup, null);
            var toHtml = sw.ElapsedMilliseconds;

            output.WriteLine($"{lines,10:N0} {source.Length / 1024,8:N0} {lex,8:N0} {parse,9:N0} "
                             + $"{classify,10:N0} {toHtml,8:N0} {errors.Count,8:N0}");
        }
    }
}

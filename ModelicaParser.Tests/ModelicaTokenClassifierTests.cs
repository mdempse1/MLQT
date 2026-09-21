using System.Text.RegularExpressions;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// The two properties that carry <see cref="ModelicaTokenClassifier"/>, plus the tiers and the one
/// place it deliberately disagrees with <see cref="ModelicaRenderer"/>.
///
/// <list type="bullet">
///   <item><description><b>Round trip</b> — strip the tags and the output is the source, character
///   for character. This is the entire fidelity claim and it is one assertion.</description></item>
///   <item><description><b>Agreement</b> — the category sequence is the renderer's. This pins the
///   colours to the behaviour users have now without freezing a golden file, so a change to either
///   side has to be deliberate.</description></item>
/// </list>
///
/// <para>Both were measured over the Modelica Standard Library and Modelica Buildings before any of
/// this was written — 8,367 files, 1,103,108 lines, exact — and
/// <see cref="RoundTripsOverAWholeLibrary"/> is how that is re-run.</para>
/// </summary>
public class ModelicaTokenClassifierTests
{
    // ── the corpus ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deliberately not tidy. Between them these cover what the walk actually branches on: a within
    /// clause, imports, type specifiers, function calls, `der`, a class annotation with a graphics
    /// array and a DynamicSelect inside a named argument, a multi-line documentation string, both
    /// comment forms, a quoted identifier, protected sections and a short class definition.
    /// </summary>
    public static TheoryData<string, string> Corpus() => new()
    {
        { "within clause and imports", """
            within Some.Package;
            model M "a model"
              import Modelica.Units.SI;
              import Modelica.Math.sin;
              extends Modelica.Icons.Example;
              parameter SI.Length   L = 1 "length";
            end M;
            """ },
        { "calls, der and subscripts in equations", """
            model Eq "equations"
              Real x[3];
              Real y;
            equation
              y = Modelica.Math.cos(2*x[1]) + sin(x[2]);
              der(y) = -y;
              assert(y > 0, "y must be positive");
            end Eq;
            """ },
        { "graphics annotation with a dynamic select", """
            model Icon "icon"
              Boolean u;
              annotation (Icon(coordinateSystem(preserveAspectRatio=false), graphics={
                Ellipse(
                  extent={{-71,7},{-85,-7}},
                  lineColor=DynamicSelect({235,235,235}, if u then {0,255,0} else {235,235,235}),
                  fillPattern=FillPattern.Solid),
                Text(extent={{-150,150},{150,110}}, textString="%name")}));
            end Icon;
            """ },
        { "multi-line documentation and both comment forms", """
            model Doc "doc"
              // a line comment
              /* a block
                 comment */
              Real q;
              annotation (Documentation(info="<html>
            <p>Several lines.</p>
            <p>And another.</p>
            </html>"));
            end Doc;
            """ },
        { "quoted identifier and odd spacing", """
            package P "p"
              model 'Connections.branch()' "quoted"
                Real    z =   1.5e-3;
              end 'Connections.branch()';
            end P;
            """ },
        { "protected section and short class definition", """
            model V "visibility"
              Real pub;
            protected
              Real prot;
              type Len = Modelica.Units.SI.Length;
            end V;
            """ },
        { "algorithm with assignment and nested call", """
            function F "function"
              input Real a[:];
              output Real b;
            protected
              Real m[2,2];
              Integer i;
            algorithm
              b := a[1];
              m[integer(i),1] := a[2];
            end F;
            """ },
    };

    // ── property 1: round trip ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Corpus))]
    public void StrippingTheTagsGivesBackTheSource(string _, string source)
    {
        AssertRoundTrips(source);
    }

    [Fact]
    public void RoundTripsWhateverTheLineEndings()
    {
        const string source = "model M \"m\"\n  Real x;\nend M;\n";

        AssertRoundTrips(source.Replace("\n", "\r\n"));
        AssertRoundTrips(source.Replace("\n", "\r"));
    }

    [Fact]
    public void RoundTripsTextTheParserHadToTidyBeforeItCouldRead()
    {
        // PreprocessCode trims the end and appends a ';' when there is none, so the token stream
        // describes text that is not quite the source. The emitter has to ignore the token that is
        // not there and put the trimmed tail back.
        AssertRoundTrips("model M \"m\"\nend M");
        AssertRoundTrips("model M \"m\"\nend M;\n\n\n   \n");
        AssertRoundTrips("model M \"m\"\nend M;   ");
    }

    [Theory]
    [InlineData("model Broken \"unterminated")]
    [InlineData("Real x /* inline */ = 1;")]
    [InlineData("@@@ not modelica at all <html>&amp;</html>")]
    [InlineData("model M \"m\" end N;")]
    [InlineData("")]
    public void RoundTripsWhatDoesNotParse(string source)
    {
        // The tiers exist so that a class which cannot be parsed is still shown - and still shown
        // exactly. `Real x /* inline */ = 1;` is three parse errors under this grammar, because
        // COMMENT is on the default channel and only legal at c_comment positions; the renderer
        // cannot show that comment at all, and this does.
        AssertRoundTrips(source);
    }

    // ── property 1b: the emit loop, where a crash cannot hide ────────────────────

    /// <summary>
    /// Emits through the three-argument <see cref="ModelicaTokenClassifier.Highlight(Antlr4.Runtime.Tree.IParseTree?, Antlr4.Runtime.BufferedTokenStream, string)"/>,
    /// which has no <c>catch</c> — and asserts the output is <b>tagged</b> as well as faithful.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the round-trip tests above cannot do this job (B234).</b>
    /// <c>Highlight(string)</c> ends in <c>catch { return Plain(source); }</c>, and <c>Plain</c>
    /// returns the source as untagged lines — which round-trips perfectly. So a fault that makes
    /// the emit loop <i>throw</i> is caught, falls back, and every round-trip assertion still
    /// passes. A test that only strips the tags off cannot tell <c>Highlight</c> from
    /// <c>Plain</c>.</para>
    ///
    /// <para><b>That is why the offset arithmetic survived mutation, and the corpus was never the
    /// reason.</b> <see cref="RoundTripsOverAWholeLibrary"/> calls the same catching entry point and
    /// strips, so it would not have killed those mutants either — over 8,367 files or over eight.
    /// Reaching the guards is easy; noticing that they were reached is what was missing.</para>
    ///
    /// <para><b>What still survives in the emit loop, and why it is not worth chasing.</b> Measured
    /// after these tests: 22 survivors became 19, 84.72% became 86.81%, and what is left there is
    /// equivalent — read, as CLAUDE.md asks, rather than scored:</para>
    /// <list type="bullet">
    /// <item>the <c>StringBuilder</c> capacity arithmetic, which changes no output;</item>
    /// <item><c>i &lt; stream.Size</c> to <c>&lt;=</c>, unreachable because the EOF <c>break</c>
    /// fires first — and the <c>break</c> itself, unreachable because the guard below would drop
    /// EOF anyway. Those two are equivalent <i>because of each other</i>, which is defence in depth
    /// rather than an accident;</item>
    /// <item><c>start &gt; cursor</c> to <c>&gt;=</c> and <c>cursor &lt; text.Length</c> to
    /// <c>&lt;=</c>: when the two are equal the extra call appends a zero-length span.</item>
    /// </list>
    /// <para>One is not equivalent and is left: a logical mutation at the guard that drops the
    /// <c>stop &gt;= text.Length</c> disjunct. Two of that line's three mutants die here; no input
    /// was found for the third.</para>
    ///
    /// </remarks>
    [Theory]
    // PreprocessCode appends a ';' when the source has none. That token starts one past the end of
    // the text being emitted, and the guard has to drop it rather than slice for it.
    [InlineData("no trailing semicolon", "model M \"m\"\n  Real x = 1;\nend M")]
    // ...and it trims the end, so the tail it removed is not in any token and has to be put back.
    [InlineData("trailing blank lines", "model M \"m\"\n  Real x = 1;\nend M;\n\n\n   \n")]
    [InlineData("trailing spaces", "model M \"m\"\n  Real x = 1;\nend M;   ")]
    // A character the lexer refuses is in no token at all, so it leaves a gap between one token's
    // end and the next one's start. WS is hidden rather than skipped, so only a rejected character
    // does this.
    [InlineData("a character the lexer rejects", "model M \"m\"\n  Real x = 1; !!!\nend M;")]
    [InlineData("rejected character at the end", "model M \"m\"\nend M; @@@")]
    public void TheEmitLoopIsFaithfulAndStillColours(string _, string source)
    {
        var (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);

        // No catch on this overload: a mutation that throws fails here instead of falling back.
        var lines = ModelicaTokenClassifier.Highlight(tree, stream, source);

        Assert.Equal(ModelicaParserHelper.NormalizeLineEndings(source), Strip(lines));

        // The half that Plain would also satisfy. Without it this is a test of Plain.
        Assert.Contains(lines, line => line.Contains("<KEYWORD>model</KEYWORD>"));
    }

    /// <summary>
    /// The control for the assertion above: <see cref="ModelicaTokenClassifier.Plain"/> round-trips
    /// too, so a test that only round-trips passes against it.
    /// </summary>
    [Fact]
    public void PlainRoundTripsAsWell_WhichIsWhyTheTagsAreAsserted()
    {
        const string source = "model M \"m\"\n  Real x = 1;\nend M;\n";

        var plain = ModelicaTokenClassifier.Plain(source);

        Assert.Equal(ModelicaParserHelper.NormalizeLineEndings(source), Strip(plain));
        Assert.DoesNotContain(plain, line => line.Contains("<KEYWORD>"));
    }

    [Fact]
    public void RoundTripsWithNoParseTreeAtAll()
    {
        const string source = """
            model M "m"
              Real x = 1;
            end M;
            """;

        var lines = ModelicaTokenClassifier.Highlight(
            tree: null, ModelicaTokenClassifier.TokensOnly(source), source);

        Assert.Equal(ModelicaParserHelper.NormalizeLineEndings(source), Strip(lines));
        Assert.Contains(lines, line => line.Contains("<KEYWORD>model</KEYWORD>"));
    }

    /// <summary>
    /// The property over a real library, which is where it was established: 2,671 files for the
    /// Modelica Standard Library, 5,696 for Buildings, every one of them exact. Opt-in, because a
    /// library that large is not in the repository — point <c>MLQT_FIDELITY_CORPUS</c> at one
    /// (several may be separated by <c>;</c>) and this runs over every <c>.mo</c> file in it.
    /// </summary>
    [Fact]
    public void RoundTripsOverAWholeLibrary()
    {
        var roots = Environment.GetEnvironmentVariable("MLQT_FIDELITY_CORPUS");
        if (string.IsNullOrWhiteSpace(roots))
            return;

        var failures = new List<string>();
        var files = 0;

        foreach (var root in roots.Split(';', StringSplitOptions.RemoveEmptyEntries))
            foreach (var file in Directory.EnumerateFiles(root.Trim(), "*.mo", SearchOption.AllDirectories))
            {
                files++;
                var source = ModelicaFileEncoding.ReadAllTextOnly(file);
                if (Strip(ModelicaTokenClassifier.Highlight(source))
                    != ModelicaParserHelper.NormalizeLineEndings(source))
                    failures.Add(file);
            }

        Assert.True(files > 0, $"MLQT_FIDELITY_CORPUS matched no .mo files: {roots}");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {files} files did not round trip:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(20)));
    }

    // ── property 2: agreement with the renderer ───────────────────────────────────

    [Theory]
    [MemberData(nameof(Corpus))]
    public void AgreesWithTheRendererOnEveryCategory(string _, string source)
    {
        var (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);

        var renderer = new ModelicaRenderer(
            renderForCodeEditor: true, showAnnotations: true, excludeClassDefinitions: false,
            tokenStream: stream, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.VisitStored_definition(tree);

        Assert.Equal(
            WordTokens(renderer.Code),
            WordTokens(ModelicaTokenClassifier.Highlight(tree, stream, source)));
    }

    /// <summary>
    /// The one place the two used to differ, and the reason the property above was asserted on a
    /// corpus that avoided it rather than with a tolerance (backlog B232).
    ///
    /// <para>The renderer held <c>_isFunction</c> in a field and visited a reference's
    /// <c>array_subscripts</c> without clearing it, so the subscript of an assignment target was
    /// coloured as a function call — 377 tokens in MSL and 512 in Buildings, and the whole residue
    /// of a 99.998% agreement. The classifier declined to reproduce it, which left one of the two
    /// deliberately wrong; <b>B232 scoped the field, so they now agree</b> and this test says so
    /// from the side that first noticed.</para>
    /// </summary>
    [Fact]
    public void ASubscriptIsNotAFunctionCall_AndTheRendererAgrees()
    {
        const string source = """
            function F "f"
              output Real b[2];
              Integer i;
            algorithm
              b[i] := 1;
            end F;
            """;

        var (tree, stream) = ModelicaParserHelper.ParseWithTokens(source);
        var renderer = new ModelicaRenderer(
            renderForCodeEditor: true, showAnnotations: true, excludeClassDefinitions: false,
            tokenStream: stream, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.VisitStored_definition(tree);

        Assert.Contains(("IDENT", "i"), WordTokens(renderer.Code));
        Assert.DoesNotContain(("FUNCTION", "i"), WordTokens(renderer.Code));
        Assert.Contains(("IDENT", "i"), WordTokens(ModelicaTokenClassifier.Highlight(tree, stream, source)));
    }

    /// <summary>
    /// The rule that needed three increment sites rather than the one the design note named: a
    /// function inside a <b>named</b> argument of a graphics element is coloured as a call, because
    /// `named_argument : IDENT '=' function_argument` and the level is bumped around a
    /// function_argument's expression.
    /// </summary>
    [Fact]
    public void FunctionDeepInsideAGraphicsAnnotationIsStillAFunction()
    {
        const string source = """
            model M "m"
              Boolean u;
              annotation (Icon(graphics={Ellipse(extent={{-1,1},{1,-1}},
                lineColor=DynamicSelect({0,0,0}, if u then {1,1,1} else {0,0,0}))}));
            end M;
            """;

        var tokens = WordTokens(ModelicaTokenClassifier.Highlight(source));

        Assert.Contains(("FUNCTION", "DynamicSelect"), tokens);
        // Its host is not: the element itself sits at a shallower level than the rule re-enables.
        Assert.Contains(("IDENT", "Ellipse"), tokens);
    }

    /// <summary>
    /// The negative half of the rule above, and the half that the measurement could not check:
    /// function colouring is <b>off</b> inside an annotation, and comes back on only deep inside a
    /// class-level <c>graphics</c> array. Without these, every mutation that makes the rule fire
    /// more often than it should goes unnoticed — the positive test still passes.
    /// </summary>
    /// <remarks>
    /// Each of these is <see cref="FunctionDeepInsideAGraphicsAnnotationIsStillAFunction"/> with one
    /// thing changed, so the pair isolates that one thing. Shallower negative cases do not
    /// discriminate: they never reach the nesting the rule re-enables colouring at, so they pass
    /// whether the rule is right or not.
    /// </remarks>
    [Theory]
    [InlineData("the array is not the graphics one", """
        model M "m"
          Boolean u;
          annotation (Icon(other={Ellipse(extent={{-1,1},{1,-1}},
            lineColor=DynamicSelect({0,0,0}, if u then {1,1,1} else {0,0,0}))}));
        end M;
        """)]
    [InlineData("the annotation is on a declaration, not on the class", """
        model M "m"
          Boolean u annotation (Icon(graphics={Ellipse(extent={{-1,1},{1,-1}},
            lineColor=DynamicSelect({0,0,0}, if u then {1,1,1} else {0,0,0}))}));
        end M;
        """)]
    public void AFunctionInsideAnAnnotationIsNotColouredAsOne(string _, string source)
    {
        var tokens = WordTokens(ModelicaTokenClassifier.Highlight(source));

        Assert.Contains(("IDENT", "DynamicSelect"), tokens);
        Assert.DoesNotContain(("FUNCTION", "DynamicSelect"), tokens);
    }

    [Theory]
    [InlineData("der")]
    [InlineData("initial")]
    [InlineData("pure")]
    public void TheCallKeywordsAreColouredAsTheCallsTheyAre(string keyword)
    {
        var tokens = WordTokens(ModelicaTokenClassifier.Highlight($$"""
            model M "m"
              Real y;
            equation
              y = {{keyword}}(y);
            end M;
            """));

        Assert.Contains(("FUNCTION", keyword), tokens);
    }

    // ── the markup contract CodeViewer and DiffViewer rely on ─────────────────────

    [Fact]
    public void NoTagEverSpansALine()
    {
        // CodeViewer and DiffViewer both run their tag regex per line, and `.` does not cross a
        // newline. 0.18% of tokens span lines and they cover a quarter of all lines, so a tag left
        // open at a line break loses the colour for the rest of the file.
        var lines = ModelicaTokenClassifier.Highlight("""
            model M "m"
              annotation (Documentation(info="<html>
            one
            two
            </html>"));
            end M;
            """);

        foreach (var line in lines)
        {
            Assert.DoesNotContain("\n", line);
            Assert.Equal(
                Regex.Matches(line, "<(?!/)[A-Z]+>").Count,
                Regex.Matches(line, "</[A-Z]+>").Count);
        }
    }

    [Fact]
    public void TextOutsideATagIsHtmlEncoded()
    {
        // '&' is in no lexer rule, so it is skipped rather than tokenised and lands in the gap
        // between two tokens, where nothing downstream will encode it. Left raw it would be markup
        // in the page. ('<' and '>' are real operators and so arrive inside a tag, where being raw
        // is correct - CodeViewer and DiffViewer encode tag contents themselves.)
        var markup = string.Join("\n", ModelicaTokenClassifier.Highlight("model M \"m\" & end M;"));

        Assert.Contains("&amp;", markup);
        Assert.DoesNotMatch(new Regex(@"&(?!amp;)"), markup);
    }

    [Fact]
    public void TextInsideATagIsLeftRawForTheViewerToEncode()
    {
        // The other half of the contract, and the one that breaks silently: CodeViewer HTML-encodes
        // what it finds between the tags, so encoding it here as well would show a user
        // `&amp;lt;html&amp;gt;` in their documentation string.
        var markup = string.Join("\n", ModelicaTokenClassifier.Highlight("""
            model M "m <html>&amp;</html>"
            end M;
            """));

        Assert.Contains("<STRING>\"m <html>&amp;</html>\"</STRING>", markup);
    }

    [Fact]
    public void PlainIsTheSourceEncodedAndNothingElse()
    {
        var lines = ModelicaTokenClassifier.Plain("model M \"a <b> & c\"\nend M;");

        Assert.Equal(["model M &quot;a &lt;b&gt; &amp; c&quot;", "end M;"], lines);
    }

    [Fact]
    public void EmptySourceIsNoLines()
    {
        Assert.Empty(ModelicaTokenClassifier.Highlight(""));
        Assert.Empty(ModelicaTokenClassifier.Plain(""));
        Assert.Empty(ModelicaTokenClassifier.Highlight(tree: null, stream: null, ""));
    }

    [Fact]
    public void AMissingTokenStreamFallsBackToPlain()
    {
        Assert.Equal(
            ModelicaTokenClassifier.Plain("model M \"m\"\nend M;"),
            ModelicaTokenClassifier.Highlight(tree: null, stream: null, "model M \"m\"\nend M;"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static readonly Regex TagRegex =
        new(@"</?(KEYWORD|TYPE|IDENT|NAME|FUNCTION|OPERATOR|NUMBER|STRING|COMMENT)>", RegexOptions.Compiled);

    private static readonly Regex WordRegex =
        new(@"<(IDENT|NAME|TYPE|FUNCTION|KEYWORD)>(.*?)</\1>", RegexOptions.Compiled);

    private static void AssertRoundTrips(string source)
    {
        Assert.Equal(
            ModelicaParserHelper.NormalizeLineEndings(source),
            Strip(ModelicaTokenClassifier.Highlight(source)));
    }

    private static readonly Regex TagPairRegex =
        new(@"<(KEYWORD|TYPE|IDENT|NAME|FUNCTION|OPERATOR|NUMBER|STRING|COMMENT)>(.*?)</\1>",
            RegexOptions.Compiled);

    /// <summary>
    /// The markup with its tags removed and its encoding undone — which must be the source.
    ///
    /// <para>Undone <b>only outside the tags</b>, which is the same split <c>CodeViewer</c> makes:
    /// inside a tag the text is the source's own, and a documentation string containing
    /// <c>&amp;amp;</c> really does contain those five characters. Decoding the whole markup instead
    /// turns them into one and reports 1,313 of 8,367 real library files as failing a round trip
    /// that in fact succeeded.</para>
    /// </summary>
    private static string Strip(List<string> lines)
    {
        var markup = string.Join("\n", lines);
        var source = new System.Text.StringBuilder(markup.Length);
        var last = 0;

        foreach (Match match in TagPairRegex.Matches(markup))
        {
            source.Append(System.Net.WebUtility.HtmlDecode(markup[last..match.Index]));
            source.Append(match.Groups[2].Value);
            last = match.Index + match.Length;
        }

        source.Append(System.Net.WebUtility.HtmlDecode(markup[last..]));
        return source.ToString();
    }

    /// <summary>
    /// The identifiers and keywords in order, with their categories, from either side's markup.
    ///
    /// <para>The renderer emits a dotted name as one tag — <c>&lt;TYPE&gt;A.B.C&lt;/TYPE&gt;</c> —
    /// where the classifier tags each IDENT, so the parts are split out to compare like with like.
    /// Not on a dot inside a quoted identifier: <c>ModelicaReference</c> has a class genuinely
    /// called <c>'Connections.branch()'</c>, and splitting inside it invents tokens no lexer
    /// produced.</para>
    /// </summary>
    private static List<(string Category, string Text)> WordTokens(IEnumerable<string> markupLines)
    {
        var result = new List<(string, string)>();
        foreach (var line in markupLines)
            foreach (Match match in WordRegex.Matches(line))
                foreach (var part in SplitName(match.Groups[2].Value))
                    result.Add((match.Groups[1].Value, part));
        return result;
    }

    private static IEnumerable<string> SplitName(string text)
    {
        var start = 0;
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'')
                quoted = !quoted;
            else if (text[i] == '.' && !quoted)
            {
                if (i > start)
                    yield return text[start..i];
                start = i + 1;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }
}

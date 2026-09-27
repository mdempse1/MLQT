using System.Diagnostics;
using System.Text;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

/// <summary>
/// B409 — a comment between a class's name and its description string.
///
/// <para><b>What it was.</b> The grammar keeps <c>//</c> and <c>/* */</c> comments as real tokens so
/// the formatter can write them back, and accepts them only where it lists them. The class header
/// was not one of those places, so <c>function f</c> / <c>// note</c> / <c>"description"</c> was a
/// syntax error — and error recovery then lost the nesting, so every class after it in a
/// single-file library came back without its enclosing package.</para>
///
/// <para><b>The fix</b> is in <c>string_comment</c>: comments may come before the STRING, and belong
/// to the rule only when a STRING follows them. That covers every description, not only a class's:
/// a component's, a short class's, an enumeration literal's. <c>comment</c> takes comments before an
/// annotation on the same terms.</para>
/// </summary>
public class DescriptionCommentTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    private static readonly string Reproduction = Normalise("""
        package P
          function f
            // a comment here
            "description"
            input Real x;
          end f;
          model g "after it"
          end g;
        end P;
        """);

    [Fact]
    public void ACommentBeforeAClassDescriptionParses()
    {
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(Reproduction);
        Assert.Empty(errors);
    }

    [Fact]
    public void TheClassesAfterItKeepTheirPackage()
    {
        // The damage the defect did: recovery detached every later class from its package.
        var models = ModelicaParserHelper.ExtractModels(Reproduction);

        Assert.Equal("P", models.Single(m => m.Name == "f").ParentModelName);
        Assert.Equal("P", models.Single(m => m.Name == "g").ParentModelName);
    }

    [Fact]
    public void TheDescriptionIsStillTheDescription()
    {
        var tree = ModelicaParserHelper.Parse(Reproduction);
        var f = tree.class_definition()[0].class_specifier().long_class_specifier().composition()
                    .element_list()[0].element()[0].class_definition().class_specifier().long_class_specifier();

        var description = f.string_comment();
        Assert.Equal("\"description\"", Assert.Single(description.STRING()).GetText());
        Assert.Equal("// a comment here", Assert.Single(description.c_comment()).GetText().Trim());
    }

    [Theory]
    [InlineData("model M // trailing\n  \"d\"\n  Real x;\nend M;")]
    [InlineData("model M /* block */ \"d\"\n  Real x;\nend M;")]
    [InlineData("model M\n  // one\n  /* two */\n  // three\n  \"d\"\n  Real x;\nend M;")]
    [InlineData("model extends Base // note\n  \"d\"\n  Real x;\nend Base;")]
    [InlineData("model extends Base(k = 1) // note\n  \"d\"\nend Base;")]
    [InlineData("model M \"m\"\n  Real x // why\n    \"the x\";\nend M;")]
    [InlineData("model M \"m\"\n  type T = Real // why\n    \"a type\";\nend M;")]
    [InlineData("model M \"m\"\n  type E = enumeration(a // why\n    \"first\", b \"second\");\nend M;")]
    [InlineData("model M \"m\"\n  Real x \"d\" // note\n    annotation(Evaluate = true);\nend M;")]
    [InlineData("model M \"m\"\n  Real x\n    // note\n    annotation(Evaluate = true);\nend M;")]
    [InlineData("model M \"m\"\n  Real x;\nequation\n  x = 1 \"d\" /* b */ annotation(foo = 1);\nend M;")]
    public void EveryDescriptionMayHaveCommentsBeforeIt(string source)
    {
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.Empty(errors);
    }

    [Fact]
    public void CommentsWithNoDescriptionAfterThemStayInTheClassBody()
    {
        // The header takes comments only when a string follows. Otherwise they are the first thing
        // in the body, where they have always been - the renderer and ClassInterfaceExtractor both
        // read them there.
        var tree = ModelicaParserHelper.Parse("model M\n  // body\n  Real x;\nend M;");
        var spec = tree.class_definition()[0].class_specifier().long_class_specifier();

        Assert.Empty(spec.string_comment().c_comment());
        Assert.Equal("// body", Assert.Single(spec.composition().element_list()[0].c_comment()).GetText().Trim());
    }

    [Fact]
    public void TheRendererKeepsACommentOnItsOwnLine()
    {
        TestHelpers.AssertClass(Normalise("""
            package P

              function f
                // a comment here
                "description"
                input Real x;
              end f;
            end P;
            """));
    }

    [Fact]
    public void TheRendererKeepsATrailingCommentOnTheNameLine()
    {
        TestHelpers.AssertClass(Normalise("""
            model M // trailing
              "d"
              Real x;
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsSeveralCommentsInOrder()
    {
        TestHelpers.AssertClass(Normalise("""
            model extends Base // on the name
              // one
              /* two */
              "d"
              Real x;
            end Base;
            """));
    }

    [Fact]
    public void TheRendererKeepsACommentBeforeAComponentDescription()
    {
        TestHelpers.AssertClass(Normalise("""
            model M "m"
              Real x // why
                "the x";
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsCommentsBeforeAnAnnotation()
    {
        TestHelpers.AssertClass(Normalise("""
            model M "m"
              Real x "d" // note
                annotation (Evaluate=true);
              Real y
                // own line
                annotation (Evaluate=true);
            end M;
            """));
    }

    [Fact]
    public void RenderingIsStable()
    {
        // What is written must read back as itself, or every save moves the comment again.
        var once = TestHelpers.FormatCode(Reproduction);
        Assert.Equal(once, TestHelpers.FormatCode(once));
        Assert.Contains("// a comment here", once);
    }

    // ── B235: nothing here may make a run of comments quadratic again ─────────────

    private static string HeaderComments(int count, bool description) =>
        new StringBuilder()
            .Append("model Big\n")
            .Append(string.Concat(Enumerable.Range(0, count).Select(i => $"  // a comment {i}\n")))
            .Append(description ? "  \"the description\"\n" : "")
            .Append("  parameter Real p = 1.0;\n")
            .Append("end Big;\n")
            .ToString();

    /// <summary>
    /// Growth, not a threshold, as in <see cref="TrailingCommentParseTests"/>. The shape that can go
    /// wrong is comments with <b>no</b> description after them: the header has to look past the
    /// whole run to leave it to the class body, and a grammar that lets the header take such a run
    /// as well (tried: <c>(c_comment* STRING ('+' STRING)* | c_comment+)?</c>) rescans it at every
    /// comment - 2.8s for 500 and 41s for 2,000. With a description after them the loop ends on the
    /// STRING one token ahead whatever the grammar around it does, so that shape is not measured:
    /// it parses 4,000 in a few milliseconds, where timing is noise.
    /// </summary>
    [Fact]
    public void TwiceTheHeaderCommentsIsNotFourTimesTheWork()
    {
        ModelicaParserHelper.ParseWithTokens(HeaderComments(50, description: false));

        // The times are in ticks, the smaller is the best of five, and the larger gets up to five
        // tries to come in under the bar: the whole suite runs in parallel, and a sample of a few
        // milliseconds is at the mercy of whatever else the machine is doing.
        var small = Enumerable.Range(0, 5).Min(_ => Time(HeaderComments(400, description: false)));
        var bar = small * 8;
        var large = long.MaxValue;
        for (var i = 0; i < 5 && large >= bar; i++)
            large = Math.Min(large, Time(HeaderComments(1600, description: false)));

        Assert.True(large < bar,
            $"400 comments took {small} ticks and 1,600 took {large} - that is superlinear (B235). "
            + "See the comment on string_comment in modelica.g4.");
    }

    private static long Time(string source)
    {
        var clock = Stopwatch.StartNew();
        var (tree, _, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(source);
        clock.Stop();

        Assert.NotNull(tree);
        Assert.Empty(errors);
        return Math.Max(1, clock.ElapsedTicks);
    }
}

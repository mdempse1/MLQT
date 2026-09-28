using System.Text;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// B431 - comments inside a bracketed list: after the separators between the arguments of a
/// modification or a function call, the elements of an array, the rows of a matrix and the literals
/// of an enumeration; after the opening bracket and before the closing one; before the ',' of an
/// enumeration; and before 'constrainedby' inside a modification.
///
/// <para>Every list takes one shape in the grammar (see the comment on <c>argument_list</c> in
/// modelica.g4): a run after a separator ends on the next item, and a run after the last item is the
/// bracketing rule's and ends on the bracket, so no run is rescanned per comment (B235) - held by the
/// growth tests below. A comment before a separator is refused outside an enumeration, because
/// taking it makes every separator in every file a prediction; the grammar comment has the
/// measurement. As with B409 and B432, a parse error here detached every later class in a
/// single-file library from its package.</para>
/// </summary>
public class ListCommentTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    public static TheoryData<string> Positions() => new()
    {
        // a modification's arguments
        "model M\n  Real x(start=1, // why\n    fixed=true);\nend M;",
        "model M\n  Real x( // opening\n    start=1, // after the comma\n    fixed=true // last\n    );\nend M;",
        "model M\n  Real x(\n    // own line\n    start=1,\n    /* block */ fixed=true\n    // closing\n    ) \"d\";\nend M;",
        "model M\n  Real x(\n    // nothing but a comment\n    );\nend M;",
        "model M\n  extends B(k=1, // why\n    break y);\nend M;",
        "model M\n  extends B( // opening\n    k=1 // closing\n    );\n  extends C(a=1, b=2, c=3\n    // closing on its own line\n    );\nend M;",
        // 'constrainedby' inside a modification
        "model M\n  S s(redeclare replaceable package P = Q // why\n    constrainedby R, k=1);\nend M;",
        // function call arguments, positional and named
        "model M\n  Real x = f(1, // why\n    2);\nequation\n  x = g( // opening\n    a=1, // named\n    b=2 // last\n    );\nend M;",
        "function f\n  output Real x;\nalgorithm\n  x := h(x, /* c */ y\n    // closing\n    );\nend f;",
        "function f\n  output Real x;\nalgorithm\n  x := h(function g(a=1), // positional\n    k, // named next\n    b=2);\nend f;",
        // arrays and matrices - a data table with a note on each row is the real case
        "model M\n  parameter Real t[:, :] = [\n    // time, value\n    0, 0; // start\n    1, 10; // ramp\n    2, 10 // end\n    ];\nend M;",
        "model M\n  parameter Real t[:, :] = [1, // a\n    2; // after the semicolon\n    3, 4];\nend M;",
        "model M\n  parameter Real u[:] = {1, // one\n    2, /* two */\n    3 // three\n    };\nend M;",
        "model M\n  parameter Real u[:] = { // opening\n    1, 2};\nend M;",
        // an enumeration: before a ',', after the '(' and before the ')'
        "model M\n  type E = enumeration( // opening\n    a \"A\" // before the comma\n    , b \"B\", // after it\n    c \"C\" // last\n    );\nend M;",
        // inside an annotation, graphics included
        "model M\n  annotation(Documentation(info=\"x\"), // why\n    Icon(graphics={Line(points={{0, 0}, // origin\n      {10, 10}}, color={0, 0, 255})}));\nend M;",
    };

    [Theory]
    [MemberData(nameof(Positions))]
    public void EachPositionParses(string source)
    {
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.Empty(errors);
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void RenderingKeepsTheCommentsAndIsStable(string source)
    {
        var once = TestHelpers.FormatCode(source);

        // Every comment, and each only once: a comment dropped on save is the user's text lost.
        foreach (var comment in CommentsIn(source))
            Assert.Single(CommentsIn(once), c => c == comment);
        Assert.Equal(CommentsIn(source).Count, CommentsIn(once).Count);
        Assert.Equal(once, TestHelpers.FormatCode(once));
    }

    [Theory]
    [MemberData(nameof(Positions))]
    public void TheViewerKeepsTheCommentsInOrder(string source)
    {
        var (tree, tokens) = ModelicaParserHelper.ParseWithTokens(source);
        var renderer = new ModelicaRenderer(true, showAnnotations: true, excludeClassDefinitions: false,
            tokens, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.VisitStored_definition(tree);

        var shown = string.Join("\n", renderer.Code);
        var at = 0;
        foreach (var comment in CommentsIn(source))
        {
            var next = shown.IndexOf($"<COMMENT>{comment}</COMMENT>", at, StringComparison.Ordinal);
            Assert.True(next >= 0, $"'{comment}' is missing, or out of order, in:\n{shown}");
            at = next;
        }
    }

    [Fact]
    public void CommentsInsideAnAnnotationAreHiddenWithIt()
    {
        var (tree, tokens) = ModelicaParserHelper.ParseWithTokens(
            "model M\n  Real x annotation(Dialog(group=\"g\", // why\n    enable=true));\nend M;");
        var renderer = new ModelicaRenderer(false, showAnnotations: false, excludeClassDefinitions: false,
            tokens, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.VisitStored_definition(tree);

        Assert.DoesNotContain("why", string.Join("\n", renderer.Code));
    }

    [Fact]
    public void TheClassesAfterThemKeepTheirPackage()
    {
        // The damage a parse error did: recovery detached every later class from its package.
        var source = Normalise("""
            package P
              model M
                Real x(start=1, // why
                  fixed=true);
                parameter Real t[:, :] = [0, 0; // start
                  1, 1];
              equation
                x = f(1, // why
                  2);
              end M;
              type E = enumeration(a // before the comma
                , b);
              model G "after them"
              end G;
            end P;
            """);

        var models = ModelicaParserHelper.ExtractModels(source);

        Assert.Equal("P", models.Single(m => m.Name == "M").ParentModelName);
        Assert.Equal("P", models.Single(m => m.Name == "G").ParentModelName);
    }

    [Fact]
    public void EachRunBelongsToTheRuleTheTokenAfterItNames()
    {
        var declaration = ModelicaParserHelper.Parse(
                "model M\n  Real x(start=1, // a\n    fixed=true // b\n    \"desc\" // c\n    );\nend M;")
            .class_definition()[0].class_specifier().long_class_specifier().composition()
            .element_list()[0].element()[0].component_clause().component_list().component_declaration()[0];
        var modification = declaration.declaration().modification().class_modification();

        // after the ',' - the list's; before a STRING - the description's (B409); before the ')' -
        // the closing run, the bracketing rule's
        Assert.Equal("// a", Assert.Single(modification.argument_list().c_comment()).GetText());
        var fixedArgument = modification.argument_list().argument()[1]
            .element_modification_or_replaceable().element_modification();
        Assert.Equal("// b", Assert.Single(fixedArgument.string_comment().c_comment()).GetText());
        Assert.Equal("// c", Assert.Single(modification.c_comment()).GetText());
    }

    [Fact]
    public void TheRendererKeepsModificationCommentsWhereTheyStood()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real x(start=1, // why
                fixed=true);
              Real z(
                // own line first
                start=1,
                fixed=true,
                nominal=2
                // own line last
              ) "desc";
            end M;
            """));
    }

    [Fact]
    public void TheRendererWritesACommentBeforeAnEnumerationCommaAfterIt()
    {
        // Kept before the ',', a line comment would leave the ',' alone at the start of the next
        // line - as B432 does for a ';'. After it, the next save reads it back the same.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  type E = enumeration(
                    a "A" // before
                    , b "B");
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  type E = enumeration(
                    a "A", // before
                    b "B"
                  );
                end M;
                """));
    }

    [Theory]
    [InlineData("model M\n  Real x(start=1 // why\n    , fixed=true);\nend M;")]
    [InlineData("model M\n  Real x = f(1 // why\n    , 2);\nend M;")]
    [InlineData("model M\n  parameter Real t[:, :] = [0, 1 // why\n    ; 2, 3];\nend M;")]
    public void ACommentBeforeASeparatorOutsideAnEnumerationIsStillRefused(string source)
    {
        // Deliberately, and for a measured reason: taking it makes every ',' and ';' in every list
        // a prediction rather than a one-token switch - 61% more predictions over MSL and
        // Buildings, and parsing 5-8% slower - for a position `a=1, // why` makes unnecessary.
        // If this starts to pass, read the comment on argument_list in modelica.g4 first.
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(source);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void TheRendererKeepsATableWithANoteOnEachRow()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real table[:, :]=[
                // time, value
                0, 0; // start
                1, 10; // ramp
                2, 10 // end
              ] "a table";
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsCallCommentsWhereTheyStood()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real x;

            equation
              x = f(1, // why
                2);
              x = g( // opening
                a=1, // named
                b=2 // last named
              );
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsArrayAndEnumerationCommentsWhereTheyStood()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real u[:]={1, // one
                2, 3 // three
              };
              type E = enumeration( // opening
                a "A",
                b "B" // last
              );
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsCommentsBeforeConstrainedbyInAModification()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              S s(redeclare replaceable package P = Q // why
                constrainedby R, k=1);
            end M;
            """));
    }

    private static List<string> CommentsIn(string source) =>
        ModelicaTokenClassifier.TokensOnly(source).GetTokens()
            .Where(t => t.Type is modelicaLexer.LINE_COMMENT or modelicaLexer.COMMENT)
            .Select(t => t.Text.Trim())
            .ToList();

    // ── B235: nothing here may make a run of comments quadratic again ─────────────

    private static string Run(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"    // a comment {i}\n"));

    /// <summary>
    /// The shapes that could rescan a run: each has a run where two rules could want it, and the
    /// token after the run is what says whose it is. The first is the one a plausible grammar gets
    /// wrong - comments allowed both before and after an optional list, so a list holding nothing
    /// but comments leaves two loops competing for every one of them.
    /// </summary>
    public static TheoryData<string> Shapes() => new()
    {
        "modification of nothing but comments",
        "modification closing run",
        "modification closing run after a description",
        "call closing run",
        "call run before a named argument",
        "table closing run",
        "array closing run",
        "enumeration closing run",
        "enumeration run before a comma",
        "constrainedby run in a modification",
    };

    private static string Shape(string shape, int count) => new StringBuilder()
        .Append("model Big\n")
        .Append(shape switch
        {
            "modification of nothing but comments" => $"  Real x(\n{Run(count)}  );\n",
            "modification closing run" => $"  Real x(start=1\n{Run(count)}  );\n",
            "modification closing run after a description" => $"  Real x(start=1 \"d\"\n{Run(count)}  );\n",
            "call closing run" => $"  Real x = f(1, a=2\n{Run(count)}  );\n",
            "call run before a named argument" => $"  Real x = f(1,\n{Run(count)}  a=2);\n",
            "table closing run" => $"  parameter Real t[:, :] = [0, 1\n{Run(count)}  ];\n",
            "array closing run" => $"  parameter Real t[:] = {{0, 1\n{Run(count)}  }};\n",
            "enumeration closing run" => $"  type E = enumeration(a, b\n{Run(count)}  );\n",
            "enumeration run before a comma" => $"  type E = enumeration(a\n{Run(count)}  , b);\n",
            "constrainedby run in a modification" =>
                $"  S s(redeclare replaceable package P = Q\n{Run(count)}  constrainedby R, k=1);\n",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        })
        .Append("end Big;\n")
        .ToString();

    /// <summary>
    /// Growth of the lookahead, counted rather than timed (<see cref="ParseGrowth"/>, B459). Checked
    /// against the plausible wrong grammars - a run allowed both before and after an optional
    /// modification list, and a closing run inside an enumeration's list as well as after it - and
    /// each fails here by a factor of about sixteen.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void TwiceTheCommentsIsNotFourTimesTheLookahead(string shape)
        => ParseGrowth.AssertLinear(count => Shape(shape, count), $"comments ({shape})", "argument_list");
}

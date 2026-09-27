using System.Text;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

/// <summary>
/// B432 - the smaller comment positions B409 left as parse errors: before a class's leading
/// annotation, after the ',' in an enumeration, between an equation or statement and its ';', before
/// 'constrainedby', and between an extends clause and its annotation.
///
/// <para>Each is taken on B409's terms (see <see cref="DescriptionCommentTests"/>): the comments
/// belong to the new position only when the token that position needs follows them, so the choice is
/// made once and a run of comments is not rescanned per comment (B235). A parse error in any of them
/// detaches every later class in a single-file library from its package, which is why they matter
/// more than their frequency suggests.</para>
/// </summary>
public class CommentPositionTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    public static TheoryData<string> Positions() => new()
    {
        // before the leading class annotation
        "model M\n  // about the icon\n  annotation(Documentation(info=\"x\"));\n  Real x;\nend M;",
        "model M \"d\" // on the name\n  annotation(Documentation(info=\"x\"));\n  Real x;\nend M;",
        "model M\n  /* one */\n  // two\n  annotation(Documentation(info=\"x\"));\nend M;",
        // after the ',' in an enumeration
        "model M\n  type E = enumeration(\n    a \"A\", // first\n    b \"B\",\n    // own line\n    c \"C\");\nend M;",
        // between an equation or a statement and its ';'
        "model M\n  Real x;\nequation\n  x = 2 // before\n  ;\nend M;",
        "model M\n  Real x;\nequation\n  if true then\n    x = 1 /* b */ ;\n  else\n    x = 2\n    // own\n    ;\n  end if;\nend M;",
        "function f\n  output Real x;\nalgorithm\n  x := 1 // s\n  ;\nend f;",
        // before 'constrainedby'
        "model M\n  replaceable package P = Q // why\n    constrainedby R;\n  replaceable Real x = 1\n    // own\n    constrainedby Real;\nend M;",
        // between an extends clause and its annotation
        "model M\n  extends B(k = 1) // c\n    annotation(IconMap(primitivesVisible=false));\n  extends C\n    // own\n    annotation(IconMap(primitivesVisible=false));\nend M;",
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

        foreach (var comment in CommentsIn(source))
            Assert.Contains(comment, once);
        Assert.Equal(once, TestHelpers.FormatCode(once));
    }

    [Fact]
    public void TheClassesAfterThemKeepTheirPackage()
    {
        // The damage a parse error did: recovery detached every later class from its package.
        var source = Normalise("""
            package P
              type E = enumeration(a, // why
                b);
              model M
                // about the icon
                annotation(Documentation(info="x"));
                extends B // c
                  annotation(IconMap(primitivesVisible=false));
                replaceable Real x = 1 // own
                  constrainedby Real;
              equation
                x = 2 // before
                ;
              end M;
              model G "after them"
              end G;
            end P;
            """);

        var models = ModelicaParserHelper.ExtractModels(source);

        Assert.Equal("P", models.Single(m => m.Name == "M").ParentModelName);
        Assert.Equal("P", models.Single(m => m.Name == "G").ParentModelName);
    }

    [Fact]
    public void CommentsBeforeTheLeadingAnnotationAreTheCompositionsOnlyWhenAnAnnotationFollows()
    {
        var withAnnotation = ModelicaParserHelper.Parse("model M\n  // c\n  annotation(Evaluate=true);\n  Real x;\nend M;")
            .class_definition()[0].class_specifier().long_class_specifier().composition();
        var withoutAnnotation = ModelicaParserHelper.Parse("model M\n  // c\n  Real x;\nend M;")
            .class_definition()[0].class_specifier().long_class_specifier().composition();

        Assert.Equal("// c", Assert.Single(withAnnotation.c_comment()).GetText().Trim());
        Assert.Empty(withAnnotation.element_list()[0].c_comment());

        Assert.Empty(withoutAnnotation.c_comment());
        Assert.Equal("// c", Assert.Single(withoutAnnotation.element_list()[0].c_comment()).GetText().Trim());
    }

    [Fact]
    public void TheRendererMovesCommentsBeforeTheLeadingAnnotationWithIt()
    {
        // The leading annotation is written at the end of the class, as it always was, and its
        // comments go with it - above the blank line, where the next save reads them back the same.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  // about the icon
                  annotation(Documentation(info="x"));
                  Real x;
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  Real x;
                  // about the icon

                  annotation (
                    Documentation(info="x")
                  );
                end M;
                """));
    }

    [Fact]
    public void CommentsBeforeTheLeadingAnnotationAreHiddenWithIt()
    {
        var (tree, tokens) = ModelicaParserHelper.ParseWithTokens(
            "within;\nmodel M\n  // about the icon\n  annotation(Documentation(info=\"x\"));\n  Real x;\nend M;");
        var renderer = new ModelicaRenderer(false, showAnnotations: false, excludeClassDefinitions: false,
            tokens, classNamesToExclude: null, formatting: FormattingOptions.None);
        renderer.VisitStored_definition(tree);

        Assert.DoesNotContain("about the icon", string.Join("\n", renderer.Code));
    }

    [Fact]
    public void TheRendererKeepsEnumerationCommentsWhereTheyStood()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              type E = enumeration(
                a "A", // first
                b "B",
                // own line
                c "C"
              );
            end M;
            """));
    }

    [Fact]
    public void TheRendererWritesCommentsBeforeASemicolonAfterIt()
    {
        // Kept before the ';', a line comment would leave the ';' alone on the next line; after it,
        // each on its own line, is how a comment following an equation is already written.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real x;
                equation
                  x = 2 // before
                  ;
                algorithm
                  x := 1 /* s */ ;
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  Real x;

                equation
                  x = 2;
                  // before

                algorithm
                  x := 1;
                  /* s */
                end M;
                """));
    }

    [Fact]
    public void TheRendererKeepsCommentsBeforeConstrainedby()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              replaceable package P = Q // why
                constrainedby R;
              replaceable Real x=1
                // own
                constrainedby Real;
            end M;
            """));
    }

    [Fact]
    public void TheRendererKeepsCommentsBeforeAnExtendsAnnotation()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              extends B(k=1) // c
                annotation (IconMap(primitivesVisible=false));
              extends C
                // own
                annotation (IconMap(primitivesVisible=false));
            end M;
            """));
    }

    [Fact]
    public void AnEquationWithACommentBeforeItsSemicolonIsStillAnEquation()
    {
        // equation_or_comment is comment-only when it has no equation, not when it has comments:
        // reading c_comment() to decide dropped the equation and gave its comment to the next one.
        var behavior = BehaviorExtractor.ExtractFromCode(Normalise("""
            model M
              Real x;
              Real y;
            equation
              x = 2 // why
              ;
              y = 3;
            algorithm
            end M;
            """));

        Assert.Equal(["x = 2", "y = 3"], behavior.Equations.Select(e => e.Text));
        Assert.All(behavior.Equations, e => Assert.Empty(e.LeadingComments));
    }

    [Fact]
    public void AStatementWithACommentBeforeItsSemicolonIsStillAStatement()
    {
        var behavior = BehaviorExtractor.ExtractFromCode(Normalise("""
            function f
              output Real x;
            algorithm
              x := 1 // why
              ;
              x := 2;
            end f;
            """));

        Assert.Equal(["x := 1", "x := 2"], behavior.Statements.Select(s => s.Text));
        Assert.All(behavior.Statements, s => Assert.Empty(s.LeadingComments));
    }

    private static IEnumerable<string> CommentsIn(string source) =>
        ModelicaTokenClassifier.TokensOnly(source).GetTokens()
            .Where(t => t.Type is modelicaLexer.LINE_COMMENT or modelicaLexer.COMMENT)
            .Select(t => t.Text.Trim());

    // ── B235: nothing here may make a run of comments quadratic again ─────────────

    private static string BodyComments(int count) =>
        new StringBuilder()
            .Append("model Big\n")
            .Append(string.Concat(Enumerable.Range(0, count).Select(i => $"  // a comment {i}\n")))
            .Append("  parameter Real p = 1.0;\n")
            .Append("end Big;\n")
            .ToString();

    /// <summary>
    /// Growth, not a threshold, as in <see cref="DescriptionCommentTests"/>. The shape that could go
    /// wrong here is a class body that opens with comments and no annotation after them: the leading
    /// annotation has to look past the whole run to leave it to the element list, which must happen
    /// once, not once per comment.
    /// </summary>
    [Fact]
    public void TwiceTheBodyCommentsIsNotFourTimesTheWork()
        => ParseGrowth.AssertLinear(BodyComments, "comments opening a class body", "composition");
}

namespace ModelicaParser.Tests;

/// <summary>
/// B464 - an argument list wrapped for length starts its continuation with the first argument when
/// keeping that argument on the opening line would take the line past the maximum length. Before
/// this only the later arguments were given that test, so a Buildings
/// <c>TimeTable timTabLin(smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments,</c>
/// stayed at about 110 characters on the declaration's line however the source was laid out.
///
/// <para>Most of these use a maximum of 40 so the fixtures stay readable; the length is measured as
/// the renderer measures it everywhere, without the line's indentation.</para>
/// </summary>
public class FirstArgumentWrapTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void TheBuildingsDeclarationStartsItsContinuationWithTheFirstArgument()
    {
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments, period=5)
                    "Time table";
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(
                    smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments, period=5) "Time table";
                end M;
                """));
    }

    [Fact]
    public void AFirstArgumentThatFitsStaysOnTheOpeningLine()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Tt tt(a=1234567890123456789012345678901,
                b=2);
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void AFirstArgumentOneCharacterPastTheLimitStartsTheContinuation()
    {
        // One digit more than the test above: the opening line, with the ',' after the argument,
        // would be 41 characters.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Tt tt(a=12345678901234567890123456789012, b=2);
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  Tt tt(
                    a=12345678901234567890123456789012,
                    b=2);
                end M;
                """),
            maxLineLength: 40);
    }

    [Fact]
    public void ASingleArgumentIsNotMoved()
    {
        // A list of one does not wrap, so there is no continuation for it to start.
        TestHelpers.AssertClass(Normalise("""
            model M
              Tt tt(a=12345678901234567890123456789012345678901234567890);
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void AFirstArgumentTheRendererBreaksOverLinesKeepsItsLayout()
    {
        // A data table keeps its author's rows (B462); its last line is long, but it is not the
        // opening line, and nothing of the table is moved.
        TestHelpers.AssertClass(Normalise("""
            model M
              Tt tt(table=[0, 0;
                1, 1111111111111111111111111111111111111111],
                b=2);
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void ACommentAfterTheBracketKeepsTheLineItLeaves()
    {
        // B431: the comment ends the opening line, and the first argument is already on a
        // continuation line of its own - even one too long for it, which is not moved again.
        TestHelpers.AssertClass(Normalise("""
            model M
              Tt tt( // c
                a=12345678901234567890123456789012345678901234567890,
                b=2);
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void AListInsideOneWrittenAnArgumentALineIsLeftAsItWas()
    {
        // The annotation's modification is written an argument a line, and a call inside it
        // continues at that level; moving only its first argument a level in would misalign them.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Real k=1 annotation (Dialog(group="Nominal", enable=Modelica.Math.isEqual(s1=123456789012345678901234567890, s2=2)));
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  parameter Real k=1
                    annotation (Dialog(
                      group="Nominal",
                      enable=Modelica.Math.isEqual(s1=123456789012345678901234567890,
                      s2=2)
                    ));
                end M;
                """),
            maxLineLength: 40);
    }

    [Fact]
    public void AModificationWrittenOneArgumentALineIsUnchanged()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Tt tt(
                a=1,
                b=2,
                c=3,
                d=4,
                e=5,
                f=12345678901234567890123456789012345678901234567890
              );
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void TheFirstNamedArgumentOfAFunctionCallStartsTheContinuation()
    {
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Real y;
                  Real u;

                equation
                  y = homotopy(actual=smooth(0, noEvent(if u > 1 then 1 else u)), simplified=u);
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  Real y;
                  Real u;

                equation
                  y = homotopy(
                    actual=smooth(0, noEvent(if u > 1 then 1 else u)),
                    simplified=u);
                end M;
                """),
            maxLineLength: 40);
    }

    [Fact]
    public void AnExtendsModificationTooLongForItsLineIsAlreadyWrittenOneArgumentALine()
    {
        // The extends clause measures its whole list before it opens it, so its first argument was
        // never left on the opening line - the layout B464 brings the other lists closer to.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  extends Base(redeclare package Medium = Modelica.Media.Water.StandardWater, k=1);
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  extends Base(
                    redeclare package Medium = Modelica.Media.Water.StandardWater,
                    k=1
                  );
                end M;
                """),
            maxLineLength: 40);
    }

    [Fact]
    public void AGraphicsAnnotationIsLaidOutByItsOwnRules()
    {
        TestHelpers.AssertClass(Normalise("""
            model M

              annotation (Icon(graphics={Line(points={{-100, 0}, {100, 0}},
                color={0, 0, 127})}));
            end M;
            """), maxLineLength: 40);
    }

    [Fact]
    public void TheCodeViewerMovesTheSameArgument()
    {
        // Measured without the highlighting tags, so the viewer breaks where the saved file does.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  Tt tt(a=12345678901234567890123456789012, b=2);
                end M;
                """),
            renderForCodeEditor: true,
            expectedOutput: Normalise("""
                <KEYWORD>model</KEYWORD> <IDENT>M</IDENT>
                  <TYPE>Tt</TYPE> <IDENT>tt</IDENT>(
                    <NAME>a</NAME><OPERATOR>=</OPERATOR><NUMBER>12345678901234567890123456789012</NUMBER>,
                    <NAME>b</NAME><OPERATOR>=</OPERATOR><NUMBER>2</NUMBER>);
                <KEYWORD>end</KEYWORD> <IDENT>M</IDENT>;
                """),
            maxLineLength: 40);
    }

    public static TheoryData<string> Sources() => new()
    {
        "model M\n  Tt tt(a=12345678901234567890123456789012, b=2);\nend M;",
        "model M\n  Tt tt(\n    a=12345678901234567890123456789012,\n    b=2, c=3);\nend M;",
        "model M\n  Tt tt(a=1, b=12345678901234567890123456789012);\nend M;",
        "model M\n  Tt tt(table=[\n    0, 0;\n    1, 1], b=12345678901234567890123456789012);\nend M;",
        "model M\n  Tt tt( // c\n    a=12345678901234567890123456789012, b=2);\nend M;",
        "model M\n  Tt tt(a=12345678901234567890123456789012, // c\n    b=2);\nend M;",
        "model M\n  Real y = f(x=12345678901234567890123456789012, z=2);\nend M;",
        "model M\n  extends Base(redeclare package Medium = Modelica.Media.Water.StandardWater, k=1);\nend M;",
        "model M\n  Tt tt(m(min=12345678901234567890123456789012, max=2), b=2);\nend M;",
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void RenderingIsStable(string source)
    {
        var once = TestHelpers.FormatCode(source, maxLineLength: 40);
        Assert.Equal(once, TestHelpers.FormatCode(once, maxLineLength: 40));
    }
}

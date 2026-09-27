using ModelicaParser.Comparison;

namespace ModelicaParser.Tests;

/// <summary>
/// B462 - the formatter keeps a matrix's rows where its author wrote them. A row that starts a line
/// in the source starts one on save, a level in from the line the matrix began on; rows written
/// together stay together, and a table written on one line stays on one line. The layout is read
/// from the tokens' line numbers, as B431 reads a comment's, so it holds with or without comments and
/// a saved file saves back unchanged.
///
/// <para>Before this, every matrix was joined onto one line with <c>"; "</c>, so a Buildings
/// <c>TimeTable(table=[...])</c> written a row a line came back as one line of 150 characters or
/// more, past the maximum line length.</para>
/// </summary>
public class MatrixRowTests
{
    private static string Normalise(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void ATableWrittenOnOneLineStaysOnOneLine()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[0, 0; 1, 10; 2, 10; 3, 0];
            end M;
            """));
    }

    [Fact]
    public void ATableWrittenARowALineKeepsItsRows()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[0, 0;
                1, 10;
                2, 10;
                3, 0];
            end M;
            """));
    }

    [Fact]
    public void RowsTheAuthorWroteTogetherStayTogether()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[0, 0; 1, 10;
                2, 10; 3, 0; 4, 0;
                5, 1];
            end M;
            """));
    }

    [Fact]
    public void TheSourceIndentOfARowIsNotKept()
    {
        // Only where the rows break is the author's; how far in they are is the renderer's.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Real t[:, :] = [0,0;
                                            1,10;
                     2,10];
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  parameter Real t[:, :]=[0, 0;
                    1, 10;
                    2, 10];
                end M;
                """));
    }

    [Fact]
    public void ABreakWrittenBeforeTheSemicolonKeepsTheRowOnItsOwnLine()
    {
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Real t[:, :] = [0, 0
                    ; 1, 10];
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  parameter Real t[:, :]=[0, 0;
                    1, 10];
                end M;
                """));
    }

    [Fact]
    public void ABreakInsideARowIsNotARowBreak()
    {
        // Only the row breaks are kept: elements of one row split over lines join up as before.
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Real t[:, :] = [0,
                    0; 1, 10];
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  parameter Real t[:, :]=[0, 0; 1, 10];
                end M;
                """));
    }

    [Fact]
    public void ARowAfterAStringSpanningLinesStartsALineOnlyIfItStartsOneInTheSource()
    {
        // A row is on a new line when it starts below where the row before it *ends*, and a string
        // can end lines below where it starts.
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter String t[:, :]=["a", "first
            second"; "b", "c";
                "d", "e"];
            end M;
            """));
    }

    [Fact]
    public void RowsInAWrappedArgumentListLineUpUnderTheArgument()
    {
        // The Buildings shape. The argument list is wrapped for length, which indents the lines it
        // writes and goes back a level before the table's last line is written; the rows must still
        // be one level in from the argument, the last row included. The first argument starts the
        // continuation too, as it would have taken the declaration's line past the limit (B464).
        TestHelpers.AssertClass(Normalise("""
            model M
              Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(
                smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments,
                table=[0, 0; 0.3, 1; 0.5, 0;
                  0.7, 1;
                  1, 0]) "Time table with smoothness method of constant segments";
            end M;
            """));
    }

    [Fact]
    public void RowsInAProtectedSectionKeepTheirIndent()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real x;
            protected
              parameter Real t[:, :]=[0, 0;
                1, 10];
            end M;
            """));
    }

    [Fact]
    public void RowsInAnEquationKeepTheirBreaks()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real y[2];
              Real x[2];

            equation
              y = [1, 2;
                3, 4]*x;
            end M;
            """));
    }

    [Fact]
    public void ANestedMatrixKeepsItsOwnRows()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[[1, 2;
                3, 4], [5; 6]];
            end M;
            """));
    }

    [Fact]
    public void CommentsAndRowBreaksTogether()
    {
        // B431's comments end their lines as they did; a row with no comment keeps its own break.
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[0, 0; // start
                1, 10;
                2, 10; 3, 10;
                4, 0 // end
              ];
            end M;
            """));
    }

    [Fact]
    public void CommentsInAWrappedArgumentListLineUpUnderTheArgument()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(
                smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments,
                table=[0, 0; // a
                  0.3, 1;
                  1, 0 // end
                ]) "Time table with smoothness method of constant segments";
            end M;
            """));
    }

    [Fact]
    public void AFirstRowWrittenBelowTheBracketStaysThere()
    {
        // B463: the '[' is checked as each row's ';' is, so a first row on the next line keeps it.
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[
                0, 0;
                1, 10;
                2, 10];
            end M;
            """));
    }

    [Fact]
    public void AFirstRowBelowTheBracketIsIndentedByTheRenderer()
    {
        TestHelpers.AssertClass(
            Normalise("""
                model M
                  parameter Real t[:, :] = [
                            0,0; 1,10;
                  2,10];
                end M;
                """),
            expectedOutput: Normalise("""
                model M
                  parameter Real t[:, :]=[
                    0, 0; 1, 10;
                    2, 10];
                end M;
                """));
    }

    [Fact]
    public void AOneRowTableBelowTheBracketStaysBelowIt()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[
                0, 1, 0];
            end M;
            """));
    }

    [Fact]
    public void AFirstRowBelowTheBracketInAWrappedArgumentListLinesUpWithTheOtherRows()
    {
        // The Buildings shape (CDL/Integers/Validation/Equal.mo): the '[' line is a continuation of
        // an argument list wrapped for length, and every row, the first included, is a level in.
        TestHelpers.AssertClass(Normalise("""
            model M
              Buildings.Controls.OBC.CDL.Reals.Sources.TimeTable timTabLin(
                smoothness=Buildings.Controls.OBC.CDL.Types.Smoothness.ConstantSegments,
                table=[
                  0, 0;
                  0.3, 1;
                  0.5, 0;
                  0.7, 1;
                  1, 0]) "Time table with smoothness method of constant segments";
            end M;
            """));
    }

    [Fact]
    public void AFirstRowBelowTheBracketInAnEquationKeepsItsLine()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              Real y[2];
              Real x[2];

            equation
              y = [
                1, 2;
                3, 4]*x;
            end M;
            """));
    }

    [Fact]
    public void ACommentOnTheBracketsLineStillEndsItBeforeTheFirstRow()
    {
        // B431's comment after the '[' was already followed by the first row on a line of its own;
        // the row is not moved a second line down.
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[ // start
                0, 0;
                1, 10 // end
              ];
            end M;
            """));
    }

    [Fact]
    public void ACommentOnItsOwnLineBeforeTheFirstRowIsKept()
    {
        TestHelpers.AssertClass(Normalise("""
            model M
              parameter Real t[:, :]=[
                // time, value
                0, 0;
                1, 10];
            end M;
            """));
    }

    public static TheoryData<string> Sources() => new()
    {
        "model M\n  parameter Real t[:, :] = [\n0, 0;\n    1, 10];\nend M;",
        "model M\n  parameter Real t[:, :] = [\n  0, 0; 1, 10];\nend M;",
        "model M\n  T tab(k=1, table=[\n  0,0; // a\n      1,0]) \"d\";\nend M;",
        "model M\n  parameter Real t[:, :] = [\n    [1, 2;\n    3, 4], [\n 5; 6]];\nend M;",
        "model M\n  parameter Real t[:, :] = [0, 0; 1, 10; 2, 10];\nend M;",
        "model M\n  parameter Real t[:, :] = [0, 0;\n    1, 10;\n    2, 10];\nend M;",
        "model M\n  parameter Real t[:, :] = [0, 0; 1, 10;\n 2, 10];\nend M;",
        "model M\n  parameter Real t[:, :] = [\n    0, 0; // start\n    1, 10;\n    2, 10 // end\n    ];\nend M;",
        "model M\n  T tab(k=1, table=[\n      0,0;\n      0.3,1;\n      1,0]) \"d\";\nend M;",
        "model M\n  Real y[2];\nequation\n  y = [1, 2;\n       3, 4] * {1, 1};\nend M;",
    };

    [Theory]
    [MemberData(nameof(Sources))]
    public void RenderingIsStable(string source)
    {
        var once = TestHelpers.FormatCode(source);
        Assert.Equal(once, TestHelpers.FormatCode(once));
    }

    [Fact]
    public void ReLayingOutATableIsACosmeticChange()
    {
        // What the formatter now does to a table - joining or breaking its rows - is a change of
        // layout, and the classifier must not call it a change to what is simulated.
        var oneLine = Normalise("""
            within MyLib;
            model Resistor
              parameter Real t[:, :] = [0, 0; 1, 10; 2, 10];
            end Resistor;
            """);
        var rows = Normalise("""
            within MyLib;
            model Resistor
              parameter Real t[:, :] = [0, 0;
                1, 10;
                2, 10];
            end Resistor;
            """);

        var kinds = ClassChangeClassifier.Compare(oneLine, rows);

        Assert.Equal(ClassChangeKind.Cosmetic, Assert.Contains("MyLib.Resistor", kinds));
    }
}

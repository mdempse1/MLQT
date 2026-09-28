using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// B445 — the text of a file outside every class, read once at load and put back by every writer
/// that rebuilds the file from a class's stored source.
/// </summary>
public class FileLevelTextTests
{
    private static FileLevelText? ReadFile(string file, string className = "M")
    {
        var start = file.IndexOf("model " + className, StringComparison.Ordinal);
        var endToken = "end " + className;
        var stop = file.LastIndexOf(endToken, StringComparison.Ordinal) + endToken.Length - 1;
        return FileLevelText.Read(file, start, stop);
    }

    #region Read

    [Fact]
    public void Read_TakesEachPartExactlyAsWritten()
    {
        var text = ReadFile("// header\n\nwithin P;  // after\n\nmodel M\nend M;  // trailer\n")!;

        Assert.Equal("// header\n\n", text.Leading);
        Assert.Equal("  // after\n\n", text.AfterWithin);
        Assert.Equal("  // trailer\n", text.Trailing);
    }

    [Fact]
    public void Read_IsNullForAFileWithOnlyWhitespaceOutsideTheClass()
        => Assert.Null(ReadFile("\nwithin P;\n\nmodel M\nend M;\n\n"));

    [Fact]
    public void Read_IsNullForAClassWithNoFileAroundIt()
        => Assert.Null(ReadFile("model M\nend M;"));

    [Fact]
    public void Read_InAFileWithNoClause_TakesTheTextAboveTheClass()
    {
        var text = ReadFile("/* header */\nmodel M\nend M;\n")!;

        Assert.Equal("/* header */\n", text.Leading);
        Assert.Equal("\n", text.AfterWithin);
        Assert.Equal("\n", text.Trailing);
    }

    [Fact]
    public void Read_DoesNotTakeAClassThatFollowsAsTrailingText()
    {
        // A second top-level class after the last one read is not a comment; nothing trails.
        const string file = "// header\nwithin;\nmodel M\nend M;\nmodel N\nend N;\n";

        var text = ReadFile(file)!;

        Assert.Equal("", text.Trailing);
    }

    [Fact]
    public void Read_DoesNotTakeTextAfterAClassWithNoSemicolon()
    {
        const string file = "// header\nwithin;\nmodel M\nend M // no semicolon\n";

        Assert.Equal("", ReadFile(file)!.Trailing);
    }

    [Fact]
    public void Read_IsNullForAnOffsetOutsideTheFile()
    {
        Assert.Null(FileLevelText.Read("// x\nmodel M end M;", -1, 3));
        Assert.Null(FileLevelText.Read("// x\nmodel M end M;", 99, 100));
    }

    [Fact]
    public void Read_IsNullForAClauseWithNoSemicolonBeforeTheClass()
        => Assert.Null(FileLevelText.Read("// x\nwithin P model M end M;", 14, 26));

    [Fact]
    public void Read_IgnoresATrailingStopBeforeTheClass()
        => Assert.Equal("", FileLevelText.Read("// x\nmodel M end M;", 5, 2)!.Trailing);

    #endregion

    #region ApplyTo and Ensure

    [Fact]
    public void Ensure_PutsEveryPartBackCharacterForCharacter()
    {
        const string file = "// header\n\nwithin P;  // after\n\nmodel M\nend M;  // trailer\n";
        var text = ReadFile(file);

        Assert.Equal(file, WithinClause.Ensure("model M\nend M;", "P", text));
    }

    [Fact]
    public void Ensure_WithNoFileText_IsThePlainEnsure()
        => Assert.Equal("within P;\nmodel M\nend M;", WithinClause.Ensure("model M\nend M;", "P", null));

    [Fact]
    public void Ensure_OfAWholeFile_LeavesItsOwnHeaderAlone()
    {
        const string whole = "// its own\nwithin P;\nmodel M\nend M;";

        Assert.Equal(whole, WithinClause.Ensure(whole, "P", ReadFile("// other\nwithin P;\nmodel M\nend M;")));
    }

    [Fact]
    public void Ensure_InAFileThatHadNoClause_PutsTheHeaderAboveTheAddedOne()
        => Assert.Equal("/* header */\nwithin;\nmodel M\nend M;",
            WithinClause.Ensure("model M\nend M;", null, ReadFile("/* header */\nmodel M\nend M;")));

    [Fact]
    public void Set_NamesTheNewParent_AndKeepsTheHeader()
        => Assert.Equal("// header\nwithin Q;\nmodel M\nend M;",
            WithinClause.Set("within P;\nmodel M\nend M;", "Q", ReadFile("// header\nwithin P;\nmodel M\nend M;")));

    [Fact]
    public void ApplyTo_TextWithoutAClause_IsUnchanged()
        => Assert.Equal("model M\nend M;", ReadFile("// h\nwithin;\nmodel M\nend M;")!.ApplyTo("model M\nend M;"));

    [Fact]
    public void ApplyTo_AClauseWithNoSemicolon_IsUnchanged()
        => Assert.Equal("within P", ReadFile("// h\nwithin;\nmodel M\nend M;")!.ApplyTo("within P"));

    #endregion

    #region Formatted

    [Fact]
    public void Formatted_IsWhatTheRendererWritesAtTheTopOfAFile()
    {
        var text = ReadFile("// header   \n\n/* block\n     kept */\n\nwithin P;\nmodel M\nend M;\n")!.Formatted();

        Assert.Equal("// header\n/* block\n     kept */\n", text.Leading);
        Assert.Equal("\n", text.AfterWithin);
        Assert.Equal("", text.Trailing);
    }

    [Fact]
    public void Formatted_PutsTheCommentsAfterTheClauseAndTheClassOnLinesOfTheirOwn()
    {
        var text = ReadFile("within P; // after\nmodel M\nend M; // trailer\n")!.Formatted();

        Assert.Equal("within P;\n// after\nmodel M\nend M;\n// trailer",
            text.ApplyTo("within P;\nmodel M\nend M;\n"));
    }

    #endregion

    #region AroundNested

    [Fact]
    public void AroundNested_PutsTheCommentsDirectlyAboveAndBelowTheClass()
    {
        var text = ReadFile("// header\n\nwithin P; // after\nmodel M\nend M; // trailer\n")!;

        Assert.Equal("// header\n// after\nmodel M\nend M;\n// trailer", text.AroundNested("model M\nend M;\n"));
    }

    [Fact]
    public void AroundNested_WithOnlyATrailer_LeavesTheTopOfTheClassAlone()
    {
        var text = ReadFile("within P;\nmodel M\nend M; // trailer\n")!;

        Assert.Equal("model M\nend M;\n// trailer", text.AroundNested("model M\nend M;"));
    }

    [Fact]
    public void AroundNested_WithOnlyAHeader_LeavesTheEndOfTheClassAlone()
    {
        var text = ReadFile("// header\nwithin P;\nmodel M\nend M;\n")!;

        Assert.Equal("// header\nmodel M\nend M;\n", text.AroundNested("model M\nend M;\n"));
    }

    #endregion
}

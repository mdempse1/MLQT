using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

public class WithinClauseTests
{
    #region Ensure

    [Fact]
    public void Ensure_AddsClauseWhenSourceHasNone()
    {
        var result = WithinClause.Ensure("model M\nend M;", "My.Package");

        Assert.Equal("within My.Package;\nmodel M\nend M;", result);
    }

    [Fact]
    public void Ensure_AddsBareClauseWhenParentIsNull()
    {
        Assert.Equal("within;\nmodel M\nend M;", WithinClause.Ensure("model M\nend M;", null));
    }

    [Fact]
    public void Ensure_AddsBareClauseWhenParentIsEmpty()
    {
        Assert.Equal("within;\nmodel M\nend M;", WithinClause.Ensure("model M\nend M;", ""));
    }

    [Fact]
    public void Ensure_LeavesAnExistingClauseAlone()
    {
        var source = "within Other.Package;\nmodel M\nend M;";

        Assert.Same(source, WithinClause.Ensure(source, "My.Package"));
    }

    [Fact]
    public void Ensure_LeavesAnExistingBareClauseAlone()
    {
        var source = "within;\npackage Modelica\nend Modelica;";

        Assert.Same(source, WithinClause.Ensure(source, "My.Package"));
    }

    [Fact]
    public void Ensure_LeavesAClauseBehindLeadingBlankLinesAlone()
    {
        // Rendered and hand-edited text can open with a blank line. Reading that as "no clause" is
        // exactly what wrote a second one into every file the incremental formatter touched.
        var source = "\n\nwithin My.Package;\nmodel M\nend M;";

        Assert.Same(source, WithinClause.Ensure(source, "My.Package"));
    }

    [Fact]
    public void Ensure_DoesNotMistakeAnIdentifierForAClause()
    {
        var result = WithinClause.Ensure("withinTolerance = 1;", "My.Package");

        Assert.Equal("within My.Package;\nwithinTolerance = 1;", result);
    }

    [Fact]
    public void Ensure_DoesNotMistakeAnIdentifierWithUnderscoreForAClause()
    {
        var result = WithinClause.Ensure("within_range = 1;", "My.Package");

        Assert.StartsWith("within My.Package;\n", result);
    }

    [Fact]
    public void Ensure_AddsClauseToEmptySource()
    {
        Assert.Equal("within My.Package;\n", WithinClause.Ensure("", "My.Package"));
    }

    #endregion

    #region Strip

    [Fact]
    public void Strip_RemovesTheClauseAndItsNewline()
    {
        Assert.Equal("model M\nend M;", WithinClause.Strip("within My.Package;\nmodel M\nend M;"));
    }

    [Fact]
    public void Strip_RemovesTheClauseWhenTheFileUsesCrlf()
    {
        Assert.Equal("model M\r\nend M;", WithinClause.Strip("within My.Package;\r\nmodel M\r\nend M;"));
    }

    [Fact]
    public void Strip_RemovesABareClause()
    {
        Assert.Equal("package Modelica\nend Modelica;", WithinClause.Strip("within;\npackage Modelica\nend Modelica;"));
    }

    [Fact]
    public void Strip_RemovesLeadingBlankLinesAlongWithTheClause()
    {
        Assert.Equal("model M\nend M;", WithinClause.Strip("\n\nwithin My.Package;\nmodel M\nend M;"));
    }

    [Fact]
    public void Strip_LeavesSourceWithNoClauseUnchanged()
    {
        var source = "model M\nend M;";

        Assert.Same(source, WithinClause.Strip(source));
    }

    [Fact]
    public void Strip_LeavesAnIdentifierThatStartsWithTheKeywordAlone()
    {
        var source = "withinTolerance = 1;\nmodel M\nend M;";

        Assert.Same(source, WithinClause.Strip(source));
    }

    [Fact]
    public void Strip_LeavesSourceAloneWhenTheClauseIsUnterminated()
    {
        // No semicolon means this is not a clause we can safely cut.
        var source = "within My.Package\nmodel M";

        Assert.Same(source, WithinClause.Strip(source));
    }

    [Fact]
    public void Strip_RemovesAClauseThatEndsTheSource()
    {
        Assert.Equal("", WithinClause.Strip("within My.Package;"));
    }

    [Fact]
    public void Strip_RemovesOnlyTheFirstClause()
    {
        // A second clause is a syntax error the parser now reports; Strip must not paper over it by
        // quietly removing both.
        Assert.Equal("within B;\nmodel M\nend M;", WithinClause.Strip("within A;\nwithin B;\nmodel M\nend M;"));
    }

    [Fact]
    public void Strip_LeavesEmptySourceUnchanged()
    {
        Assert.Equal("", WithinClause.Strip(""));
    }

    #endregion

    [Theory]
    [InlineData("model M\nend M;", "My.Package")]
    [InlineData("package P\nend P;", null)]
    [InlineData("withinTolerance = 1;", "My.Package")]
    public void EnsureThenStrip_ReturnsTheOriginalSource(string source, string? parent)
    {
        Assert.Equal(source, WithinClause.Strip(WithinClause.Ensure(source, parent)));
    }

    [Fact]
    public void EnsureIsIdempotent()
    {
        var once = WithinClause.Ensure("model M\nend M;", "My.Package");

        Assert.Same(once, WithinClause.Ensure(once, "My.Package"));
    }
}

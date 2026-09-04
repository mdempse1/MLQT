using ModelicaParser.Helpers;
using ModelicaParser;
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

    #region Set

    [Fact]
    public void Set_AddsTheClauseWhenSourceHasNone()
    {
        Assert.Equal("within My.Package;\nmodel M\nend M;", WithinClause.Set("model M\nend M;", "My.Package"));
    }

    [Fact]
    public void Set_ReplacesAClauseNamingADifferentPackage()
    {
        // Creating or moving a class: the destination decides the clause, so one that arrived with
        // the source would otherwise file the class under the wrong package.
        var result = WithinClause.Set("within Somewhere.Else;\nmodel M\nend M;", "My.Package");

        Assert.Equal("within My.Package;\nmodel M\nend M;", result);
    }

    [Fact]
    public void Set_DoesNotAppendASecondClause()
    {
        var result = WithinClause.Set("within My.Package;\nmodel M\nend M;", "My.Package");

        Assert.Equal("within My.Package;\nmodel M\nend M;", result);
        Assert.Equal(1, result.Split("within").Length - 1);
    }

    [Fact]
    public void Set_ReplacesABareClause()
    {
        Assert.Equal("within My.Package;\nmodel M\nend M;", WithinClause.Set("within;\nmodel M\nend M;", "My.Package"));
    }

    [Fact]
    public void Set_WritesABareClauseForATopLevelLibrary()
    {
        Assert.Equal("within;\npackage P\nend P;", WithinClause.Set("within Old.Parent;\npackage P\nend P;", null));
    }

    [Fact]
    public void Set_LeavesAnIdentifierThatStartsWithTheKeywordAlone()
    {
        var result = WithinClause.Set("model M\n  Real withinTolerance;\nend M;", "My.Package");

        Assert.Equal("within My.Package;\nmodel M\n  Real withinTolerance;\nend M;", result);
    }

    [Fact]
    public void Set_IsIdempotent()
    {
        var once = WithinClause.Set("model M\nend M;", "My.Package");

        Assert.Equal(once, WithinClause.Set(once, "My.Package"));
    }

    #endregion

    #region Has

    [Theory]
    [InlineData("within My.Package;\nmodel M\nend M;")]
    [InlineData("within;\npackage P\nend P;")]
    [InlineData("\n\nwithin My.Package;\nmodel M\nend M;")]
    [InlineData("   within My.Package;")]
    public void Has_IsTrueForAClause(string source) => Assert.True(WithinClause.Has(source));

    [Theory]
    [InlineData("model M\nend M;")]
    [InlineData("withinTolerance = 1;")]
    [InlineData("within_range = 1;")]
    [InlineData("")]
    public void Has_IsFalseWithoutAClause(string source) => Assert.False(WithinClause.Has(source));

    #endregion

    #region A clause behind a comment (B87)

    // A licence header above the within clause is ordinary Modelica and parses cleanly. Reading it as
    // "no clause" made Ensure add a second one, which does not parse — so the incremental formatter's
    // "leave a file we cannot parse alone" guard declined to write, and every file with a header
    // comment went silently unformatted.
    private const string HeaderThenClause =
        "// Copyright (c) 2026 Someone.\nwithin My.Package;\nmodel M\nend M;";

    [Fact]
    public void AClauseBehindALineCommentIsSeen()
        => Assert.True(WithinClause.Has(HeaderThenClause));

    [Fact]
    public void EnsureDoesNotAddASecondClauseBehindAComment()
        => Assert.Same(HeaderThenClause, WithinClause.Ensure(HeaderThenClause, "My.Package"));

    [Fact]
    public void TheOriginalAndTheEnsuredBothParse()
    {
        // The property that matters, stated as the parser sees it rather than as a string compare.
        var (_, before) = ModelicaParserHelper.ParseWithErrors(HeaderThenClause);
        var (_, after) = ModelicaParserHelper.ParseWithErrors(
            WithinClause.Ensure(HeaderThenClause, "My.Package"));

        Assert.Empty(before);
        Assert.Empty(after);
    }

    [Fact]
    public void StripRemovesAClauseBehindAComment()
    {
        var stripped = WithinClause.Strip(HeaderThenClause);

        Assert.DoesNotContain("within", stripped, StringComparison.Ordinal);
        Assert.StartsWith("// Copyright", stripped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/* block */\nwithin My.Package;\nmodel M\nend M;")]
    [InlineData("/* one */ /* two */ within My.Package;\nmodel M\nend M;")]
    [InlineData("\n  // spaced\n\n  within My.Package;\nmodel M\nend M;")]
    public void EveryShapeTheLexerIgnoresIsSkipped(string source)
        => Assert.True(WithinClause.Has(source));

    [Theory]
    [InlineData("// only a comment\nmodel M\nend M;")]
    [InlineData("/* unterminated\nwithin My.Package;")]
    [InlineData("// withinTolerance is not a clause\nwithinTolerance = 1;")]
    public void WhatIsNotAClauseIsStillNotOne(string source)
        => Assert.False(WithinClause.Has(source));

    [Fact]
    public void ACommentedOutClauseIsNotAClause()
    {
        // The line comment hides it, so the file genuinely has none and Ensure must supply one.
        const string source = "// within My.Package;\nmodel M\nend M;";

        Assert.False(WithinClause.Has(source));
        Assert.StartsWith("within My.Package;", WithinClause.Ensure(source, "My.Package"), StringComparison.Ordinal);
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

using System.Text.RegularExpressions;
using MLQT.Shared.Components;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="NamingStyleSelect.ParsePattern"/> — what the settings page accepts as a naming
/// exception.
///
/// <para>Worth pinning because a bad pattern does not fail here: it is stored, reloaded on every
/// check afterwards, and throws somewhere a long way from the page that accepted it.</para>
/// </summary>
public class NamingStyleSelectTests
{
    [Fact]
    public void AValidPattern_IsAccepted()
    {
        var (pattern, error) = NamingStyleSelect.ParsePattern("^[A-Z][a-zA-Z0-9]*$");

        Assert.Equal("^[A-Z][a-zA-Z0-9]*$", pattern);
        Assert.Null(error);
    }

    [Fact]
    public void SurroundingWhitespace_IsTrimmed()
    {
        // Pasting from a document brings a trailing newline with it, and " ^X$ " and "^X$" are the
        // same exception — stored separately they would both be listed and both be added again.
        var (pattern, _) = NamingStyleSelect.ParsePattern("  ^[A-Z]$\n");

        Assert.Equal("^[A-Z]$", pattern);
    }

    [Fact]
    public void AnInvalidRegex_IsRejectedWithAReason()
    {
        var (pattern, error) = NamingStyleSelect.ParsePattern("^[A-Z");

        Assert.Null(pattern);
        Assert.NotNull(error);
        Assert.StartsWith("Invalid regex:", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyBox_IsNeitherAcceptedNorAnError(string? input)
    {
        // Pressing add with nothing typed is not a mistake to complain about.
        var (pattern, error) = NamingStyleSelect.ParsePattern(input);

        Assert.Null(pattern);
        Assert.Null(error);
    }

    [Fact]
    public void ABracketWrappedPattern_IsUnwrapped()
    {
        // The settings page lists patterns wrapped in brackets, and users paste them back in that
        // form. Stored as typed, the pattern would match nothing and the exception would silently
        // never apply.
        var (pattern, error) = NamingStyleSelect.ParsePattern("[^[A-Z][a-zA-Z]*$]");

        Assert.Null(error);
        Assert.NotNull(pattern);
        Assert.DoesNotContain("$]", pattern);
    }

    [Fact]
    public void WhateverIsAccepted_Compiles()
    {
        // The property behind the whole method: anything it hands back is used to build a Regex
        // later, in a context with no user to tell.
        string[] inputs =
        [
            "^[A-Z][a-zA-Z0-9]*$", "  ^der$ ", "[^[A-Z][a-zA-Z]*$]", @"^\w+_\d+$", "a|b",
        ];

        foreach (var input in inputs)
        {
            var (pattern, error) = NamingStyleSelect.ParsePattern(input);
            Assert.Null(error);
            Assert.NotNull(pattern);
            _ = new Regex(pattern!);
        }
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[")]
    [InlineData("*")]
    [InlineData(@"(?<")]
    public void NothingThatFailsToCompile_IsAccepted(string input)
    {
        var (pattern, error) = NamingStyleSelect.ParsePattern(input);

        Assert.Null(pattern);
        Assert.NotNull(error);
    }
}

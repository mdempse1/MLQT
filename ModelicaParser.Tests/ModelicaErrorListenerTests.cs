using Antlr4.Runtime;
using Xunit;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

public class ModelicaErrorListenerTests
{
    [Fact]
    public void SyntaxError_WithRecognitionException_CollectsError()
    {
        // Parse invalid code to trigger real syntax error with RecognitionException
        var invalidCode = "model Test Real x end Test;"; // Missing semicolon

        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(invalidCode);

        Assert.NotNull(parseTree);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void SyntaxError_WithoutRecognitionException_CollectsError()
    {
        var listener = new ModelicaErrorListener();
        var token = new CommonToken(0, "invalid");

        listener.SyntaxError(Console.Out, null!, token, 10, 25, "Syntax error message", null!);

        Assert.Single(listener.Errors);
        Assert.Equal(10, listener.Errors[0].Line);
        Assert.Equal(25, listener.Errors[0].CharPosition);
        Assert.Equal("Syntax error message", listener.Errors[0].Message);
        Assert.Equal("invalid", listener.Errors[0].OffendingToken);
    }

    [Fact]
    public void SyntaxError_WithDifferentLineNumbers_ReportsCorrectLine()
    {
        var listener = new ModelicaErrorListener();
        var token = new CommonToken(0, "test");

        listener.SyntaxError(Console.Out, null!, token, 100, 50, "Error at line 100", null!);

        Assert.Single(listener.Errors);
        Assert.Equal(100, listener.Errors[0].Line);
        Assert.Equal(50, listener.Errors[0].CharPosition);
    }

    [Fact]
    public void Parse_InvalidSyntax_ProducesParseTree()
    {
        var invalidCode = @"
model InvalidModel
  Real x
  // Missing semicolon
end InvalidModel;";

        // Parse() silently handles errors — verify it still produces a parse tree
        var parseTree = ModelicaParserHelper.Parse(invalidCode);
        Assert.NotNull(parseTree);
    }

    [Fact]
    public void Parse_MultipleErrors_CollectsAllErrors()
    {
        var invalidCode = @"
model MultiErrorModel
  Real x
  Real y
  Real z
end MultiErrorModel;";

        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(invalidCode);

        Assert.NotNull(parseTree);
        Assert.True(errors.Count > 0);
    }

    [Fact]
    public void Parse_ValidCode_ProducesNoErrors()
    {
        var validCode = @"
model ValidModel
  Real x;
  Real y;
end ValidModel;";

        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(validCode);

        Assert.NotNull(parseTree);
        Assert.Empty(errors);
    }

    [Fact]
    public void SyntaxError_WithEmptyMessage_CollectsError()
    {
        var listener = new ModelicaErrorListener();
        var token = new CommonToken(0, "test");

        listener.SyntaxError(Console.Out, null!, token, 1, 1, "", null!);

        Assert.Single(listener.Errors);
        Assert.Equal(1, listener.Errors[0].Line);
        Assert.Equal(1, listener.Errors[0].CharPosition);
    }

    [Fact]
    public void SyntaxError_WithLineZero_ReportsLineZero()
    {
        var listener = new ModelicaErrorListener();
        var token = new CommonToken(0, "test");

        listener.SyntaxError(Console.Out, null!, token, 0, 0, "Error at start", null!);

        Assert.Single(listener.Errors);
        Assert.Equal(0, listener.Errors[0].Line);
        Assert.Equal(0, listener.Errors[0].CharPosition);
    }

    // --- Message quality ------------------------------------------------------------------------
    // An error the author cannot act on is barely better than no error at all, so these pin the
    // wording down rather than just asserting that something was reported.

    [Fact]
    public void UnterminatedString_IsNamedAsSuch_NotAsATokenRecognitionError()
    {
        // A missing closing quote in a Documentation(info=...) annotation — the string then runs to
        // the end of the file, and ANTLR's raw message is "token recognition error at: '<all of it>'".
        var code = "model M \"desc\"\n  annotation(Documentation(info=\"<html><p>hi</p>));\nend M;";

        var (_, errors) = ModelicaParserHelper.ParseWithErrors(code);

        var unterminated = Assert.Single(errors, e => e.Message.Contains("Unterminated string literal"));
        Assert.DoesNotContain("token recognition error", unterminated.Message);
        Assert.Contains("<html>", unterminated.Message);   // keeps an excerpt as evidence
    }

    [Fact]
    public void UnterminatedString_ExcerptIsTruncatedToOneShortLine()
    {
        var longText = new string('x', 500);
        var code = $"model M \"desc\"\n  annotation(Documentation(info=\"{longText}\n\nmore\n));\nend M;";

        var (_, errors) = ModelicaParserHelper.ParseWithErrors(code);

        var unterminated = Assert.Single(errors, e => e.Message.Contains("Unterminated string literal"));
        Assert.True(unterminated.Message.Length < 200, $"message not truncated: {unterminated.Message.Length} chars");
        Assert.DoesNotContain('\n', unterminated.Message);
    }

    [Fact]
    public void ParserError_UsesAntlrDiagnosis_NotTheExceptionTypeName()
    {
        // RecognitionException doesn't override Message, so preferring it yields
        // "Exception of type 'Antlr4.Runtime.InputMismatchException' was thrown." — useless.
        var code = "model M \"desc\"\n  annotation(Documentation(info=\"<html><p>hi</p>));\nend M;";

        var (_, errors) = ModelicaParserHelper.ParseWithErrors(code);

        Assert.DoesNotContain(errors, e => e.Message.Contains("Exception of type"));
        // ANTLR's diagnosis always names the input it choked on ("mismatched input '<EOF>' expecting …",
        // "no viable alternative at input …") — the exception's default message never does.
        Assert.Contains(errors, e => e.OffendingToken is not null && e.Message.Contains("input"));
    }

    [Theory]
    [InlineData("token recognition error at: '@@@'", "Unrecognised input: '@@@'")]
    [InlineData("some other lexer message", "some other lexer message")]
    [InlineData("", "")]
    public void DescribeLexerError_LeavesNonStringMessagesUsable(string input, string expected)
        => Assert.Equal(expected, ModelicaErrorListener.DescribeLexerError(input));
}

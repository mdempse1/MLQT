using ModelicaParser.SpellChecking;

namespace ModelicaParser.Tests.SpellChecking;

public class TextExtractorTests
{
    [Fact]
    public void StripHtml_RemovesTags()
    {
        var html = "<p>This is a <strong>test</strong> paragraph.</p>";
        var result = TextExtractor.StripHtml(html);
        Assert.Equal("This is a test paragraph.", result);
    }

    [Fact]
    public void StripHtml_DecodesEntities()
    {
        var html = "Temperature &gt; 100 &amp; pressure &lt; 200";
        var result = TextExtractor.StripHtml(html);
        Assert.Equal("Temperature > 100 & pressure < 200", result);
    }

    [Fact]
    public void StripHtml_RemovesCodeBlocks()
    {
        var html = "<p>See the example:</p><code>x := y + 1;</code><p>for details.</p>";
        var result = TextExtractor.StripHtml(html);
        Assert.Contains("See the example", result);
        Assert.Contains("for details", result);
        Assert.DoesNotContain("x := y", result);
    }

    [Fact]
    public void StripHtml_RemovesPreBlocks()
    {
        var html = "<p>Description</p><pre>model Test\n  Real x;\nend Test;</pre><p>End</p>";
        var result = TextExtractor.StripHtml(html);
        Assert.Contains("Description", result);
        Assert.Contains("End", result);
        Assert.DoesNotContain("Real x", result);
    }

    [Fact]
    public void StripHtml_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TextExtractor.StripHtml(""));
        Assert.Equal(string.Empty, TextExtractor.StripHtml(null!));
    }

    [Fact]
    public void TokenizeToWords_SplitsOnWhitespace()
    {
        var words = TextExtractor.TokenizeToWords("Hello world test").ToList();
        Assert.Equal(3, words.Count);
        Assert.Equal("Hello", words[0].word);
        Assert.Equal("world", words[1].word);
        Assert.Equal("test", words[2].word);
    }

    [Fact]
    public void TokenizeToWords_SplitsOnPunctuation()
    {
        var words = TextExtractor.TokenizeToWords("Hello, world! (test)").ToList();
        Assert.Equal(3, words.Count);
        Assert.Equal("Hello", words[0].word);
        Assert.Equal("world", words[1].word);
        Assert.Equal("test", words[2].word);
    }

    [Fact]
    public void TokenizeToWords_ReturnsCorrectOffsets()
    {
        var words = TextExtractor.TokenizeToWords("Hello world").ToList();
        Assert.Equal(0, words[0].charOffset);
        Assert.Equal(6, words[1].charOffset);
    }

    [Fact]
    public void TokenizeToWords_HandlesApostrophes()
    {
        var words = TextExtractor.TokenizeToWords("it's don't").ToList();
        Assert.Equal(2, words.Count);
        Assert.Equal("it's", words[0].word);
        Assert.Equal("don't", words[1].word);
    }

    [Fact]
    public void TokenizeToWords_StripsLeadingTrailingApostrophes()
    {
        var words = TextExtractor.TokenizeToWords("'quoted'").ToList();
        Assert.Single(words);
        Assert.Equal("quoted", words[0].word);
    }

    [Fact]
    public void TokenizeToWords_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(TextExtractor.TokenizeToWords(""));
        Assert.Empty(TextExtractor.TokenizeToWords(null!));
    }

    [Theory]
    [InlineData("myVariable", true)]       // camelCase
    [InlineData("TimeStep", true)]          // PascalCase with camelCase pattern (upper after lower)
    [InlineData("timeStep", true)]          // camelCase
    [InlineData("x", true)]                 // single char
    [InlineData("ABC", true)]               // ALL_CAPS
    [InlineData("SI", true)]                // ALL_CAPS (2 chars)
    [InlineData("abc123", true)]            // contains digit
    [InlineData("my.path", true)]           // contains dot
    [InlineData("my_var", true)]            // contains underscore
    [InlineData("equation", true)]          // Modelica keyword
    [InlineData("model", true)]             // Modelica keyword
    [InlineData("\u0394p", true)]           // decoded HTML entity (Δp)
    [InlineData("\u03B6", true)]            // decoded HTML entity (ζ)
    [InlineData("temperature", false)]      // normal English word
    [InlineData("pressure", false)]         // normal English word
    [InlineData("The", false)]              // normal word
    [InlineData("is", false)]               // normal word
    [InlineData("", true)]                  // empty
    public void ShouldSkipWord_CorrectlyClassifies(string word, bool expected)
    {
        Assert.Equal(expected, TextExtractor.ShouldSkipWord(word));
    }

    [Fact]
    public void TokenizeToWords_KeepsUnderscoresInWord()
    {
        var words = TextExtractor.TokenizeToWords("the transformationMatrixFrom_nxy value").ToList();
        Assert.Equal(3, words.Count);
        Assert.Equal("the", words[0].word);
        Assert.Equal("transformationMatrixFrom_nxy", words[1].word);
        Assert.Equal("value", words[2].word);
    }

    [Fact]
    public void TokenizeToWords_KeepsDigitsInWord()
    {
        var words = TextExtractor.TokenizeToWords("use step2 here").ToList();
        Assert.Equal(3, words.Count);
        Assert.Equal("use", words[0].word);
        Assert.Equal("step2", words[1].word);
        Assert.Equal("here", words[2].word);
    }

    [Fact]
    public void TokenizeToWords_StripsLeadingTrailingUnderscores()
    {
        var words = TextExtractor.TokenizeToWords("_private_").ToList();
        Assert.Single(words);
        Assert.Equal("private", words[0].word);
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("\"\"", "")]
    [InlineData("hello", "hello")]
    [InlineData("\"a\"", "a")]
    public void StripQuotes_RemovesSurroundingQuotes(string input, string expected)
    {
        Assert.Equal(expected, TextExtractor.StripQuotes(input));
    }

    [Theory]
    [InlineData("no newlines", 5, 0)]
    [InlineData("line1\nline2\nline3", 0, 0)]
    [InlineData("line1\nline2\nline3", 6, 1)]
    [InlineData("line1\nline2\nline3", 12, 2)]
    [InlineData("line1\nline2\nline3", 100, 2)]
    [InlineData("", 0, 0)]
    public void CountNewlinesBefore_ReturnsCorrectCount(string text, int offset, int expected)
    {
        Assert.Equal(expected, TextExtractor.CountNewlinesBefore(text, offset));
    }

    [Fact]
    public void StripHtmlPreservingNewlines_PreservesNewlines()
    {
        var html = "<html>\n<p>First line.</p>\n<p>Second line.</p>\n</html>";
        var result = TextExtractor.StripHtmlPreservingNewlines(html);
        Assert.Contains("\n", result);
        var lines = result.Split('\n');
        Assert.True(lines.Length >= 4, "Should have at least 4 lines");
    }

    [Fact]
    public void StripHtmlPreservingNewlines_RemovesTags()
    {
        var html = "<p>Hello <strong>world</strong></p>";
        var result = TextExtractor.StripHtmlPreservingNewlines(html);
        Assert.Contains("Hello", result);
        Assert.Contains("world", result);
        Assert.DoesNotContain("<p>", result);
        Assert.DoesNotContain("<strong>", result);
    }

    [Fact]
    public void StripHtmlPreservingNewlines_PreservesNewlinesInPreBlocks()
    {
        // A <pre> block spanning 3 lines should preserve those newlines
        var html = "<p>Before</p>\n<pre>line1\nline2\nline3</pre>\n<p>After</p>";
        var result = TextExtractor.StripHtmlPreservingNewlines(html);
        // Count total newlines - should be 4 (before pre, 2 inside pre, after pre)
        var newlineCount = result.Count(c => c == '\n');
        Assert.Equal(4, newlineCount);
        Assert.DoesNotContain("line1", result);
        Assert.Contains("Before", result);
        Assert.Contains("After", result);
    }

    [Fact]
    public void ShouldSkipWord_DecodedHtmlEntities_Skipped()
    {
        // Decoded HTML entities like &Delta; → Δ should be skipped
        Assert.True(TextExtractor.ShouldSkipWord("\u0394p"));    // Δp
        Assert.True(TextExtractor.ShouldSkipWord("\u03B6"));     // ζ
        Assert.True(TextExtractor.ShouldSkipWord("\u03C0"));     // π
        Assert.True(TextExtractor.ShouldSkipWord("\u03C1"));     // ρ
    }
}

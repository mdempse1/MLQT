using ModelicaParser.SpellChecking;

namespace ModelicaParser.Tests.SpellChecking;

/// <summary>
/// The desktop app reads the flagged word back out of a spelling finding's message — to underline
/// every occurrence in the code view, to offer corrections, and to record it in the repository's
/// accepted spellings. These tests keep the wording and the reader in step: a change to one that the
/// other cannot follow shows up here, rather than as a word that quietly stops being underlined.
/// </summary>
public class SpellingMessageTests
{
    [Theory]
    [InlineData("tyre", SpellingMessage.InDescription)]
    [InlineData("tyre", SpellingMessage.InDocumentationInfo)]
    [InlineData("tyre", SpellingMessage.InDocumentationRevisions)]
    [InlineData("Stodola's", SpellingMessage.InDocumentationInfo)]
    [InlineData("its'", SpellingMessage.InDescription)]
    public void TheWordSurvivesTheRoundTrip(string word, string where)
    {
        var message = SpellingMessage.For(word, where);

        Assert.True(SpellingMessage.Is(message));
        Assert.Equal(word, SpellingMessage.WordFrom(message));
    }

    [Fact]
    public void AMessageFromAnotherRule_IsNotReadAsASpelling()
    {
        Assert.False(SpellingMessage.Is("Class 'Foo' has no description"));
        Assert.Null(SpellingMessage.WordFrom("Class 'Foo' has no description"));
        Assert.False(SpellingMessage.Is(null));
        Assert.Null(SpellingMessage.WordFrom(null));
    }
}

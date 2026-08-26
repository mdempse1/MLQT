using ModelicaParser.SpellChecking;

namespace ModelicaParser.Tests.SpellChecking;

public class SpellCheckerTests
{
    private readonly SpellChecker _checker = SpellChecker.Create();

    [Theory]
    [InlineData("model")]
    [InlineData("temperature")]
    [InlineData("pressure")]
    [InlineData("controller")]
    [InlineData("equation")]
    public void IsCorrect_EnglishWord_ReturnsTrue(string word)
    {
        Assert.True(_checker.IsCorrect(word));
    }

    [Theory]
    [InlineData("tempurature")]
    [InlineData("modl")]
    [InlineData("contrller")]
    public void IsCorrect_MisspelledWord_ReturnsFalse(string word)
    {
        Assert.False(_checker.IsCorrect(word));
    }

    [Fact]
    public void IsCorrect_CustomWord_ReturnsTrue()
    {
        var checker = SpellChecker.Create(customWords: ["Claytex", "MVEM"]);
        Assert.True(checker.IsCorrect("Claytex"));
        Assert.True(checker.IsCorrect("MVEM"));
    }

    [Fact]
    public void IsCorrect_CaseInsensitive_CustomWords()
    {
        var checker = SpellChecker.Create(customWords: ["Claytex"]);
        Assert.True(checker.IsCorrect("claytex"));
        Assert.True(checker.IsCorrect("CLAYTEX"));
    }

    [Fact]
    public void AddCustomWord_BecomesCorrect()
    {
        var checker = SpellChecker.Create();
        Assert.False(checker.IsCorrect("xyzzyplugh"));
        checker.AddCustomWord("xyzzyplugh");
        Assert.True(checker.IsCorrect("xyzzyplugh"));
    }

    [Fact]
    public void IsCorrect_ModelicaTerm_ReturnsTrue()
    {
        // Built-in Modelica terms from modelica_terms.txt
        Assert.True(_checker.IsCorrect("Modelica"));
        Assert.True(_checker.IsCorrect("Dymola"));
        Assert.True(_checker.IsCorrect("OpenModelica"));
        Assert.True(_checker.IsCorrect("Jacobian"));
        Assert.True(_checker.IsCorrect("linearization"));
    }

    [Fact]
    public void IsCorrect_WithContextWords_AcceptsContextWord()
    {
        var contextWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mySpecialVar", "plenumPressure" };
        Assert.True(_checker.IsCorrect("mySpecialVar", contextWords));
        Assert.True(_checker.IsCorrect("plenumPressure", contextWords));
    }

    [Fact]
    public void IsCorrect_ContextWordsDoNotPersist()
    {
        var contextWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ephemeralWord" };
        Assert.True(_checker.IsCorrect("ephemeralWord", contextWords));
        // Without context words, the word should not be found
        Assert.False(_checker.IsCorrect("ephemeralWord"));
    }

    [Fact]
    public void IsCorrect_EmptyAndWhitespace_ReturnsTrue()
    {
        Assert.True(_checker.IsCorrect(""));
        Assert.True(_checker.IsCorrect("  "));
        Assert.True(_checker.IsCorrect(null!));
    }

    [Fact]
    public void CustomWords_ReturnsSnapshot()
    {
        var checker = SpellChecker.Create(customWords: ["Alpha", "Beta"]);
        var words = checker.CustomWords;
        // Should contain our custom words plus all modelica_terms
        Assert.Contains("Alpha", words, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Beta", words, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithNoLanguages_StillWorksWithCustomWords()
    {
        var checker = SpellChecker.Create(languageCodes: [], customWords: ["testword"]);
        Assert.True(checker.IsCorrect("testword"));
        Assert.False(checker.IsCorrect("temperature")); // No dictionaries loaded
    }

    [Fact]
    public void Create_WithInvalidLanguage_SkipsGracefully()
    {
        var checker = SpellChecker.Create(languageCodes: ["xx_XX", "en_US"]);
        // en_US should still work
        Assert.True(checker.IsCorrect("temperature"));
    }

    [Fact]
    public void IsCorrect_BritishSpelling_ReturnsTrue()
    {
        // en_GB dictionary should accept British spellings
        Assert.True(_checker.IsCorrect("colour"));
        Assert.True(_checker.IsCorrect("organisation"));
    }

    [Fact]
    public void IsCorrect_AmericanSpelling_ReturnsTrue()
    {
        Assert.True(_checker.IsCorrect("color"));
        Assert.True(_checker.IsCorrect("organization"));
    }

    [Fact]
    public void Suggest_MisspelledWord_ReturnsSuggestions()
    {
        var suggestions = _checker.Suggest("tempurature");
        Assert.NotEmpty(suggestions);
        Assert.Contains("temperature", suggestions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suggest_CorrectWord_MayReturnEmpty()
    {
        // Correct words typically return no suggestions or very few
        var suggestions = _checker.Suggest("temperature");
        // Just verify it doesn't throw
        Assert.NotNull(suggestions);
    }

    [Fact]
    public void Suggest_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(_checker.Suggest(""));
        Assert.Empty(_checker.Suggest(null!));
    }

    // ── DictionarySource record tests ──

    [Fact]
    public void DictionarySource_StoresProperties()
    {
        var source = new DictionarySource("/path/to/en.aff", "/path/to/en.dic");
        Assert.Equal("/path/to/en.aff", source.AffixFilePath);
        Assert.Equal("/path/to/en.dic", source.DictionaryFilePath);
    }

    [Fact]
    public void DictionarySource_ValueEquality()
    {
        var a = new DictionarySource("a.aff", "a.dic");
        var b = new DictionarySource("a.aff", "a.dic");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DictionarySource_NotEqual_DifferentPaths()
    {
        var a = new DictionarySource("a.aff", "a.dic");
        var b = new DictionarySource("b.aff", "b.dic");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DictionarySource_ToString_ContainsPaths()
    {
        var source = new DictionarySource("test.aff", "test.dic");
        var str = source.ToString();
        Assert.Contains("test.aff", str);
        Assert.Contains("test.dic", str);
    }

    // ── Additional SpellChecker coverage ──

    [Fact]
    public void Create_WithAdditionalDictionaries_NonExistentFiles_SkipsGracefully()
    {
        var additionalDicts = new[]
        {
            new DictionarySource("/nonexistent/path.aff", "/nonexistent/path.dic")
        };
        var checker = SpellChecker.Create(additionalDictionaries: additionalDicts);
        // Should still work with built-in dictionaries
        Assert.True(checker.IsCorrect("temperature"));
    }

    [Fact]
    public void Create_WithCustomWords_WhitespaceOnly_Filtered()
    {
        var checker = SpellChecker.Create(customWords: ["  ", "", "  valid  "]);
        // Whitespace-only words should be filtered, but "valid" (trimmed) should be accepted
        Assert.True(checker.IsCorrect("valid"));
    }

    [Fact]
    public void AddCustomWord_WhitespaceOnly_Ignored()
    {
        var checker = SpellChecker.Create();
        checker.AddCustomWord("  ");
        checker.AddCustomWord("");
        // Should not throw and should not add empty words
        Assert.True(checker.IsCorrect("temperature")); // Still works normally
    }

    [Fact]
    public void AddCustomWord_TrimsWhitespace()
    {
        var checker = SpellChecker.Create();
        checker.AddCustomWord("  xyzzyword  ");
        Assert.True(checker.IsCorrect("xyzzyword"));
    }

    [Fact]
    public void BundledLanguageCodes_ReturnsExpectedCodes()
    {
        var codes = SpellChecker.BundledLanguageCodes;
        Assert.Contains("en_US", codes);
        Assert.Contains("en_GB", codes);
        Assert.Equal(2, codes.Count);
    }

    [Fact]
    public void Suggest_ReturnsSortedResults()
    {
        var suggestions = _checker.Suggest("tempurature");
        // Should be sorted case-insensitively
        for (int i = 1; i < suggestions.Count; i++)
        {
            Assert.True(string.Compare(suggestions[i - 1], suggestions[i], StringComparison.OrdinalIgnoreCase) <= 0);
        }
    }

    [Fact]
    public void IsCorrect_NullContextWords_StillWorks()
    {
        Assert.True(_checker.IsCorrect("temperature", null));
        Assert.False(_checker.IsCorrect("xyzzynotaword", null));
    }

    [Fact]
    public void Suggest_WhitespaceInput_ReturnsEmpty()
    {
        Assert.Empty(_checker.Suggest("  "));
    }

    [Fact]
    public void IsCorrect_AcceptsThePossessiveOfAnAcceptedWord()
    {
        // A name a repository has accepted appears in prose as often in the possessive as not, and
        // no dictionary — Hunspell or hand-written — carries possessive forms. Reporting "Stodola's"
        // while "Stodola" beside it is fine reads as the word list being ignored.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Stodola"]);

        Assert.True(checker.IsCorrect("Stodola"));
        Assert.True(checker.IsCorrect("Stodola's"));
    }

    [Fact]
    public void IsCorrect_AcceptsThePossessiveOfADictionaryWord()
    {
        var checker = SpellChecker.Create(["en_US"]);

        Assert.True(checker.IsCorrect("engine's"));
    }

    [Fact]
    public void IsCorrect_AcceptsAPossessiveWrittenWithATypographicApostrophe()
    {
        // Documentation is HTML prose and carries either apostrophe.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Stodola"]);

        Assert.True(checker.IsCorrect("Stodola\u2019s"));
    }

    [Fact]
    public void IsCorrect_DoesNotAcceptThePossessiveOfAMisspelling()
    {
        // Accepting possessives must not become a way for any word ending in 's to pass.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Stodola"]);

        Assert.False(checker.IsCorrect("Stodolla's"));
        Assert.False(checker.IsCorrect("qwertys"));
    }

    [Fact]
    public void AcceptedWords_IgnoreCase()
    {
        // The repository's word list is matched case-insensitively, so a term does not have to be
        // listed once per casing it appears in. (The language dictionaries follow Hunspell's own
        // rules instead: a lowercase entry covers "engine", "Engine" and "ENGINE", while a proper
        // noun listed capitalised is not matched in lowercase.)
        var checker = SpellChecker.Create(["en_US"], customWords: ["Stodola"]);

        Assert.True(checker.IsCorrect("stodola"));
        Assert.True(checker.IsCorrect("STODOLA"));
        Assert.True(checker.IsCorrect("StOdOlA"));
        Assert.True(checker.IsCorrect("stodola's"));
    }

    [Theory]
    [InlineData("Stodola's", "Stodola")]
    [InlineData("Stodola\u2019s", "Stodola")]
    [InlineData("engine's", "engine")]
    [InlineData("Stodola", null)]
    [InlineData("its", null)]
    [InlineData("'s", null)]
    public void PossessiveBaseOf_NamesTheWordAPossessiveBelongsTo(string word, string? expected)
    {
        // Anything recording an accepted word uses this, so the list gets "Stodola" whichever form
        // the user happened to click on.
        Assert.Equal(expected, SpellChecker.PossessiveBaseOf(word));
    }

    [Fact]
    public void Suggest_OffersAnAcceptedWordThatIsNearlyRight()
    {
        // The case that prompted this: a repository accepts "Pacejka", someone types "Pacjeka", and
        // no English dictionary has anything to say about either. The word the team accepted was one
        // transposition away and was not being offered.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Pacejka"]);

        var suggestions = checker.Suggest("Pacjeka");

        Assert.Contains("Pacejka", suggestions);
    }

    [Fact]
    public void Suggest_PutsAcceptedWordsFirst()
    {
        // They are the likelier intent: someone took the trouble to accept that exact term here.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Pacejka"]);

        var suggestions = checker.Suggest("Pacjeka");

        Assert.Equal("Pacejka", suggestions[0]);
    }

    [Fact]
    public void Suggest_OffersAnAcceptedWordInItsAcceptedCasing()
    {
        var checker = SpellChecker.Create(["en_US"], customWords: ["VeSyMA"]);

        Assert.Contains("VeSyMA", checker.Suggest("VeSyMa1"));
    }

    [Fact]
    public void Suggest_DoesNotOfferAcceptedWordsThatAreNothingLikeIt()
    {
        // A repository's vocabulary must not start answering for unrelated words.
        var checker = SpellChecker.Create(["en_US"], customWords: ["Pacejka", "enthalpy", "SOC"]);

        var suggestions = checker.Suggest("qwertyuiop");

        Assert.DoesNotContain("Pacejka", suggestions);
        Assert.DoesNotContain("enthalpy", suggestions);
    }

    [Fact]
    public void Suggest_DoesNotOfferTheWordItself()
    {
        var checker = SpellChecker.Create(["en_US"], customWords: ["Pacejka"]);

        Assert.DoesNotContain("Pacejka", checker.Suggest("pacejka"));
    }

    [Fact]
    public void Suggest_StillOffersTheDictionarysOwnSuggestions()
    {
        var checker = SpellChecker.Create(["en_US"], customWords: ["Pacejka"]);

        var suggestions = checker.Suggest("tempurature");

        Assert.Contains(suggestions, s => s.Equals("temperature", StringComparison.OrdinalIgnoreCase));
    }
}

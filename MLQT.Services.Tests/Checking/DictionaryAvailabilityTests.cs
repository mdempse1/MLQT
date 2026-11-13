using MLQT.Services.Checking;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// Which dictionary a language names, and what is said when one names none (B529). Sessions wrote
/// en-GB and en_UK into a repository's settings; the checker loaded nothing for either and reported
/// every word, and the warning said no dictionary was installed, which read as a broken installer.
/// The checker and the warning now both ask <see cref="DictionaryAvailability.Resolve"/>.
/// </summary>
public class DictionaryAvailabilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mlqt-dict-" + Guid.NewGuid().ToString("N"));
    private readonly DictionaryManagerService _manager;

    public DictionaryAvailabilityTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "de_DE.aff"), "SET UTF-8");
        File.WriteAllText(Path.Combine(_dir, "de_DE.dic"), "1\nHaus");
        _manager = new DictionaryManagerService(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData("en_GB", "en_GB")]
    [InlineData("en-GB", "en_GB")]
    [InlineData("en_gb", "en_GB")]
    [InlineData("en_UK", "en_GB")]
    [InlineData("EN-US", "en_US")]
    [InlineData("de-de", "de_DE")]   // an imported dictionary, matched whatever the case
    public void ALanguage_ResolvesToTheDictionaryItNames(string asked, string code)
    {
        Assert.Equal(code, DictionaryAvailability.Resolve(asked, _manager));
    }

    [Theory]
    [InlineData("British")]
    [InlineData("fr_FR")]
    [InlineData("")]
    [InlineData("  ")]
    public void ALanguageWithNoDictionary_ResolvesToNothing(string asked)
    {
        Assert.Null(DictionaryAvailability.Resolve(asked, _manager));
    }

    [Fact]
    public void ALanguageThatResolves_IsNotMissing()
    {
        // en_gb passed the old, case-insensitive availability check with no warning, and then the
        // case-sensitive loader loaded nothing for it: the one silent way to lose every dictionary.
        Assert.Empty(DictionaryAvailability.MissingLanguages(["en_gb", "en-US", "de-DE"], _manager));
        Assert.Null(DictionaryAvailability.WarningFor(["en_gb"], _manager));
    }

    [Fact]
    public void TheWarning_NamesWhatIsNotMatched_AndWhatThereIs()
    {
        var warning = DictionaryAvailability.WarningFor(["British"], _manager);

        Assert.NotNull(warning);
        Assert.Contains("no spell-check dictionary matches British", warning);
        Assert.Contains("every word is reported as misspelled", warning);
        Assert.Contains("the dictionaries here are de_DE, en_GB, en_US", warning);
    }

    [Fact]
    public void TheWarning_ForOneOfSeveral_SaysTheRestStillJudge()
    {
        var warning = DictionaryAvailability.WarningFor(["en_GB", "fr_FR"], _manager);

        Assert.NotNull(warning);
        Assert.Contains("matches fr_FR; those words are checked against the remaining languages", warning);
    }

    [Fact]
    public void TheFactory_LoadsTheDictionaryTheWarningSaysIsThere()
    {
        var checker = SpellCheckerFactory.Build(["en-GB"], [], _manager);

        Assert.True(checker.IsCorrect("behaviour"));
        Assert.False(checker.IsCorrect("postion"));
    }

    [Fact]
    public void TheFactory_FindsAnImportedDictionary_WhateverItsCase()
    {
        var checker = SpellCheckerFactory.Build(["de-de"], [], _manager);

        Assert.True(checker.IsCorrect("Haus"));
    }
}

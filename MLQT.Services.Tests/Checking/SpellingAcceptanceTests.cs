using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests.Checking;

/// <summary>
/// What accepting a spelling covers (backlog B119).
/// </summary>
/// <remarks>
/// The page offers two waivers of different width — accept the word for the repository, or for this
/// class only — and they share this rule. Scope is the feature: the accepted-words list is committed
/// with the code, so a term accepted in one library must not silence the same word in another team's.
/// </remarks>
public class SpellingAcceptanceTests
{
    private static LogMessage Spelling(string model, string word, string where = "the description") =>
        new(model, "warning", 1, SpellingMessage.For(word, where))
        {
            RuleId = "MLQT.Spelling.Description",
            Discriminator = $"{where}:{word}",
        };

    private static LogMessage Other(string model) =>
        new(model, "warning", 1, "The class is missing a description string") { RuleId = "MLQT.Doc.ClassDescription" };

    private static IReadOnlySet<string> Scope(params string[] models) =>
        new HashSet<string>(models, StringComparer.Ordinal);

    [Theory]
    [InlineData("Stodola", "Stodola")]
    [InlineData("Stodola's", "Stodola")]        // the checker derives the possessive already
    [InlineData("Jones’s", "Jones")]       // typographic apostrophe, as documentation prose carries
    [InlineData("isentropic", "isentropic")]
    public void ThePossessiveIsRecordedAsTheWordItself(string clicked, string recorded)
    {
        Assert.Equal(recorded, SpellingAcceptance.WordToRecord(clicked));
    }

    [Theory]
    [InlineData("Pacejka", "Pacejka", true)]
    [InlineData("Pacejka", "Pacejka's", true)]   // the possessive of an accepted word
    [InlineData("Pacejka", "pacejka", true)]     // the word list ignores case, so this must too
    [InlineData("Pacejka", "PACEJKA", true)]
    [InlineData("Pacejka", "Pacejkas", false)]   // a plural is a different word, not a possessive
    [InlineData("Pacejka", "Pacjeka", false)]    // the transposition somebody accepted the fix for
    [InlineData("Pacejka", null, false)]
    public void AnAcceptedWordCoversItselfAndItsPossessive(string accepted, string? flagged, bool covered)
    {
        Assert.Equal(covered, SpellingAcceptance.Covers(accepted, flagged));
    }

    [Fact]
    public void TheWordIsReadFromTheMessage_NotTheDiscriminator()
    {
        // The discriminator exists to make a fingerprint unique, and for a documentation finding it
        // carries the section as well as the word ("documentation info:tyre"). Reading it here
        // underlined nothing, because no such text appears in the source.
        var finding = Spelling("Lib.Model", "tyre", where: "documentation info");

        Assert.Equal("tyre", SpellingAcceptance.WordOf(finding));
        Assert.NotEqual(finding.Discriminator, SpellingAcceptance.WordOf(finding));
    }

    [Fact]
    public void OnlySpellingFindingsAreSpellingFindings()
    {
        Assert.True(SpellingAcceptance.IsSpellingFinding(Spelling("Lib.Model", "tyre")));
        Assert.False(SpellingAcceptance.IsSpellingFinding(Other("Lib.Model")));
        Assert.False(SpellingAcceptance.IsSpellingFinding(null));
        Assert.Null(SpellingAcceptance.WordOf(Other("Lib.Model")));
    }

    [Fact]
    public void AcceptingForARepositoryClearsTheWordOnlyInThatRepositorysClasses()
    {
        // The assertion the whole feature rests on. Clearing it everywhere would silence a word in
        // another team's library, which has not agreed to it - and the next check reports it again,
        // long after anybody would connect the two.
        var cleared = SpellingAcceptance.ClearedBy("tyre", Scope("Ours.A", "Ours.B"));

        Assert.True(cleared(Spelling("Ours.A", "tyre")));
        Assert.True(cleared(Spelling("Ours.B", "tyre's")));
        Assert.False(cleared(Spelling("Theirs.C", "tyre")));
    }

    [Fact]
    public void ItClearsNeitherAnotherWordNorAnotherKindOfFinding()
    {
        var cleared = SpellingAcceptance.ClearedBy("tyre", Scope("Ours.A"));

        Assert.False(cleared(Spelling("Ours.A", "wheeel")));
        Assert.False(cleared(Other("Ours.A")));
    }

    [Fact]
    public void AcceptingInOneClassLeavesTheWordFlaggedInEveryOther()
    {
        // The narrower waiver, written into the class's own source. It is the middle option between
        // suppressing the rule for the class and accepting the word repository-wide, and it would be
        // neither if it reached further than the class.
        var cleared = SpellingAcceptance.ClearedInClass("tyre", "Lib.Model");

        Assert.True(cleared(Spelling("Lib.Model", "tyre")));
        Assert.True(cleared(Spelling("Lib.Model", "Tyre")));
        Assert.False(cleared(Spelling("Lib.Other", "tyre")));
        Assert.False(cleared(Spelling("Lib.Model.Nested", "tyre")));
    }
}

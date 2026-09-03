using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>__MLQT(spelling="…")</c>: a word accepted in one class, which is what the Code Review menu's
/// Ignore records. Scoped to the word — everything else in the class is still spell checked — and to
/// the class, so a sibling still reports it.
/// </summary>
public class SpellingSuppressionTests
{
    private static StyleCheckingSettings SpellRules => new()
    {
        SpellCheckDescription = true,
        SpellCheckDocumentation = true,
    };

    private static List<Finding> Check(string code, bool honor = true)
        => StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", code), SpellRules, "TestModel",
            spellChecker: SpellChecker.Create(), honorSuppressions: honor);

    [Fact]
    public void AcceptedWord_IsNotReportedInThatClass()
    {
        var code = """
            model TestModel "Uses the wibbler"
              annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void AcceptedWord_CoversDocumentationAsWellAsDescriptions()
    {
        var code = """
            model TestModel "Uses the wibbler"
              annotation(
                __MLQT(spelling="wibbler"),
                Documentation(info="<html><p>The wibbler is driven directly.</p></html>"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void AcceptedWord_CoversItsPossessive()
    {
        var code = """
            model TestModel "Follows the wibbler's position"
              annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void AcceptedWord_IgnoresCase()
    {
        var code = """
            model TestModel "Uses the Wibbler"
              annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void OtherMisspellings_AreStillReported()
    {
        // The point of accepting a word rather than suppressing the rule: the rest of the class is
        // still checked.
        var code = """
            model TestModel "Uses the wibbler and the frimbo"
              annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        var findings = Check(code);

        Assert.Single(findings);
        Assert.Equal("frimbo", findings[0].Discriminator);
    }

    [Fact]
    public void SeveralAcceptedWords_AreAllCovered()
    {
        var code = """
            model TestModel "Uses the wibbler and the frimbo"
              annotation(__MLQT(spelling="wibbler,frimbo"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void AcceptedWord_DoesNotCoverASiblingClass()
    {
        // The waiver belongs to the class it is written in; anything else would make it a quieter
        // version of the repository word list.
        var code = """
            package Lib
              model Accepting "Uses the wibbler"
                annotation(__MLQT(spelling="wibbler"));
              end Accepting;
            end Lib;
            """;

        var sibling = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("Other", "model Other \"Uses the wibbler\" end Other;"),
            SpellRules, "Lib.Other", spellChecker: SpellChecker.Create());

        Assert.Empty(Check(code));                                  // the class that accepted it
        Assert.Equal("wibbler", Assert.Single(sibling).Discriminator);
    }

    [Fact]
    public void NoSuppress_ReportsTheAcceptedWordAgain()
    {
        // What `mlqt check --no-suppress` audits with.
        var code = """
            model TestModel "Uses the wibbler"
              annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        Assert.Equal("wibbler", Assert.Single(Check(code, honor: false)).Discriminator);
    }

    [Fact]
    public void SpellingOnAComponent_AppliesToTheClass()
    {
        // A spelling finding names no element, so a component-scoped word list could never match one.
        // Reading it as the class's keeps the annotation from silently doing nothing.
        var code = """
            model TestModel "Uses the wibbler"
              Real x "Offset from the wibbler" annotation(__MLQT(spelling="wibbler"));
            end TestModel;
            """;

        Assert.Empty(Check(code));
    }

    [Fact]
    public void WriterOutput_IsHonouredByTheChecker()
    {
        // The annotation the app writes and the one the checker reads are the same shape — the pair
        // that has to agree for Ignore to hold through the next check.
        const string original = """
            model TestModel "Uses the wibbler"
            end TestModel;
            """;

        Assert.True(MlqtSuppressionWriter.TryAddSpellingException(
            original, "wibbler", reason: null, out var annotated, out var error));
        Assert.Null(error);

        Assert.Empty(Check(annotated));
    }
}

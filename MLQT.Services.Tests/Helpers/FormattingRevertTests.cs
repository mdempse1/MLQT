using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using RevisionControl;
using Xunit;

namespace MLQT.Services.Tests.Helpers;

/// <summary>
/// B302 — <b>Exclude from auto-formatting</b> reverted the class's file whenever it had changed, and a
/// revert restores the committed file whatever made it differ: it deleted a file that had never been
/// committed and threw away hand edits to every other class in a modified one. These hold the one
/// question the button now asks first — would reverting take back formatting and nothing else?
/// </summary>
public class FormattingRevertTests
{
    /// <summary>A committed file the formatter would change: extra spaces and an odd indent.</summary>
    private static readonly string Committed = ModelicaParserHelper.NormalizeLineEndings("""
        within Lib;
        package P "two classes in one file"
          model A "first"
            parameter Real    k =  1   "loosely spaced";
          end A;

          model B "second"
                Real x;
          end B;
        end P;
        """);

    private static readonly StyleCheckingSettings Formatting = new() { ApplyFormattingRules = true };

    /// <summary>What the incremental formatter writes over <paramref name="source"/>.</summary>
    private static string Formatted(string source) =>
        ModelicaFileEncoding.EnsureFinalNewline(
            ModelicaPackageSaver.RenderFileSource(source, "Lib", Formatting.ToFormattingOptions()));

    private static FormattingRevert.Decision Decide(
        VcsFileStatus? status, string? committed, string workingCopy, StyleCheckingSettings? settings = null) =>
        FormattingRevert.Decide(status, committed, workingCopy, "Lib", settings ?? Formatting, "Lib.P");

    [Fact]
    public void TheFixtureIsOneTheFormatterChanges()
    {
        // Otherwise every "only formatting" case below is also an "unchanged" case, and could not
        // tell a working copy the formatter wrote from one nobody touched.
        Assert.NotEqual(Committed, ModelicaParserHelper.NormalizeLineEndings(Formatted(Committed)));
    }

    [Fact]
    public void AFileTheFormatterRewroteAndNobodyElseTouchedIsReverted()
    {
        Assert.True(Decide(VcsFileStatus.Modified, Committed, Formatted(Committed)).Revert);
    }

    [Fact]
    public void AnUntrackedFileIsNeverReverted()
    {
        // Git and SVN both revert a file the repository has never seen by deleting it.
        var decision = Decide(VcsFileStatus.Untracked, null, Formatted(Committed));

        Assert.False(decision.Revert);
        Assert.Contains("delete", decision.Reason);
    }

    [Fact]
    public void AnAddedFileIsNeverReverted()
    {
        // Added is as new as untracked: there is no committed version, and a revert deletes it. Even
        // when the text happens to match what formatting some committed text would give.
        var decision = Decide(VcsFileStatus.Added, Committed, Formatted(Committed));

        Assert.False(decision.Revert);
        Assert.Contains("delete", decision.Reason);
    }

    [Theory]
    [InlineData(VcsFileStatus.Deleted)]
    [InlineData(VcsFileStatus.Renamed)]
    [InlineData(VcsFileStatus.Conflicted)]
    [InlineData(null)]
    public void NoOtherStatusIsReverted(VcsFileStatus? status)
    {
        Assert.False(Decide(status, Committed, Formatted(Committed)).Revert);
    }

    [Fact]
    public void AnEditToAnotherClassInTheFileIsNotDiscarded()
    {
        // The package.mo case: the user edited B by hand, the formatter then ran over the file. The
        // revert would have taken B's edit with it.
        var edited = Committed.Replace("Real x;", "Real x \"now documented\";");
        var workingCopy = Formatted(edited);

        var decision = Decide(VcsFileStatus.Modified, Committed, workingCopy);

        Assert.False(decision.Revert);
        Assert.Contains("other uncommitted changes", decision.Reason);
    }

    [Fact]
    public void AnEditTheFormatterHasNotSeenIsNotDiscarded()
    {
        var decision = Decide(VcsFileStatus.Modified, Committed, Committed.Replace("k =  1", "k =  2"));

        Assert.False(decision.Revert);
    }

    [Fact]
    public void WithFormattingSwitchedOffNothingWasFormatted()
    {
        // MLQT never touched this file, so every difference in it is the user's.
        var off = new StyleCheckingSettings { ApplyFormattingRules = false };

        Assert.False(Decide(VcsFileStatus.Modified, Committed, Formatted(Committed), off).Revert);
    }

    [Fact]
    public void LineEndingsAreNotAChange()
    {
        // The write keeps the file's own endings (B251), so a CRLF checkout's formatted file is CRLF.
        var crlf = Formatted(Committed).Replace("\n", "\r\n");

        Assert.True(Decide(VcsFileStatus.Modified, Committed.Replace("\n", "\r\n"), crlf).Revert);
    }

    [Fact]
    public void AFileThatDiffersOnlyInLineEndingsCanBeReverted()
    {
        Assert.True(Decide(VcsFileStatus.Modified, Committed, Committed.Replace("\n", "\r\n")).Revert);
    }

    [Fact]
    public void WithNoCommittedTextThereIsNothingToCompare()
    {
        Assert.False(Decide(VcsFileStatus.Modified, null, Formatted(Committed)).Revert);
    }

    [Fact]
    public void ACommittedVersionThatDoesNotParseIsNotReverted()
    {
        // The formatter leaves a file it cannot parse alone, so whatever changed in it was not the
        // formatter.
        const string broken = "within Lib;\npackage P \"unterminated\n  model A\n  end A;\nend P;\n";

        Assert.False(Decide(VcsFileStatus.Modified, broken, "within Lib;\npackage P\nend P;\n").Revert);
    }
}

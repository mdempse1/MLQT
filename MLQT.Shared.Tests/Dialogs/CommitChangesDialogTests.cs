using MLQT.Shared.Dialogs;
using ModelicaGraph;
using Xunit;

namespace MLQT.Shared.Tests.Dialogs;

/// <summary>
/// The commit-message policy: whether a commit is allowed, and what message it actually writes.
///
/// <para>Both halves live only in <see cref="CommitChangesDialog"/> — no other surface in the
/// solution implements them — so if enforcement stopped, nothing would notice. The settings behind
/// them are ones a user switched on expecting them to hold.</para>
/// </summary>
public class CommitChangesDialogTests
{
    private static StyleCheckingSettings RequiresIssue(bool atEnd = false) => new()
    {
        CommitRequiresIssueNumber = true,
        IssueNumberAtEnd = atEnd,
    };

    private static StyleCheckingSettings NoPolicy() => new();

    [Fact]
    public void CanCommit_WithFilesAndAMessage_IsAllowed()
    {
        Assert.True(CommitChangesDialog.CanCommitWith(1, "Fix the thing", "", NoPolicy()));
    }

    [Fact]
    public void CanCommit_WithNothingSelected_IsRefused()
    {
        Assert.False(CommitChangesDialog.CanCommitWith(0, "Fix the thing", "", NoPolicy()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void CanCommit_WithNoRealMessage_IsRefused(string? message)
    {
        // Whitespace counts as empty: a commit message of three spaces is not a commit message.
        Assert.False(CommitChangesDialog.CanCommitWith(1, message, "", NoPolicy()));
    }

    [Fact]
    public void CanCommit_WhenAnIssueNumberIsRequiredAndMissing_IsRefused()
    {
        Assert.False(CommitChangesDialog.CanCommitWith(1, "Fix the thing", "", RequiresIssue()));
    }

    [Fact]
    public void CanCommit_WhenAnIssueNumberIsRequiredAndGiven_IsAllowed()
    {
        Assert.True(CommitChangesDialog.CanCommitWith(1, "Fix the thing", "MLQT-42", RequiresIssue()));
    }

    [Fact]
    public void CanCommit_WithNoSettingsAtAll_DoesNotRequireAnIssueNumber()
    {
        // A repository whose settings have never been saved must still be committable. The policy is
        // opt-in, so absent settings mean no policy rather than an unsatisfiable one.
        Assert.True(CommitChangesDialog.CanCommitWith(1, "Fix the thing", "", settings: null));
    }

    [Fact]
    public void ComposeCommitMessage_WithNoPolicy_LeavesTheMessageAlone()
    {
        var message = CommitChangesDialog.ComposeCommitMessage("Fix the thing", "MLQT-42", NoPolicy());

        Assert.Equal("Fix the thing", message);
    }

    [Fact]
    public void ComposeCommitMessage_WithTheIssueNumberFirst_PutsItOnTheLineAbove()
    {
        var message = CommitChangesDialog.ComposeCommitMessage(
            "Fix the thing", "MLQT-42", RequiresIssue(atEnd: false));

        Assert.Equal("MLQT-42\nFix the thing", message);
    }

    [Fact]
    public void ComposeCommitMessage_WithTheIssueNumberAtTheEnd_PutsItOnTheLineBelow()
    {
        var message = CommitChangesDialog.ComposeCommitMessage(
            "Fix the thing", "MLQT-42", RequiresIssue(atEnd: true));

        Assert.Equal("Fix the thing\nMLQT-42", message);
    }

    [Fact]
    public void ComposeCommitMessage_WithAMultiLineMessage_KeepsTheIssueNumberOnItsOwnLine()
    {
        // The reason the separator is a newline and not a space: a commit message with a body must
        // still start (or end) with a line that is only the issue number, which is what the hooks
        // and trackers reading these messages match on.
        var message = CommitChangesDialog.ComposeCommitMessage(
            "Fix the thing\n\nLonger explanation.", "MLQT-42", RequiresIssue(atEnd: false));

        Assert.Equal("MLQT-42", message.Split('\n')[0]);
    }

    [Fact]
    public void ComposeCommitMessage_WithNoSettings_LeavesTheMessageAlone()
    {
        var message = CommitChangesDialog.ComposeCommitMessage("Fix the thing", "MLQT-42", settings: null);

        Assert.Equal("Fix the thing", message);
    }
}

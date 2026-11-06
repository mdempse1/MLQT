using MLQT.Shared.Helpers;

namespace MLQT.Shared.Tests.Helpers;

/// <summary>
/// <see cref="PreCommitFormat"/>: which changed files the format before a commit may skip, and what
/// its progress says. Skipping rests on the write time alone, so an edit made anywhere - another
/// editor, an svn revert, while the monitor was paused - is formatted again.
/// </summary>
public class PreCommitFormatTests
{
    private static readonly DateTime Formatted = new(2026, 11, 5, 15, 12, 26, DateTimeKind.Utc);

    private static readonly Dictionary<string, DateTime> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a.mo"] = Formatted,
        ["b.mo"] = Formatted,
    };

    [Fact]
    public void AFileStillAtItsRecordedWriteTime_IsSkipped() =>
        Assert.Equal(["a.mo"], PreCommitFormat.AlreadyFormatted(["a.mo"], Known, _ => Formatted));

    [Fact]
    public void AFileWrittenSince_IsFormattedAgain() =>
        Assert.Empty(PreCommitFormat.AlreadyFormatted(["a.mo"], Known, _ => Formatted.AddMilliseconds(1)));

    [Fact]
    public void AFileWithAnOlderWriteTime_IsFormattedAgain() =>
        // An svn revert with use-commit-times sets the committed time, which is older.
        Assert.Empty(PreCommitFormat.AlreadyFormatted(["a.mo"], Known, _ => Formatted.AddDays(-3)));

    [Fact]
    public void AFileNeverRecorded_IsFormatted() =>
        Assert.Empty(PreCommitFormat.AlreadyFormatted(["c.mo"], Known, _ => Formatted));

    [Fact]
    public void AFileThatCannotBeRead_IsNotSkipped() =>
        Assert.Empty(PreCommitFormat.AlreadyFormatted(["a.mo"], Known, _ => throw new IOException("locked")));

    [Fact]
    public void OnlyTheUnchangedOnesAreSkipped() =>
        Assert.Equal(["b.mo"], PreCommitFormat.AlreadyFormatted(
            ["a.mo", "b.mo", "c.mo"], Known, f => f == "a.mo" ? Formatted.AddSeconds(5) : Formatted));

    [Fact]
    public void AFileWrittenSince_IsSeen_WhateverTheClockSays()
    {
        // The time a filesystem stamps can be earlier than the system clock read before the write;
        // comparing with the file's own earlier time is what makes that not matter.
        var before = PreCommitFormat.WriteTimes(["a.mo", "b.mo"], _ => Formatted);

        Assert.True(PreCommitFormat.AnyRewritten(before, f => f == "b.mo" ? Formatted.AddMilliseconds(-3) : Formatted));
    }

    [Fact]
    public void NoFileWrittenSince_NothingRewritten()
    {
        var before = PreCommitFormat.WriteTimes(["a.mo", "b.mo"], _ => Formatted);

        Assert.False(PreCommitFormat.AnyRewritten(before, _ => Formatted));
    }

    [Theory]
    [InlineData(1, "Applying the formatting rules to 1 changed file")]
    [InlineData(2, "Applying the formatting rules to 2 changed files")]
    [InlineData(17482, "Applying the formatting rules to 17,482 changed files")]
    public void TheProgressSaysHowManyFiles(int files, string expected) =>
        Assert.Equal(expected, PreCommitFormat.FormattingMessage(files));
}

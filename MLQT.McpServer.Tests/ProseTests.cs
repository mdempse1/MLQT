using System.Text.RegularExpressions;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// The guidance and instructions are hard-wrapped to fit the source file, and were sent that way: a
/// sentence reached the agent as <c>text\r\nreader</c>, with the CRLF of a Windows checkout in it.
/// </summary>
public class ProseTests
{
    [Fact]
    public void WrappedLinesOfAParagraphAreJoinedWithASpace()
    {
        Assert.Equal("Reading a .mo file with a text reader gives you raw text.",
            Prose.Unwrap("Reading a .mo file with a text\r\nreader gives you raw text."));
    }

    [Fact]
    public void ParagraphsStaySeparatedByOneBlankLine()
    {
        Assert.Equal("One para.\n\nTwo para.",
            Prose.Unwrap("\nOne\npara.\n\n\n   \nTwo\npara.\n"));
    }

    [Fact]
    public void AListItemStartsALineOfItsOwnAndKeepsItsIndentation()
    {
        var text = """
            Who does what:
            - MLQT owns the
              source.
              * nested
                item.
            The loop:
              1. Read and
                 edit.
              2. Reload.
            """;

        Assert.Equal(
            "Who does what:\n- MLQT owns the source.\n  * nested item.\n\nThe loop:\n  1. Read and edit.\n  2. Reload.",
            Prose.Unwrap(text));
    }

    [Fact]
    public void ALineNoFurtherInThanTheMarkerEndsTheList()
    {
        // The source closes a list with a sentence written at the margin, and that sentence is not part
        // of the last item. Joined to it, "Do not author code through the simulator" read as step 4.
        var text = """
            The loop:
              1. Check there.
              2. Fix with MLQT, and go
                 back to step 1.
            Do not author code
            through the simulator.
            """;

        Assert.Equal(
            "The loop:\n  1. Check there.\n  2. Fix with MLQT, and go back to step 1.\n\nDo not author code through the simulator.",
            Prose.Unwrap(text));
    }

    [Fact]
    public void AHyphenInsideASentenceIsNotAListItem()
    {
        // Only a hyphen followed by a space at the start of a line starts an item; '-->' and '-1' do not.
        Assert.Equal("an extent of -10,-10 and -> arrows", Prose.Unwrap("an extent of\n-10,-10 and\n-> arrows"));
    }

    public static TheoryData<string> AllTopics()
    {
        var data = new TheoryData<string>();
        foreach (var topic in GuidanceTools.Topics)
            data.Add(topic);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void TheGuidanceBreaksALineOnlyBetweenParagraphsAndListItems(string topic)
    {
        var result = new GuidanceTools().GetGuidance(topic);
        var text = (string)result.GetType().GetProperty("guidance")!.GetValue(result)!;
        AssertNoBreakInsideAParagraph(text);
    }

    [Fact]
    public void TheInstructionsBreakALineOnlyBetweenParagraphs()
    {
        AssertNoBreakInsideAParagraph(ServerInstructions.Text);
    }

    private static void AssertNoBreakInsideAParagraph(string text)
    {
        Assert.DoesNotContain('\r', text);
        Assert.True(text.Length > 200, "the text is missing");

        // Every single line feed is followed by a list item. A wrap left in would be followed by the
        // rest of a sentence.
        var stray = Regex.Matches(text, @"(?<!\n)\n(?!\n|\s*(?:[-*]|\d+\.)\s)")
            .Select(m => text.Substring(Math.Max(0, m.Index - 30), Math.Min(60, text.Length - Math.Max(0, m.Index - 30))))
            .ToList();
        Assert.True(stray.Count == 0, "Line breaks inside a paragraph:\n" + string.Join("\n---\n", stray));
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Turns prose written for the source file into prose for an agent.
/// </summary>
/// <remarks>
/// The guidance and the instructions are raw string literals hard-wrapped at the width of the source,
/// and a raw string carries the file's line endings — CRLF on a Windows checkout. Sent as written, a
/// sentence reached the agent as <c>text\r\nreader</c>: a break that means nothing, which costs tokens,
/// reads as broken to anyone looking at the JSON, and differs by platform. What a line break does mean
/// is kept: a blank line between paragraphs, and the start of a list item.
/// </remarks>
internal static partial class Prose
{
    /// <summary>
    /// Joins each paragraph's wrapped lines with a space. Paragraphs stay separated by a blank line,
    /// and a list item (<c>- </c>, <c>* </c> or <c>1. </c>) starts a line of its own with its
    /// indentation, so nested items still read as nested. An item's wrapped lines are indented past
    /// its marker, so a line that is not ends the list and starts a paragraph — which is how the
    /// source closes a list with a sentence of its own. Line endings come back as line feeds.
    /// </summary>
    internal static string Unwrap(string text)
    {
        var result = new StringBuilder(text.Length);
        var afterBlank = false;
        var itemIndent = -1; // the indentation of the list item being continued; -1 outside a list

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var content = line.TrimStart();
            var indent = line.Length - content.Length;

            if (content.Length == 0)
            {
                afterBlank = result.Length > 0;
                itemIndent = -1;
                continue;
            }

            var isItem = ListItem().IsMatch(content);
            var endsList = !isItem && itemIndent >= 0 && indent <= itemIndent;

            if (result.Length == 0)
                result.Append(line);
            else if (afterBlank || endsList)
                result.Append("\n\n").Append(line);
            else if (isItem)
                result.Append('\n').Append(line);
            else
                result.Append(' ').Append(content);

            if (isItem)
                itemIndent = indent;
            else if (endsList)
                itemIndent = -1;
            afterBlank = false;
        }

        return result.ToString();
    }

    [GeneratedRegex(@"^(?:[-*]|\d+\.)\s")]
    private static partial Regex ListItem();
}

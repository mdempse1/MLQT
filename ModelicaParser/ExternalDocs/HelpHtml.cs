using System.Net;
using System.Text;

namespace ModelicaParser.ExternalDocs;

/// <summary>
/// Minimal scanning helpers for the generated help HTML.
///
/// <para>This works on the <b>tag stream</b>, never on lines. Dymola 2024x Refresh 1 shipped a
/// generator regression that emits a literal numeric token where newlines belong (~57k times in
/// the Modelica Standard Library alone), collapsing whole tables onto one line. The junk always
/// lands between tags, so a tag-oriented scanner is unaffected while anything that anchors a
/// marker to "the next line" reads that release as one unbroken blob. Do not reintroduce
/// line-based logic here.</para>
///
/// <para>A full DOM parser is deliberately not used: the input is machine-generated with a fixed
/// shape, the volume runs to thousands of classes across a thousand files per library, and the
/// handful of markers we need are unambiguous string literals.</para>
/// </summary>
internal static class HelpHtml
{
    /// <summary>
    /// Finds the next occurrence of an opening tag such as <c>&lt;td</c>, requiring the character
    /// after the name to be a delimiter so <c>&lt;table</c> is not matched when looking for
    /// <c>&lt;ta</c>. Returns -1 when there is none.
    /// </summary>
    public static int FindTag(string text, int start, string tagName)
    {
        var needle = "<" + tagName;
        var index = start;
        while (index >= 0 && index < text.Length)
        {
            index = text.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return -1;

            var after = index + needle.Length;
            if (after >= text.Length || after < text.Length && IsTagNameBoundary(text[after]))
                return index;

            index = after;
        }

        return -1;
    }

    private static bool IsTagNameBoundary(char c) => c == '>' || c == '/' || char.IsWhiteSpace(c);

    /// <summary>
    /// Index just past the '&gt;' that closes the tag starting at <paramref name="tagStart"/>,
    /// or the end of the text when the tag is unterminated.
    /// </summary>
    public static int EndOfTag(string text, int tagStart)
    {
        var close = text.IndexOf('>', tagStart);
        return close < 0 ? text.Length : close + 1;
    }

    /// <summary>
    /// Reads an attribute value from a single tag's text. Values are expected to be
    /// double-quoted, which the generator always does. Returns null when the attribute is absent.
    /// </summary>
    public static string? ReadAttribute(string tagText, string attributeName)
    {
        var needle = attributeName + "=\"";
        var index = tagText.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        while (index > 0)
        {
            // Require a delimiter before the name so `alt="` is not found inside `xalt="`.
            if (char.IsWhiteSpace(tagText[index - 1]))
            {
                var valueStart = index + needle.Length;
                var valueEnd = tagText.IndexOf('"', valueStart);
                if (valueEnd < 0)
                    return null;
                return WebUtility.HtmlDecode(tagText[valueStart..valueEnd]);
            }

            index = tagText.IndexOf(needle, index + needle.Length, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    /// <summary>
    /// The text of the tag that starts at <paramref name="tagStart"/>, including its angle
    /// brackets — the slice <see cref="ReadAttribute"/> expects.
    /// </summary>
    public static string TagTextAt(string text, int tagStart) =>
        text[tagStart..EndOfTag(text, tagStart)];

    /// <summary>
    /// Removes markup and decodes entities, collapsing runs of whitespace to single spaces.
    /// Used for text destined for description strings, which feed spell-checking — so the
    /// entities the generator emits (<c>&amp;#39;</c>, <c>&amp;quot;</c>, <c>&amp;reg;</c>) must
    /// not survive into the output.
    /// </summary>
    public static string StripTags(string html)
    {
        var text = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<')
                inTag = true;
            else if (c == '>')
                inTag = false;
            else if (!inTag)
                text.Append(c);
        }

        return CollapseWhitespace(WebUtility.HtmlDecode(text.ToString()));
    }

    /// <summary>
    /// Collapses all whitespace runs (including the newlines the generator wraps attributes on)
    /// to single spaces and trims. The non-breaking space that <c>&amp;nbsp;</c> decodes to counts
    /// as whitespace here, which is what makes the generator's empty-cell filler come out as an
    /// empty string rather than as a stray invisible character.
    /// </summary>
    public static string CollapseWhitespace(string text)
    {
        var result = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = result.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                result.Append(' ');
                pendingSpace = false;
            }

            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Splits on commas that sit outside both parentheses and tags, so a base-class list can be
    /// separated without a description's own commas ("Extends from A (a desc, with comma), B (…)")
    /// tearing an entry in half.
    /// </summary>
    public static List<string> SplitTopLevelCommas(string html)
    {
        var parts = new List<string>();
        var depth = 0;
        var inTag = false;
        var start = 0;
        for (var i = 0; i < html.Length; i++)
        {
            var c = html[i];
            if (c == '<')
                inTag = true;
            else if (c == '>')
                inTag = false;
            else if (inTag)
                continue;
            else if (c == '(')
                depth++;
            else if (c == ')')
                depth = Math.Max(0, depth - 1);
            else if (c == ',' && depth == 0)
            {
                parts.Add(html[start..i]);
                start = i + 1;
            }
        }

        if (start < html.Length)
            parts.Add(html[start..]);

        return parts;
    }

    /// <summary>
    /// Reads the leading Modelica qualified identifier (<c>A.B.C</c>) from plain text, or null
    /// when the text does not begin with one. Quoted identifiers ('…') are accepted as segments
    /// because Modelica permits them in class names.
    /// </summary>
    public static string? LeadingQualifiedName(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;

        var start = i;

        // Tracks the end of the last complete segment, so a trailing separator is not swallowed:
        // "Extends from Real." must yield "Real", not "Real.".
        var end = start;
        while (true)
        {
            var segmentStart = i;
            if (i < text.Length && text[i] == '\'')
            {
                i++;
                while (i < text.Length && text[i] != '\'')
                    i++;
                if (i < text.Length)
                    i++;   // closing quote
            }
            else
            {
                if (i >= text.Length || !(char.IsLetter(text[i]) || text[i] == '_'))
                    break;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;
            }

            if (i == segmentStart)
                break;

            end = i;

            if (i < text.Length && text[i] == '.')
            {
                i++;
                continue;
            }

            break;
        }

        return end > start ? text[start..end] : null;
    }
}

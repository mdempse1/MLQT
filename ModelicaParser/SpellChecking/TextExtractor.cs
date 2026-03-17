using System.Net;
using System.Text.RegularExpressions;

namespace ModelicaParser.SpellChecking;

/// <summary>
/// Utility methods for extracting and preparing text for spell checking.
/// </summary>
public static partial class TextExtractor
{
    // Modelica keywords that should not be spell-checked
    private static readonly HashSet<string> ModelicaKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "algorithm", "and", "annotation", "block", "break", "class", "connect",
        "connector", "constant", "constrainedby", "der", "discrete", "each",
        "else", "elseif", "elsewhen", "encapsulated", "end", "enumeration",
        "equation", "expandable", "extends", "external", "false", "final",
        "flow", "for", "function", "if", "import", "impure", "in", "initial",
        "inner", "input", "loop", "model", "not", "operator", "or", "outer",
        "output", "package", "parameter", "partial", "protected", "public",
        "pure", "record", "redeclare", "replaceable", "return", "stream",
        "then", "true", "type", "when", "while", "within"
    };

    /// <summary>
    /// Strips HTML tags from a string, removes content inside code/pre blocks,
    /// and decodes HTML entities.
    /// </summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove content inside <code>...</code> and <pre>...</pre> tags (contains code, not prose)
        var result = CodeBlockRegex().Replace(html, " ");

        // Remove all remaining HTML tags
        result = HtmlTagRegex().Replace(result, " ");

        // Decode HTML entities
        result = WebUtility.HtmlDecode(result);

        // Collapse multiple whitespace into single spaces
        result = MultiWhitespaceRegex().Replace(result, " ");

        return result.Trim();
    }

    /// <summary>
    /// Splits text into words with their character offsets in the source string.
    /// Words are delimited by whitespace and punctuation. Underscores and digits are
    /// kept as part of the word so that identifiers like "transformationMatrixFrom_nxy"
    /// remain intact and can be skipped by ShouldSkipWord.
    /// </summary>
    public static IEnumerable<(string word, int charOffset)> TokenizeToWords(string text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isWordChar = i < text.Length &&
                (char.IsLetterOrDigit(text[i]) || text[i] == '\'' || text[i] == '_');

            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                var word = text[start..i];
                // Strip leading/trailing apostrophes and underscores
                word = word.Trim('\'', '_');
                if (word.Length > 0)
                    yield return (word, start);
                start = -1;
            }
        }
    }

    /// <summary>
    /// Returns true for tokens that should be skipped during spell checking:
    /// camelCase identifiers, words with dots/underscores/digits, single characters,
    /// ALL_CAPS constants, Modelica keywords, non-ASCII characters (decoded HTML entities),
    /// and numeric strings.
    /// </summary>
    public static bool ShouldSkipWord(string word)
    {
        if (string.IsNullOrEmpty(word))
            return true;

        // Single character
        if (word.Length == 1)
            return true;

        // Two-letter words that are likely variable names (but allow common English two-letter words)
        // We don't skip all two-letter words since many are valid English (is, an, at, by, etc.)

        // Contains non-ASCII characters (likely decoded HTML entities like &Delta; → Δ, &zeta; → ζ)
        if (word.Any(c => c > 127))
            return true;

        // Contains digits
        if (word.Any(char.IsDigit))
            return true;

        // Contains dots, underscores, or slashes (qualified names, file paths)
        if (word.Contains('.') || word.Contains('_') || word.Contains('/') || word.Contains('\\'))
            return true;

        // ALL_CAPS (likely a constant or acronym) — 3+ uppercase letters with no lowercase
        if (word.Length >= 2 && word.All(c => char.IsUpper(c) || !char.IsLetter(c)))
            return true;

        // camelCase or PascalCase detection: has both upper and lower case letters
        // with an uppercase letter after a lowercase letter (e.g., "myVariable", "TimeStep")
        bool hasLower = false;
        bool hasUpperAfterLower = false;
        foreach (var c in word)
        {
            if (char.IsLower(c))
                hasLower = true;
            else if (char.IsUpper(c) && hasLower)
                hasUpperAfterLower = true;
        }
        if (hasUpperAfterLower)
            return true;

        // Modelica keyword
        if (ModelicaKeywords.Contains(word))
            return true;

        return false;
    }

    /// <summary>
    /// Removes surrounding double quotes from a STRING token value.
    /// </summary>
    public static string StripQuotes(string quotedString)
    {
        if (quotedString.Length >= 2 && quotedString[0] == '"' && quotedString[^1] == '"')
            return quotedString[1..^1];
        return quotedString;
    }

    /// <summary>
    /// Strips HTML tags and decodes entities like StripHtml, but preserves newline characters
    /// so that character offsets can be mapped back to source line numbers.
    /// </summary>
    public static string StripHtmlPreservingNewlines(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove content inside <code>...</code> and <pre>...</pre> tags,
        // but preserve newlines so line counting stays correct
        var result = CodeBlockRegex().Replace(html, match =>
            PreserveNewlines(match.Value));

        // Remove all remaining HTML tags, preserving newlines within multi-line tags
        result = HtmlTagRegex().Replace(result, match =>
            PreserveNewlines(match.Value));

        // Decode HTML entities
        result = WebUtility.HtmlDecode(result);

        // Collapse non-newline whitespace only (preserve \n for line counting)
        result = NonNewlineWhitespaceRegex().Replace(result, " ");

        return result;
    }

    /// <summary>
    /// Returns a string containing only the newline characters from the input,
    /// with a single space if there are no newlines. Used to replace HTML content
    /// while preserving line positions.
    /// </summary>
    private static string PreserveNewlines(string value)
    {
        int newlineCount = 0;
        foreach (var c in value)
        {
            if (c == '\n')
                newlineCount++;
        }
        return newlineCount > 0 ? new string('\n', newlineCount) : " ";
    }

    /// <summary>
    /// Counts the number of newline characters in text before the given offset.
    /// </summary>
    public static int CountNewlinesBefore(string text, int offset)
    {
        int count = 0;
        int limit = Math.Min(offset, text.Length);
        for (int i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
                count++;
        }
        return count;
    }

    [GeneratedRegex(@"<(code|pre)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex NonNewlineWhitespaceRegex();
}

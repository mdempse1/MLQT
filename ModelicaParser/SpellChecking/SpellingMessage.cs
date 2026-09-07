using System.Text.RegularExpressions;

namespace ModelicaParser.SpellChecking;

/// <summary>
/// The wording of a spelling finding, and the way to read the flagged word back out of it.
///
/// <para>The word is what the desktop app acts on — it underlines every occurrence in the code view,
/// offers corrections for it, and records it in the repository's accepted spellings. Reading it back
/// out of the message is not ideal, but the alternative that looked structural is worse: a finding's
/// <c>Discriminator</c> exists to make its fingerprint unique, and for documentation it carries the
/// section as well as the word ("documentation info:tyre"). Taking the word from there underlined
/// nothing, because no such text appears in the source.</para>
///
/// <para>So the format lives here, next to the rules that produce it, with the reader beside the
/// writers: a change to the wording that the app could not follow is a failing test rather than a
/// word that silently stops being underlined.</para>
/// </summary>
public static class SpellingMessage
{
    private const string Prefix = "Misspelled word '";

    /// <summary>Where the word was found, as it appears in the message.</summary>
    public const string InDescription = "description";
    public const string InDocumentationInfo = "documentation info";
    public const string InDocumentationRevisions = "documentation revisions";

    /// <summary>The message for a misspelling found in <paramref name="where"/>.</summary>
    public static string For(string word, string where) => $"{Prefix}{word}' in {where}";

    /// <summary>True if this message is a spelling finding.</summary>
    public static bool Is(string? message) => message?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    // Greedy up to the trailing " in <where>", because the word itself can contain the quote it is
    // wrapped in — "Stodola's". Stopping at the first quote yielded "Stodola", which underlined every
    // plain occurrence in the file and not the one that was reported.
    private static readonly Regex WordPattern =
        new(@"^Misspelled word '(.+)' in ", RegexOptions.Compiled);

    /// <summary>The flagged word, or null if this is not a spelling message.</summary>
    public static string? WordFrom(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        var match = WordPattern.Match(message);
        return match.Success ? match.Groups[1].Value : null;
    }
}

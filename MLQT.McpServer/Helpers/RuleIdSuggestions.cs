using System.Text;
using ModelicaParser.StyleRules;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// The catalog's rule ids nearest to one that is not in it, for an answer that refuses the wrong one:
/// an agent that wrote <c>MLQT.Style.NamingConvention</c> wants <c>MLQT.Naming.Convention</c>.
/// </summary>
/// <remarks>
/// Ranked by the words they share - an id split at its dots and its capitals, so
/// <c>Style.NamingConvention</c> and <c>Naming.Convention</c> share "naming" and "convention" - and then
/// by how far apart they are spelt. An id sharing no word with the one asked for is no suggestion.
/// </remarks>
internal static class RuleIdSuggestions
{
    private const string Prefix = "MLQT.";

    /// <summary>Up to <paramref name="count"/> catalog ids nearest <paramref name="ruleId"/>, nearest first.</summary>
    public static IReadOnlyList<RuleDefinition> For(string ruleId, int count = 3)
    {
        var words = Words(ruleId);
        var flat = Flat(ruleId);
        return RuleCatalog.BuiltIn.Values
            .Select(rule => (Rule: rule, Shared: Words(rule.Id).Intersect(words).Count(), Distance: Distance(flat, Flat(rule.Id))))
            .Where(c => c.Shared > 0)
            .OrderByDescending(c => c.Shared)
            .ThenBy(c => c.Distance)
            .ThenBy(c => c.Rule.Id, StringComparer.Ordinal)
            .Take(count)
            .Select(c => c.Rule)
            .ToList();
    }

    /// <summary>The suggestions as one phrase: "MLQT.Naming.Convention (Follow naming convention)", ...</summary>
    public static string Phrase(string ruleId) =>
        For(ruleId) is { Count: > 0 } near
            ? string.Join(", ", near.Select(r => $"{r.Id} ({r.Title})"))
            : "none close to it";

    // The lowercase words of an id, without its MLQT. prefix: MLQT.Style.NamingConvention -> style, naming, convention.
    private static HashSet<string> Words(string ruleId)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length > 0)
                words.Add(word.ToString().ToLowerInvariant());
            word.Clear();
        }

        foreach (var c in Unprefixed(ruleId))
        {
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }
            if (char.IsUpper(c) && word.Length > 0 && !char.IsUpper(word[^1]))
                Flush();
            word.Append(c);
        }
        Flush();
        return words;
    }

    private static string Flat(string ruleId) =>
        new([.. Unprefixed(ruleId).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    private static string Unprefixed(string ruleId) =>
        ruleId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? ruleId[Prefix.Length..] : ruleId;

    // Levenshtein distance.
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}

using System.Text;
using System.Text.RegularExpressions;
using ModelicaGraph.DataTypes;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Says, from a class's stored source alone, which classes a text search can skip without parsing them:
/// those whose own text cannot hold the query, however its description and documentation are read.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A text search parsed every loaded class to read its description and its
/// <c>Documentation(info=...)</c>, and kept every tree. Over MSL 4.1.0, Complex and ModelicaServices
/// that was 31 s on the first call; with the Dymola libraries loaded beside them it passed a client's
/// 60 s timeout, and the client dropped the server and everything loaded in it. Nearly every class can
/// be ruled out by looking at its text.</para>
/// <para><b>It only ever rules a class out when no reading of it could match</b>, so the results are
/// exactly the ones a search of every class gives. The query is cut into its runs of ASCII letters,
/// digits and underscores, and a class is kept when every run appears in its own text, ignoring case.
/// That holds because of how the searched text is made from the source: string literals joined
/// (<c>"a" + "b"</c>), and for documentation HTML tags replaced by a space, entities decoded and white
/// space collapsed. None of that can make a run of those characters out of text that does not contain
/// it, except two things, and both are allowed for:</para>
/// <list type="bullet">
/// <item>a run split across two joined literals, so the text is tried again with each joint
/// (<c>" + "</c>) taken out;</item>
/// <item>a numeric entity (<c>&amp;#77;</c>), which can decode to a letter, so a class whose text holds
/// <c>&amp;#</c> is always kept.</item>
/// </list>
/// <para><b>A class's own text</b> is its source without the classes written inline in it, which are
/// classes of their own and searched as such. Without that, every package above a matching class would
/// be parsed too, and a package is the largest thing there is to parse.</para>
/// </remarks>
internal sealed partial class TextSearchPrefilter
{
    private readonly string[] _runs;
    private readonly IReadOnlyDictionary<string, List<ModelNode>> _nestedByParent;

    /// <param name="query">The query as the search compares it.</param>
    /// <param name="candidates">Every class the search will look at, so the classes written inline in
    /// each can be found.</param>
    public TextSearchPrefilter(string query, IEnumerable<ModelNode> candidates)
    {
        _runs = Runs(query);
        // Built even when there is nothing to look for: ReducedSource needs it whatever the query.
        _nestedByParent = candidates
            .Where(m => m.IsNested && m.ParentModelName is not null)
            .GroupBy(m => m.ParentModelName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    /// <summary>The runs a class's text must hold, for tests.</summary>
    internal IReadOnlyList<string> RequiredRuns => _runs;

    /// <summary>False only when <paramref name="node"/>'s description and documentation cannot contain the query.</summary>
    public bool MayMatch(ModelNode node)
    {
        if (_runs.Length == 0)
            return true;
        var code = node.Definition.ModelicaCode;
        if (string.IsNullOrEmpty(code))
            return true;
        if (!HoldsEveryRun(code))
            return HoldsEveryRunJoined(code);
        if (code.Contains("&#", StringComparison.Ordinal))
            return true;

        var own = OwnText(code, _nestedByParent.GetValueOrDefault(node.Id), placeholder: false);
        return ReferenceEquals(own, code) || HoldsEveryRun(own) || HoldsEveryRunJoined(own);
    }

    /// <summary>
    /// <paramref name="node"/>'s source with each class written inline in it replaced by an empty
    /// <c>class _ end _</c>, or null when it has none. It is far smaller to parse than the source, and
    /// it gives the same description and documentation: both are read from the outermost class's own
    /// specifier and class-level annotations, never from its elements, and a class definition is
    /// replaced by another in the same place. A placeholder that does not fit where it is put - an
    /// <c>operator</c> prefix left outside the child's text, say - makes the text fail to parse, and
    /// the caller then parses the source as it is.
    /// </summary>
    public string? ReducedSource(ModelNode node)
    {
        var code = node.Definition.ModelicaCode;
        if (string.IsNullOrEmpty(code) || _nestedByParent.GetValueOrDefault(node.Id) is not { } nested)
            return null;
        var reduced = OwnText(code, nested, placeholder: true);
        return ReferenceEquals(reduced, code) ? null : reduced;
    }

    private const string Placeholder = "class _ end _";

    private bool HoldsEveryRun(string text)
    {
        foreach (var run in _runs)
        {
            if (!text.Contains(run, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private bool HoldsEveryRunJoined(string text)
    {
        if (text.Contains("&#", StringComparison.Ordinal))
            return true;
        return LiteralJoint().IsMatch(text) && HoldsEveryRun(LiteralJoint().Replace(text, string.Empty));
    }

    // The source with each inline class cut out and a space left in its place, or with 'placeholder' the
    // placeholder class, and a ';' after it when the child's text ended in one, as a stored class's does.
    // A child whose text is not found as written (a reformatted copy, say) is left in, which only keeps
    // more classes.
    private static string OwnText(string code, List<ModelNode>? nested, bool placeholder)
    {
        if (nested is null)
            return code;

        var spans = new List<(int Start, int End, bool Semicolon)>();
        foreach (var child in nested)
        {
            var childCode = child.Definition.ModelicaCode;
            if (string.IsNullOrEmpty(childCode))
                continue;
            var at = code.IndexOf(childCode, StringComparison.Ordinal);
            if (at >= 0)
                spans.Add((at, at + childCode.Length, childCode.AsSpan().TrimEnd().EndsWith(";")));
        }
        if (spans.Count == 0)
            return code;

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        var own = new StringBuilder(code.Length);
        var from = 0;
        foreach (var (start, end, semicolon) in spans)
        {
            if (start < from)
            {
                from = Math.Max(from, end);
                continue;
            }
            own.Append(code, from, start - from);
            if (placeholder)
                own.Append(Placeholder).Append(semicolon ? ";" : "");
            else
                own.Append(' ');
            from = end;
        }
        own.Append(code, from, code.Length - from);
        return own.ToString();
    }

    private static string[] Runs(string query)
    {
        var runs = new List<string>();
        var start = -1;
        for (var i = 0; i <= query.Length; i++)
        {
            var inRun = i < query.Length && (char.IsAsciiLetterOrDigit(query[i]) || query[i] == '_');
            if (inRun && start < 0)
                start = i;
            else if (!inRun && start >= 0)
            {
                runs.Add(query[start..i]);
                start = -1;
            }
        }
        return runs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // The end of one string literal and the start of the next, joined by '+': "...abc" + "def...".
    [GeneratedRegex("\"\\s*\\+\\s*\"", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralJoint();
}

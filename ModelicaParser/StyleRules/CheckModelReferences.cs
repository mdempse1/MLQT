using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Style rule that validates modelica:// model references in documentation and annotations.
/// Reports violations when a modelica:// URI references a model that does not exist
/// in the loaded libraries.
/// </summary>
public class CheckModelReferences : VisitorWithModelNameTracking
{
    private readonly IReadOnlySet<string> _knownModelIds;

    /// <summary>
    /// Creates a new instance with the set of known model IDs for validation.
    /// </summary>
    /// <param name="knownModelIds">All model IDs currently loaded in the graph.</param>
    /// <param name="basePackage">The package prefix from the within clause.</param>
    public CheckModelReferences(IReadOnlySet<string> knownModelIds, string basePackage = "")
        : base(basePackage)
    {
        _knownModelIds = knownModelIds;
    }

    public override object? VisitPrimary([NotNull] modelicaParser.PrimaryContext context)
    {
        if (context.STRING() != null)
        {
            var text = context.STRING().GetText();
            if (text.Contains("modelica://", StringComparison.OrdinalIgnoreCase))
            {
                ValidateModelReferences(text, context.Start.Line);
            }
        }

        return base.VisitPrimary(context);
    }

    /// <summary>
    /// Extracts modelica:// URIs that reference models (not files) and validates
    /// that the referenced model exists in the loaded libraries.
    /// </summary>
    private void ValidateModelReferences(string stringLiteral, int startLine)
    {
        var text = StripQuotes(stringLiteral);
        if (string.IsNullOrWhiteSpace(text))
            return;

        int startIndex = 0;
        // Track newlines seen so far to compute accurate line numbers
        int newlineCount = 0;
        int lastNewlineSearchEnd = 0;

        while (startIndex < text.Length)
        {
            var uriStart = text.IndexOf("modelica://", startIndex, StringComparison.OrdinalIgnoreCase);
            if (uriStart < 0)
                break;

            // Count newlines between last search position and this URI
            for (int i = lastNewlineSearchEnd; i < uriStart; i++)
            {
                if (text[i] == '\n')
                    newlineCount++;
            }
            lastNewlineSearchEnd = uriStart;

            // Skip HTML entity-encoded links (e.g., &quot;modelica://...&quot; in example code).
            // These appear when documentation shows HTML source as visible text.
            if (IsInsideHtmlEntity(text, uriStart))
            {
                startIndex = uriStart + "modelica://".Length;
                continue;
            }

            // Only validate URIs inside HTML attribute values (preceded by a quote character)
            // or at the start of the string (the entire string is a URI value).
            // Skip plain text mentions like "Replace modelica://-URIs by ..." which are
            // just descriptive text, not actual links.
            if (uriStart > 0 && text[uriStart - 1] != '"')
            {
                startIndex = uriStart + "modelica://".Length;
                continue;
            }

            // Extract the URI up to the next delimiter, but skip delimiters
            // inside Modelica quoted identifiers ('...' sections)
            var uriEnd = uriStart + "modelica://".Length;
            bool inQuotedId = false;
            while (uriEnd < text.Length)
            {
                var ch = text[uriEnd];
                if (ch == '\'')
                {
                    inQuotedId = !inQuotedId;
                    uriEnd++;
                    continue;
                }
                if (inQuotedId)
                {
                    uriEnd++;
                    continue;
                }
                if (char.IsWhiteSpace(ch) ||
                    ch == '"' || ch == '>' || ch == '<' ||
                    ch == ')' || ch == '\\' || ch == '#' || ch == '&')
                    break;
                uriEnd++;
            }

            var uri = text.Substring(uriStart, uriEnd - uriStart);
            var pathPart = uri.Substring("modelica://".Length);

            // Only validate model references (no '/' path separator = model reference).
            // URIs with '/' are file references handled by ExternalResourceExtractor.
            if (!string.IsNullOrEmpty(pathPart) && !pathPart.Contains('/'))
            {
                if (!_knownModelIds.Contains(pathPart))
                {
                    AddViolation(startLine + newlineCount, $"Broken model reference: {uri} — the model '{pathPart}' was not found in the loaded libraries");
                }
            }

            startIndex = uriEnd;
        }
    }

    /// <summary>
    /// Checks whether the modelica:// URI at the given position is preceded by an HTML entity
    /// like &amp;quot; or &amp;#34;, indicating it's inside entity-encoded example markup
    /// rather than a real link.
    /// </summary>
    private static bool IsInsideHtmlEntity(string text, int uriStart)
    {
        // Look for &quot; or &#34; immediately before the URI
        if (uriStart >= 6 && text.Substring(uriStart - 6, 6).Equals("&quot;", StringComparison.OrdinalIgnoreCase))
            return true;
        if (uriStart >= 5 && text.Substring(uriStart - 5, 5).Equals("&#34;", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        return text;
    }
}

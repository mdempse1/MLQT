using Antlr4.Runtime.Misc;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>What a <c>modelica://</c> link to a class leads to.</summary>
public enum ClassReferenceTarget
{
    /// <summary>A class of that name exists, declared or inherited.</summary>
    Found,

    /// <summary>There is no such class, and MLQT would know if there were.</summary>
    Missing,

    /// <summary>
    /// No such class is known, but one could be in what MLQT cannot see: a library that is not
    /// loaded, an encrypted class, a base that does not resolve. Never reported.
    /// </summary>
    CannotTell,
}

/// <summary>
/// Style rule that validates modelica:// model references in documentation and annotations.
/// Reports findings when a modelica:// URI references a model that does not exist
/// in the loaded libraries.
/// </summary>
/// <remarks>
/// <para>Whether a class exists is a question about the whole graph, which this assembly cannot see,
/// so the answer is passed in. The check pipeline passes ModelicaGraph's, which answers the roadmap's
/// three states: a link into a library that is not loaded, or into a class whose bases MLQT cannot
/// read, is <see cref="ClassReferenceTarget.CannotTell"/> and is not reported. The set-based
/// constructor kept for callers outside the pipeline can only say found or missing.</para>
/// </remarks>
public class CheckModelReferences : VisitorWithModelNameTracking
{
    private readonly Func<string, ClassReferenceTarget> _target;

    /// <summary>
    /// Creates a new instance with the set of known model IDs for validation.
    /// </summary>
    /// <param name="knownModelIds">All model IDs currently loaded in the graph.</param>
    /// <param name="basePackage">The package prefix from the within clause.</param>
    /// <remarks>Every name not in the set is reported, including one into a library that is not
    /// loaded. Prefer the constructor taking a verdict.</remarks>
    public CheckModelReferences(IReadOnlySet<string> knownModelIds, string basePackage = "")
        : this(name => knownModelIds.Contains(name) ? ClassReferenceTarget.Found : ClassReferenceTarget.Missing, basePackage)
    {
    }

    /// <summary>
    /// A rule that asks <paramref name="target"/> what each class link leads to, and reports only a
    /// <see cref="ClassReferenceTarget.Missing"/> one.
    /// </summary>
    /// <param name="target">The verdict for a fully qualified class name.</param>
    /// <param name="basePackage">The package prefix from the within clause.</param>
    /// <remarks>A factory rather than a second constructor: two public overloads with optional
    /// parameters are a source break for a caller outside the repository (RS0026/RS0027), and the
    /// set-based constructor's shape is one they may already use.</remarks>
    public static CheckModelReferences WithVerdict(Func<string, ClassReferenceTarget> target, string basePackage = "") =>
        new(target, basePackage);

    private CheckModelReferences(Func<string, ClassReferenceTarget> target, string basePackage)
        : base(basePackage)
    {
        _target = target;
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
            if (ModelicaUriScanner.IsShownAsText(text, uriStart))
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

            // Where it ends is the scanner's answer, the one the resource extraction uses, so a link and
            // a file reference are cut the same way: at an anchor, and past a quoted identifier.
            var uriEnd = ModelicaUriScanner.EndOf(text, uriStart);
            var uri = text.Substring(uriStart, uriEnd - uriStart);
            var pathPart = uri.Substring("modelica://".Length);

            // Only validate model references (no '/' path separator = model reference).
            // URIs with '/' are file references handled by ExternalResourceExtractor.
            if (!string.IsNullOrEmpty(pathPart) && !pathPart.Contains('/'))
            {
                if (_target(pathPart) == ClassReferenceTarget.Missing)
                {
                    AddFinding(startLine + newlineCount, $"Broken model reference: {uri} — the model '{pathPart}' was not found in the loaded libraries",
                        RuleIds.ModelReferences, discriminator: pathPart);
                }
            }

            startIndex = uriEnd;
        }
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            return text.Substring(1, text.Length - 2);
        return text;
    }
}

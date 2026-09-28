namespace ModelicaParser.Helpers;

/// <summary>
/// Finds <c>modelica://</c> URIs in a string, and decides which of them name a file.
///
/// <para><b>One implementation, because two visitors scan for the same thing.</b>
/// <c>ExternalResourceExtractor</c> (ModelicaParser) and <c>ModelAnalyzer</c> (ModelicaGraph) each
/// carried a byte-for-byte copy of this loop and of the extension test. The graph build uses the
/// second, so a fix applied to the first changed nothing a user could see — which is exactly what
/// happened to B209 before this file existed.</para>
/// </summary>
public static class ModelicaUriScanner
{
    private const string Scheme = "modelica://";

    /// <summary>
    /// Every <c>modelica://</c> URI in <paramref name="text"/> that names a file, in the order they
    /// appear.
    /// </summary>
    public static IEnumerable<string> FindFileUris(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var startIndex = 0;
        while (startIndex < text.Length)
        {
            var uriStart = text.IndexOf(Scheme, startIndex, StringComparison.OrdinalIgnoreCase);
            if (uriStart < 0)
                yield break;

            var uriEnd = uriStart + Scheme.Length;
            while (uriEnd < text.Length && !IsTerminator(text[uriEnd]))
                uriEnd++;

            var uri = text[uriStart..uriEnd];
            if (NamesAFile(uri))
                yield return uri;

            startIndex = uriEnd;
        }
    }

    /// <summary>
    /// Where a URI stops.
    /// </summary>
    /// <remarks>
    /// <para><c>&amp;</c> is the one that was missing (B209). A Modelica documentation string carries
    /// HTML inside a Modelica string, so the HTML's own quotes are commonly written as entities:</para>
    /// <code>&amp;lt;img src=&amp;quot;modelica://Modelica/Resources/Images/foo.png&amp;quot;&amp;gt;</code>
    /// <para>With no literal quote to stop at, the scan ran past the end of the file name and kept the
    /// entity — <c>foo.png&amp;quot;</c> — a path that cannot exist and is therefore reported missing
    /// for ever. The Modelica Standard Library has two of those, and one of them names a file that is
    /// on disk. No Modelica resource path contains <c>&amp;</c>, so stopping there costs nothing.</para>
    /// </remarks>
    private static bool IsTerminator(char c) =>
        char.IsWhiteSpace(c) || c is '"' or '\'' or '>' or '<' or ')' or '&' or '\\';

    /// <summary>
    /// Whether a URI points at a file rather than at a class — <c>modelica://Modelica.Blocks</c> is a
    /// class reference and not a resource. Decided by the last segment having an extension.
    /// </summary>
    public static bool NamesAFile(string uri)
    {
        if (!uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var pathPart = uri[Scheme.Length..];
        var lastSlash = pathPart.LastIndexOf('/');
        if (lastSlash < 0)
            return false;

        var lastSegment = pathPart[(lastSlash + 1)..];
        var dot = lastSegment.LastIndexOf('.');

        // An extension is a dot that is neither the first nor the last character of the segment.
        return dot > 0 && dot < lastSegment.Length - 1;
    }
}

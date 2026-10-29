namespace ModelicaParser.Helpers;

/// <summary>
/// Finds <c>modelica://</c> URIs in a string, and decides which of them name a file.
///
/// <para><b>One implementation, because two visitors scan for the same thing.</b>
/// <c>ExternalResourceExtractor</c> (ModelicaParser) and <c>ModelAnalyzer</c> (ModelicaGraph) each
/// carried a byte-for-byte copy of this loop and of the extension test. The graph build uses the
/// second, so a fix applied to the first changed nothing a user could see — which is exactly what
/// happened to B209 before this file existed. Since B511 <c>ModelAnalyzer</c> derives from the
/// extractor, so this has one caller.</para>
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
            if (NamesAFile(uri) && !IsShownAsText(text, uriStart))
                yield return uri;

            startIndex = uriEnd;
        }
    }

    /// <summary>
    /// Whether the URI at <paramref name="uriStart"/> is part of HTML the documentation shows as text
    /// rather than markup it renders: its opening quote written as an entity, <c>&amp;quot;</c> or
    /// <c>&amp;#34;</c>.
    /// </summary>
    /// <remarks>
    /// <para>In a Modelica documentation string the HTML's own quotes are <c>\"</c>.
    /// <c>&amp;lt;img src=&amp;quot;modelica://…&amp;quot;&amp;gt;</c> renders as the characters
    /// <c>&lt;img src="modelica://…"&gt;</c> - an example of how to write an image, which is what MSL's
    /// <c>UsersGuide.Conventions</c> uses it for. It names no file the library needs.</para>
    ///
    /// <para>Measured over MSL and Buildings: 1,792 image references written with <c>\"</c>, five with
    /// <c>&amp;quot;</c>, and all five in that guide's examples. B209 took the entity form for the
    /// ordinary one and kept those five as references, cut at the entity, so the External Resources
    /// tab listed the guide's example image as a missing file - and <c>MLQT.Resource.Missing</c>, run
    /// over MSL, reported it. <c>CheckModelReferences</c> had always skipped them, for this reason, with
    /// a copy of this test of its own; it uses this one now.</para>
    /// </remarks>
    public static bool IsShownAsText(string text, int uriStart) =>
        EndsAt(text, uriStart, "&quot;", StringComparison.OrdinalIgnoreCase)
        || EndsAt(text, uriStart, "&#34;", StringComparison.Ordinal);

    private static bool EndsAt(string text, int end, string entity, StringComparison comparison) =>
        end >= entity.Length && string.Compare(text, end - entity.Length, entity, 0, entity.Length, comparison) == 0;

    /// <summary>
    /// Where a URI stops.
    /// </summary>
    /// <remarks>
    /// <para><c>&amp;</c> was added for B209, which met a URI between entity quotes,
    /// <c>&amp;quot;modelica://…/foo.png&amp;quot;</c>, and read on past the file name into the entity.
    /// Such a URI is now not a reference at all (<see cref="IsShownAsText"/>), but no Modelica resource
    /// path contains <c>&amp;</c>, so stopping there still costs nothing.</para>
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

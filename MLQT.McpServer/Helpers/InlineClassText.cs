namespace MLQT.McpServer.Helpers;

/// <summary>
/// Where a class written inline in another is, in the other's stored source - which the code search
/// and the text search both need, to treat each line or each description as the innermost class's.
/// </summary>
/// <remarks>
/// <para><b>A child's stored source is not always a slice of its parent's.</b> Every class of a
/// read-only library opens with its provenance banner (<c>ReadOnlySources.Banner</c>), and its package,
/// which is one class too, carries the banner once at its top. So the child's text is looked for as
/// stored, and then without the <c>//</c> lines it opens with. Without that a supplied package
/// reported each of its children's lines as its own as well, and was parsed whole by a text search.</para>
/// <para><b>Found only when it is there exactly once.</b> A second copy - the child written out in
/// the package's description, say - leaves no way to tell which is the class, and taking the wrong
/// one hides the package's own text. Not finding it only means the child's text is read twice.</para>
/// </remarks>
internal static class InlineClassText
{
    /// <summary>Where <paramref name="childCode"/> is in <paramref name="parentCode"/>, or null.</summary>
    public static (int Start, int Length)? Locate(string parentCode, string childCode)
    {
        if (string.IsNullOrEmpty(childCode))
            return null;
        if (Once(parentCode, childCode) is { } asStored)
            return (asStored, childCode.Length);

        var body = WithoutLeadingLineComments(childCode);
        return body.Length > 0 && body.Length < childCode.Length && Once(parentCode, body) is { } bare
            ? (bare, body.Length)
            : null;
    }

    private static int? Once(string text, string part)
    {
        var at = text.IndexOf(part, StringComparison.Ordinal);
        return at >= 0 && text.IndexOf(part, at + 1, StringComparison.Ordinal) < 0 ? at : null;
    }

    // The text after the // comment lines it opens with.
    private static string WithoutLeadingLineComments(string code)
    {
        var from = 0;
        while (code.AsSpan(from).StartsWith("//", StringComparison.Ordinal))
        {
            var end = code.IndexOf('\n', from);
            if (end < 0)
                return string.Empty;
            from = end + 1;
        }
        return code[from..];
    }
}

namespace RevisionControl;

/// <summary>
/// How a revision identifier is shown to a person.
///
/// <para>The two revision control systems identify a revision differently, and only one of them has
/// anything to shorten: a Git commit is a 40-character hash of which the first few are enough to
/// recognise and to check out, while an SVN revision is already a short number and truncating it
/// would name a different revision entirely. So the decision is made from the identifier itself and
/// callers do not have to know which system produced it — which matters where the identifier arrives
/// on its own, such as the revision recorded in a metrics snapshot.</para>
/// </summary>
public static class RevisionId
{
    /// <summary>
    /// How many characters of a commit hash are shown. Git's own default abbreviation, and enough to
    /// be unique in any repository MLQT is likely to open.
    /// </summary>
    public const int ShortLength = 7;

    /// <summary>
    /// The revision as it should be displayed: a commit hash shortened to <paramref name="length"/>
    /// characters, anything else — an SVN revision number, an already-shortened hash, a branch name —
    /// returned as it is. Null or empty becomes an empty string, so it can be bound to directly.
    /// </summary>
    public static string Shorten(string? revision, int length = ShortLength) =>
        length > 0 && IsCommitHash(revision) && revision!.Length > length
            ? revision[..length]
            : revision ?? string.Empty;

    /// <summary>
    /// True if this is a full Git object id: 40 hex characters (SHA-1) or 64 (SHA-256).
    ///
    /// <para>The length is checked exactly rather than as a minimum. An SVN revision number is hex by
    /// accident — "1234567" parses as hex perfectly well — and shortening one would quietly report the
    /// wrong revision.</para>
    /// </summary>
    public static bool IsCommitHash(string? revision) =>
        revision is { Length: 40 or 64 } && revision.All(char.IsAsciiHexDigit);
}

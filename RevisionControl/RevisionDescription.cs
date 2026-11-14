namespace RevisionControl;

/// <summary>
/// The one sentence both systems describe a revision with, for the repository header's tooltip:
/// "message (by author on 2026-11-02)".
/// </summary>
/// <remarks>
/// <para><b>The day is this machine's.</b> SVN gave the day in UTC and Git in the zone the author
/// recorded, so the same commit could be one day here and another in the History dialog, which shows
/// dates in local time because that is the zone its date filter's days are in: a commit at 00:30
/// +01:00 on 2 November was "1 November" from SVN and "2 November" from Git to a user on UTC, who
/// sees it in the History dialog on the 1st.</para>
/// </remarks>
internal static class RevisionDescription
{
    internal static string Of(string message, string author, DateTimeOffset when) =>
        $"{message} (by {author} on {when.ToLocalTime():yyyy-MM-dd})";
}

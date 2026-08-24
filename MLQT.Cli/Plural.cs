namespace MLQT.Cli;

/// <summary>
/// Counting nouns for console output. "Entry" is spelled out rather than left as "finding(s)" because
/// a baseline holds entries, not findings, and the two counts legitimately differ — printing the wrong
/// noun makes the difference look like a bug.
/// </summary>
internal static class Plural
{
    public static string Entries(int count) => count == 1 ? "1 entry" : $"{count} entries";
}

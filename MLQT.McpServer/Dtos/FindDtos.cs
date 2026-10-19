namespace MLQT.McpServer.Dtos;

/// <summary>A line of code that <c>mlqt_find</c> with <c>match: code</c> found: the innermost class holding
/// it, its line within that class, and its file and line in the file when those are known.</summary>
public sealed record CodeSearchItem(string ClassId, int Line, string? FilePath, int? FileLine, string Text);

/// <summary>The lines found, a page at a time: <c>Total</c> counts them all, <c>Truncated</c> says more
/// follow the page.</summary>
public sealed record CodeSearchResult(
    string Query, int Total, int Offset, int Returned, bool Truncated, IReadOnlyList<CodeSearchItem> Items);

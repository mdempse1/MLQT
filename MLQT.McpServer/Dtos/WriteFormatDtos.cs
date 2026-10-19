namespace MLQT.McpServer.Dtos;

/// <summary>One class for <c>mlqt_write_class</c>: its whole source, and the package it goes in.</summary>
public sealed class ClassToWrite
{
    public string ParentId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool? Standalone { get; set; }
}

/// <summary>A class <c>mlqt_write_class</c> wrote: created or replaced, and where.</summary>
public sealed record WrittenClassItem(string ClassId, string Action, string FilePath, string? NewContent);

/// <summary>The classes <c>mlqt_write_class</c> wrote, in order.</summary>
public sealed record WriteClassResult(bool PreviewOnly, IReadOnlyList<WrittenClassItem> Written);

/// <summary>A file <c>mlqt_format</c> could not format, and why.</summary>
public sealed record FormatFailureItem(string ClassId, string Error);

/// <summary>A whole library formatted by <c>mlqt_format</c>: how many files changed, which, and the ones that
/// could not be formatted.</summary>
public sealed record LibraryFormatResult(
    string Library,
    bool PreviewOnly,
    int Files,
    int Changed,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<FormatFailureItem> Failures);

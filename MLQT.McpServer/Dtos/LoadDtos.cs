namespace MLQT.McpServer.Dtos;

/// <summary>A library <c>mlqt_load</c> has loaded or found loaded: how it came in (<c>Origin</c>:
/// explicit or search), where it was found (<c>FoundBy</c>), by which version rule, and for which
/// library's <c>uses</c>.</summary>
public sealed record LoadedLibraryItem(
    string Name, string? Version, string Path, string Origin, string FoundBy, string? Rule, string? RequiredBy,
    bool Encrypted);

/// <summary>A library an explicit load replaced: the copy that was there, and the one in its place.</summary>
public sealed record ReplacedLibraryItem(string Name, string? OldVersion, string OldPath, string? NewVersion, string NewPath);

/// <summary>A copy of a library seen during the search, whether or not it was taken.</summary>
public sealed record CandidateItem(string Path, string? Version);

/// <summary>A dependency the search could not satisfy, with every copy of it the search saw.</summary>
public sealed record MissingDependency(string Name, string? Version, string RequiredBy, IReadOnlyList<CandidateItem> Candidates);

/// <summary>
/// What <c>mlqt_load</c> did: the libraries it loaded, in order, and those already loaded that a
/// dependency was satisfied by; what an explicit load replaced; what it could not find; and the tool
/// folders it searched.
/// </summary>
public sealed record LoadResult(
    IReadOnlyList<LoadedLibraryItem> Loaded,
    IReadOnlyList<ReplacedLibraryItem> Replaced,
    IReadOnlyList<MissingDependency> NotFound,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? ToolInstall,
    IReadOnlyList<string> ToolFolders);

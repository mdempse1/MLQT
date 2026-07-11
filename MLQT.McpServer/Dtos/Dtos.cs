namespace MLQT.McpServer.Dtos;

/// <summary>Trimmed, serialization-friendly projections of the MLQT domain types.
/// These deliberately omit UI-only fields (icon SVG, VCS status flags, MudBlazor tree data,
/// force-directed layout coordinates) so tool output stays compact and stable.</summary>

public sealed record LibrarySummary(
    string Id,
    string Name,
    string SourceType,
    string SourcePath,
    int ModelCount,
    int TopLevelModelCount,
    string? RepositoryId,
    string? Revision);

public sealed record RepositorySummary(
    string Id,
    string Name,
    string VcsType,
    string LocalPath,
    string VcsRootPath,
    string? CurrentBranch,
    string? CurrentRevision,
    bool IsLoaded,
    int LibraryCount);

public sealed record DiscoveredLibrarySummary(
    string LibraryName,
    string RelativePath,
    string FullPath);

public sealed record LoadRepositoryResult(
    bool Success,
    string? RepositoryId,
    string? Name,
    string? VcsType,
    string? ErrorMessage,
    IReadOnlyList<DiscoveredLibrarySummary> DiscoveredLibraries,
    IReadOnlyList<LibrarySummary> LoadedLibraries,
    IReadOnlyList<string> Warnings);

public sealed record ClassInfo(
    string Id,
    string Name,
    string ClassType,
    bool IsPartial,
    bool IsPackage,
    string? ElementPrefix,
    string? ParentModelName,
    bool IsNested,
    string? FilePath,
    int StartLine,
    int StopLine,
    string? Version,
    IReadOnlyDictionary<string, string>? Uses,
    bool HasExperimentAnnotation,
    bool CanBeStoredStandalone,
    bool HasParserErrors,
    bool HasFatalParseFailure,
    bool? Writable);

public sealed record ClassSourceResult(
    string Id,
    string ClassType,
    bool AnnotationsIncluded,
    string Source);

public sealed record ClassListItem(
    string Id,
    string Name,
    string ClassType,
    string? ParentModelName);

public sealed record ClassListResult(
    int Total,
    int Offset,
    int Count,
    IReadOnlyList<ClassListItem> Items);

public sealed record PackageTreeNode(
    string Id,
    string Name,
    string ClassType,
    int ChildCount,
    IReadOnlyList<PackageTreeNode>? Children);

/// <summary>Uniform error payload returned by tools when a request cannot be satisfied
/// (e.g. no library loaded, class id not found). Tools return this instead of throwing so the
/// consuming agent gets an actionable message rather than a transport-level error.</summary>
public sealed record ToolError(string Error);

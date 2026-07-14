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
    string? Revision,
    // Libraries this one declares it depends on (from its `uses(...)` annotation), with the version it was
    // written against. Load these too (they are NOT loaded automatically) so type references resolve and
    // the views / validation / connector checks work across the whole model. Empty if none are declared.
    IReadOnlyList<LibraryDependency> Dependencies);

/// <summary>A dependency a library declares via its <c>uses</c> annotation, e.g. Modelica 4.0.0.</summary>
public sealed record LibraryDependency(string Name, string? Version);

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

/// <summary>Result of the reload tool: what scope was re-read from disk.</summary>
public sealed record ReloadResult(
    string Scope,
    IReadOnlyList<string> ReloadedLibraries,
    int AffectedModelCount,
    string? Note);

/// <summary>Result of create_library: the new top-level library's name, on-disk path and whether it was
/// loaded into the session (with its id).</summary>
public sealed record CreateLibraryResult(
    string Name,
    string Path,
    string? LibraryId,
    bool Loaded,
    bool PreviewOnly,
    string? PackageContent);

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

/// <summary>A search hit enriched with the class's description and a short documentation snippet, so the
/// caller can judge relevance (e.g. "use this for kinematic loops") without a follow-up call per result.</summary>
public sealed record ClassSearchItem(
    string Id,
    string Name,
    string ClassType,
    string? ParentModelName,
    string? Description,
    string? DocSnippet);

public sealed record ClassSearchResult(
    int Total,
    int Count,
    IReadOnlyList<ClassSearchItem> Items);

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

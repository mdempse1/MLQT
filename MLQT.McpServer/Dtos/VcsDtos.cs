namespace MLQT.McpServer.Dtos;

public sealed record ChangedFileDto(
    string Path,
    string FullPath,
    string Status,
    IReadOnlyList<string> ClassIds);

public sealed record ChangedClassesResult(
    string RepositoryId,
    string Revision,
    int ChangedFileCount,
    int ChangedClassCount,
    IReadOnlyList<ChangedFileDto> Files,
    IReadOnlyList<string> ClassIds);

public sealed record ChangeImpactResult(
    string RepositoryId,
    string Revision,
    bool DependenciesAnalyzed,
    IReadOnlyList<string> ChangedClasses,
    int ImpactedModelsCount,
    int Returned,
    bool Truncated,
    IReadOnlyList<ImpactDetailDto> ImpactDetails);

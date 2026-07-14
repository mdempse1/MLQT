namespace MLQT.McpServer.Dtos;

public sealed record ClassRef(string Id, string Name, string ClassType);

public sealed record DependencyResult(
    string ClassId,
    bool DependenciesAnalyzed,
    int Count,
    IReadOnlyList<ClassRef> Items);

public sealed record AnalyzeDependenciesResult(
    int Models,
    int DependencyEdges,
    int Resources,
    int ResourceWarnings,
    long ElapsedMs);

public sealed record ImpactDetailDto(
    string ModelId,
    string ClassType,
    IReadOnlyList<string> ImpactedBy);

public sealed record ImpactResult(
    IReadOnlyList<string> ClassIds,
    bool DependenciesAnalyzed,
    int ImpactedModelsCount,
    int Returned,
    bool Truncated,
    IReadOnlyList<ImpactDetailDto> ImpactDetails);

public sealed record ResourceRefDto(
    string ModelId,
    string RawPath,
    string? ResolvedPath,
    string ReferenceType,
    string? ParameterName,
    bool IsAbsolutePath,
    bool FileExists,
    bool IsImageFile,
    bool IsDirectory);

public sealed record ClassResourcesResult(
    string ClassId,
    bool ResourcesAnalyzed,
    int Count,
    IReadOnlyList<ResourceRefDto> Resources);

public sealed record ResourceWarningDto(
    string ModelId,
    string ResourcePath,
    string WarningType,
    string Message);

public sealed record ResourceWarningsResult(
    int Total,
    bool ResourcesAnalyzed,
    int Offset,
    int Count,
    IReadOnlyList<ResourceWarningDto> Warnings);

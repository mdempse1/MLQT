using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// External-resource queries: files and directories referenced by models via loadResource,
/// modelica:// URIs, Bitmap, and external-function annotations. Requires analyze_dependencies to
/// have been run (it populates the resource graph and validation warnings).
/// </summary>
[McpServerToolType]
public sealed class ResourceTools
{
    private const int MaxWarningLimit = 1000;

    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public ResourceTools(
        ILibraryDataService libraries,
        IExternalResourceService resources,
        SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "get_class_resources")]
    [Description("List the external resources (data files, C sources/libraries, images, directories) " +
                "referenced by a class, each with its raw path, resolved absolute path, reference type, " +
                "and whether the file exists. Requires analyze_dependencies.")]
    public object GetClassResources(
        [Description("Fully-qualified class id.")] string classId)
    {
        if (_libraries.GetModelById(classId) is null)
            return NotFound(classId);

        var resources = _resources.GetResourcesForModel(classId).Select(ToDto).ToList();
        return new ClassResourcesResult(classId, _session.ResourcesAnalyzed, resources.Count, resources);
    }

    [McpServerTool(Name = "find_resource_usages")]
    [Description("Reverse lookup: given a resolved absolute file path, list the class ids that reference " +
                "that resource. Requires analyze_dependencies. Get resolved paths from get_class_resources " +
                "or get_resource_warnings.")]
    public object FindResourceUsages(
        [Description("Resolved absolute file system path of the resource.")] string resolvedFilePath)
    {
        if (string.IsNullOrWhiteSpace(resolvedFilePath))
            return new ToolError("resolvedFilePath must be a non-empty path.");

        var models = _resources.GetModelsReferencingResource(resolvedFilePath);
        return new { resolvedFilePath, resourcesAnalyzed = _session.ResourcesAnalyzed, count = models.Count, models };
    }

    [McpServerTool(Name = "get_resource_warnings")]
    [Description("List external-resource validation warnings across all loaded models: referenced files " +
                "that don't exist, and non-portable absolute-path references. Requires analyze_dependencies. " +
                "Paginated with limit/offset.")]
    public object GetResourceWarnings(
        [Description("Max warnings to return (default 200, max 1000).")] int limit = 200,
        [Description("Warnings to skip for pagination (default 0).")] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, MaxWarningLimit);
        offset = Math.Max(offset, 0);

        var all = _resources.GetWarnings();
        var page = all.Skip(offset).Take(limit)
            .Select(w => new ResourceWarningDto(w.ModelId, w.ResourcePath, w.WarningType.ToString(), w.Message))
            .ToList();

        return new ResourceWarningsResult(all.Count, _session.ResourcesAnalyzed, offset, page.Count, page);
    }

    private static ResourceRefDto ToDto(ExternalResourceReference r) => new(
        r.ModelId, r.RawPath, r.ResolvedPath, r.ReferenceType.ToString(), r.ParameterName,
        r.IsAbsolutePath, r.FileExists, r.IsImageFile, r.IsDirectory);

    private static ToolError NotFound(string classId) =>
        new($"No class with id '{classId}'. Ensure a library is loaded and the id is fully-qualified; " +
            "use search_classes to find it.");
}

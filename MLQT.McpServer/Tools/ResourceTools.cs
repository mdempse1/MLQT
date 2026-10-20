using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// External-resource queries: files and directories referenced by models via loadResource,
/// modelica:// URIs, Bitmap, and external-function annotations. Requires mlqt_dependencies to
/// have been run (it populates the resource graph and validation warnings).
/// </summary>
[Obfuscation]
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

    internal object GetClassResources(
        string classId)
    {
        if (_libraries.GetModelById(classId) is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (!_session.ResourcesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "listing a class's external resources");

        var resources = _resources.GetResourcesForModel(classId).Select(ToDto).ToList();
        return new ClassResourcesResult(classId, _session.ResourcesAnalyzed, resources.Count, resources);
    }

    internal object FindResourceUsages(
        string resolvedFilePath)
    {
        if (string.IsNullOrWhiteSpace(resolvedFilePath))
            return new ToolError("resolvedFilePath must be a non-empty path.");
        if (!_session.ResourcesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "looking up which classes reference a resource");

        var models = _resources.GetModelsReferencingResource(resolvedFilePath);
        return new ResourceUsagesResult(resolvedFilePath, _session.ResourcesAnalyzed, models.Count, models);
    }

    internal object GetResourceWarnings(
        int limit = 25,
        int offset = 0)
    {
        if (!_session.ResourcesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries, "listing external-resource warnings");

        limit = Math.Clamp(limit, 1, MaxWarningLimit);
        offset = Math.Max(offset, 0);

        var all = _resources.GetWarnings();
        var page = all.Skip(offset).Take(limit)
            .Select(w => new ResourceWarningDto(w.ModelId, w.ResourcePath, WireNames.Of(w.WarningType), w.Message))
            .ToList();

        return new ResourceWarningsResult(all.Count, _session.ResourcesAnalyzed, offset, page.Count, page);
    }

    private static ResourceRefDto ToDto(ExternalResourceReference r) => new(
        r.ModelId, r.RawPath, r.ResolvedPath, WireNames.Of(r.ReferenceType), r.ParameterName,
        r.IsAbsolutePath, r.FileExists, r.IsImageFile, r.IsDirectory);
}

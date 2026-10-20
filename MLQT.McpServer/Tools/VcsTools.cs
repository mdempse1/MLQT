using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;
using RevisionControl;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Modelica-aware VCS tools. These are the only version-control tools the server exposes: they
/// bridge VCS file changes to the Modelica semantic graph — something a plain git/svn client cannot
/// do. Generic VCS operations (commit, log, push, branch) are intentionally left to the CLI. Both
/// tools are read-only.
/// </summary>
[Obfuscation]
public sealed class VcsTools
{
    private const int MaxImpactLimit = 2000;

    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly IImpactAnalysisService _impact;
    private readonly SessionState _session;

    public VcsTools(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        IImpactAnalysisService impact,
        SessionState session)
    {
        _libraries = libraries;
        _repositories = repositories;
        _impact = impact;
        _session = session;
    }

    internal object GetChangedClasses(
        string repositoryId,
        string? revision = null)
    {
        var (repo, repoError) = EntityResolver.ResolveRepository(_repositories, repositoryId);
        if (repoError is not null)
            return repoError;
        if (ToolDiagnostics.RequireLibrary(_libraries,
                "mapping changed files to classes (mlqt_load, given the working copy, loads its libraries)") is { } noLib)
            return noLib;

        var files = ResolveChangedFiles(repo!, revision);
        var allClassIds = files.SelectMany(f => f.ClassIds).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

        return new ChangedClassesResult(
            repositoryId, revision ?? "workingCopy", files.Count, allClassIds.Count, files, allClassIds);
    }

    internal object AnalyzeChangeImpact(
        string repositoryId,
        string? revision = null,
        int limit = 100,
        int offset = 0)
    {
        var (repo, repoError) = EntityResolver.ResolveRepository(_repositories, repositoryId);
        if (repoError is not null)
            return repoError;
        if (!_session.DependenciesAnalyzed)
            return ToolDiagnostics.NotAnalyzed(_libraries,
                "analysing change impact");

        limit = Math.Clamp(limit, 1, MaxImpactLimit);
        offset = Math.Max(offset, 0);

        var changedClasses = ResolveChangedFiles(repo!, revision)
            .SelectMany(f => f.ClassIds).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

        if (changedClasses.Count == 0)
        {
            return new ChangeImpactResult(
                repositoryId, revision ?? "workingCopy", _session.DependenciesAnalyzed,
                changedClasses, 0, 0, false, []);
        }

        var result = _impact.AnalyzeImpact(_libraries.CombinedGraph, changedClasses);
        var ordered = result.ImpactDetails.OrderBy(d => d.ModelId, StringComparer.Ordinal).ToList();
        var page = ordered.Skip(offset).Take(limit)
            .Select(d => new ImpactDetailDto(d.ModelId, d.ClassType, d.ImpactedBy))
            .ToList();

        return new ChangeImpactResult(
            repositoryId, revision ?? "workingCopy", _session.DependenciesAnalyzed,
            changedClasses, result.ImpactedModelsCount, page.Count,
            ordered.Count > offset + page.Count, page);
    }

    /// <summary>Resolve changed .mo files (working copy or a revision) to (path, status, class ids).
    /// Matching is done on canonicalized full paths (Path.GetFullPath) rather than raw strings, so a
    /// mix of '/' and '\' separators between the VCS root and the loaded file paths still lines up.</summary>
    private List<ChangedFileDto> ResolveChangedFiles(Repository repo, string? revision)
    {
        var graph = _libraries.CombinedGraph;

        // Canonicalized file path -> containing FileNode, for the currently-loaded files.
        var byPath = new Dictionary<string, FileNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in graph.FileNodes)
        {
            if (!string.IsNullOrEmpty(fn.FilePath))
                byPath[Canon(fn.FilePath)] = fn;
        }

        var vcsRoot = string.IsNullOrEmpty(repo.VcsRootPath) ? repo.LocalPath : repo.VcsRootPath;
        var localCanon = string.IsNullOrEmpty(repo.LocalPath) ? null : Canon(repo.LocalPath);

        IEnumerable<(string Path, string Status)> changes = revision is null
            ? _repositories.GetWorkingCopyChanges(repo.Id).Select(c => (c.Path, WireNames.Of(c.Status)))
            : _repositories.GetChangedFiles(repo.Id, revision).Select(c => (c.Path, WireNames.Of(c.ChangeType)));

        var files = new List<ChangedFileDto>();
        foreach (var (relPath, status) in changes)
        {
            if (!relPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase))
                continue;

            var canon = Canon(Path.Combine(vcsRoot, relPath));
            // Separator-aware: …/Lib does not contain …/LibExtra (B323).
            if (localCanon is not null && !PathContainment.IsWithin(canon, localCanon))
                continue;

            var classIds = byPath.TryGetValue(canon, out var fileNode)
                ? fileNode.ContainedModelIds.OrderBy(id => id, StringComparer.Ordinal).ToList()
                : [];

            files.Add(new ChangedFileDto(relPath, canon, status, classIds));
        }

        return files;
    }

    private static string Canon(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }
}

using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;

namespace MLQT.Cli;

/// <summary>Outcome of loading + checking a library: sorted findings and the model→file map.</summary>
internal sealed record LoadResult(
    int ExitCode,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, string> ModelToFile,
    int ModelsChecked)
{
    public bool Ok => ExitCode == ExitCodes.Ok;
    public static LoadResult Failed(int code) => new(code, [], new Dictionary<string, string>(), 0);
}

/// <summary>Shared load + check pipeline used by both `check` and the `baseline` commands.</summary>
internal static class CheckPipeline
{
    public static async Task<LoadResult> LoadAndCheckAsync(
        string libraryPath, string? configPath, TextWriter stderr, bool honorSuppressions = true)
    {
        var isDir = Directory.Exists(libraryPath);
        var isMoFile = File.Exists(libraryPath) &&
                       libraryPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase);
        if (!isDir && !isMoFile)
        {
            stderr.WriteLine($"error: library path not found: {libraryPath}");
            return LoadResult.Failed(ExitCodes.Error);
        }

        StyleCheckingSettings settings;
        try
        {
            settings = SettingsResolver.Resolve(libraryPath, configPath, out var source);
            stderr.WriteLine($"note: settings from {source}");
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return LoadResult.Failed(ExitCodes.Error);
        }

        if (!settings.HasAnyStyleRuleEnabled)
            stderr.WriteLine("note: no style rules are enabled; no findings will be produced.");

        // Load every library in the repository (matching the UI: one .mlqt/settings.json at the
        // repository root applies to all libraries found under it).
        var libraryPaths = LibraryDiscovery.DiscoverLibraryPaths(libraryPath);
        if (libraryPaths.Count == 0)
        {
            stderr.WriteLine(
                $"error: no Modelica libraries found under {libraryPath} " +
                "(expected a package.mo, sub-package directories, or .mo files)");
            return LoadResult.Failed(ExitCodes.Error);
        }

        var libraryData = new LibraryDataService();
        var models = new List<ModelNode>();
        var seenModelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in libraryPaths)
        {
            LoadedLibrary library;
            try
            {
                library = await libraryData.AddLibraryFromDirectoryAsync(path);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"warning: failed to load '{path}': {ex.Message}");
                continue;
            }

            foreach (var id in library.ModelIds)
            {
                if (!seenModelIds.Add(id))
                    continue;
                var model = libraryData.GetModelById(id);
                if (model is not null && !model.IsParseFailurePlaceholder)
                    models.Add(model);
            }
        }

        var graph = libraryData.CombinedGraph;

        // Some graph analyses (uses hygiene, unused classes) rely on cross-model dependency edges,
        // which the load path does not populate. Run dependency analysis once, only when such a rule
        // is enabled, so a plain style-check run doesn't pay for it.
        var dependenciesAnalyzed = ModelicaGraph.Analysis.GraphAnalysisRunner.RequiresDependencyAnalysis(settings);
        if (dependenciesAnalyzed)
        {
            stderr.WriteLine("note: running dependency analysis (required by an enabled rule)…");
            await ModelicaGraph.GraphBuilder.AnalyzeDependenciesAsync(graph);
        }

        var customDictionary = new CustomDictionaryService();
        var dictionaryManager = new DictionaryManagerService();

        var findings = LibraryCheckSession
            .Check(graph, models, settings, customDictionary, dictionaryManager, honorSuppressions, dependenciesAnalyzed)
            .OrderBy(f => f.ModelId, StringComparer.Ordinal)
            .ThenBy(f => f.LineNumber)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.ElementPath ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
            foreach (var model in graph.GetModelsInFile(file.Id))
                modelToFile[model.Id] = file.FilePath;

        return new LoadResult(ExitCodes.Ok, findings, modelToFile, models.Count);
    }
}

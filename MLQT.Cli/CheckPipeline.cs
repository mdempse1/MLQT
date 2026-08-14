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
    public static async Task<LoadResult> LoadAndCheckAsync(string libraryPath, string? configPath, TextWriter stderr)
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

        var libraryData = new LibraryDataService();
        LoadedLibrary library;
        try
        {
            library = isDir
                ? await libraryData.AddLibraryFromDirectoryAsync(libraryPath)
                : await libraryData.AddLibraryFromFileAsync(libraryPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: failed to load library: {ex.Message}");
            return LoadResult.Failed(ExitCodes.Error);
        }

        var graph = libraryData.CombinedGraph;
        var models = library.ModelIds
            .Select(libraryData.GetModelById)
            .Where(m => m is not null && !m!.IsParseFailurePlaceholder)
            .Cast<ModelNode>()
            .ToList();

        var customDictionary = new CustomDictionaryService();
        var dictionaryManager = new DictionaryManagerService();

        var findings = LibraryCheckSession
            .Check(graph, models, settings, customDictionary, dictionaryManager)
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

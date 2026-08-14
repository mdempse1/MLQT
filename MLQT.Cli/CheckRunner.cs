using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;

namespace MLQT.Cli;

/// <summary>Loads a Modelica library, runs the findings pipeline, formats, and computes the exit code.</summary>
internal static class CheckRunner
{
    public static async Task<int> RunAsync(CheckOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var isDir = Directory.Exists(opts.LibraryPath);
        var isMoFile = File.Exists(opts.LibraryPath) &&
                       opts.LibraryPath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase);
        if (!isDir && !isMoFile)
        {
            stderr.WriteLine($"error: library path not found: {opts.LibraryPath}");
            return ExitCodes.Error;
        }

        StyleCheckingSettings settings;
        try
        {
            settings = SettingsResolver.Resolve(opts.LibraryPath, opts.ConfigPath, out var source);
            stderr.WriteLine($"note: settings from {source}");
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Error;
        }

        if (!settings.HasAnyStyleRuleEnabled)
            stderr.WriteLine("note: no style rules are enabled; no findings will be produced.");

        var libraryData = new LibraryDataService();
        LoadedLibrary library;
        try
        {
            library = isDir
                ? await libraryData.AddLibraryFromDirectoryAsync(opts.LibraryPath)
                : await libraryData.AddLibraryFromFileAsync(opts.LibraryPath);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: failed to load library: {ex.Message}");
            return ExitCodes.Error;
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

        // Build the model id -> source file map for the file/classname fields.
        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
            foreach (var model in graph.GetModelsInFile(file.Id))
                modelToFile[model.Id] = file.FilePath;

        var report = new CheckReport(opts.LibraryPath, models.Count, findings, modelToFile);

        IFindingFormatter formatter = opts.Format switch
        {
            OutputFormat.Json => new JsonFindingFormatter(),
            OutputFormat.JUnit => new JUnitFindingFormatter(),
            _ => new ConsoleFindingFormatter(
                useColor: !opts.NoColor && opts.OutPath is null && !Console.IsOutputRedirected)
        };
        var output = formatter.Format(report);

        if (opts.OutPath is not null)
        {
            try
            {
                await File.WriteAllTextAsync(opts.OutPath, output);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: failed to write '{opts.OutPath}': {ex.Message}");
                return ExitCodes.Error;
            }
        }
        else
        {
            await stdout.WriteAsync(output);
            if (!output.EndsWith('\n'))
                await stdout.WriteLineAsync();
        }

        var failCount = opts.FailOn == FailOnLevel.Off
            ? 0
            : findings.Count(f => (int)f.Severity >= (int)ThresholdFor(opts.FailOn));

        return failCount > 0 ? ExitCodes.GateFailed : ExitCodes.Ok;
    }

    private static RuleSeverity ThresholdFor(FailOnLevel level) => level switch
    {
        FailOnLevel.Warning => RuleSeverity.Warning,
        _ => RuleSeverity.Error
    };
}

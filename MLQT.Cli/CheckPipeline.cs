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
    int ModelsChecked,
    DirectedGraph? Graph = null,
    IReadOnlyList<ModelNode>? Models = null,
    StyleCheckingSettings? Settings = null,
    IReadOnlyList<string>? DependencyLibraries = null)
{
    public bool Ok => ExitCode == ExitCodes.Ok;
    public static LoadResult Failed(int code) => new(code, [], new Dictionary<string, string>(), 0);
}

/// <summary>Shared load + check pipeline used by both `check` and the `baseline` commands.</summary>
internal static class CheckPipeline
{
    public static async Task<LoadResult> LoadAndCheckAsync(
        string libraryPath, string? configPath, TextWriter stderr, bool honorSuppressions = true,
        IReadOnlyList<string>? dependencyPaths = null)
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
        if (settings.ExcludedLibraries.Count > 0)
            stderr.WriteLine($"note: excluding {string.Join(", ", settings.ExcludedLibraries)} from the checks");

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
                // Placeholders (files that failed to parse outright) are included deliberately.
                // LibraryCheckSession skips them for the per-class rules and the graph analyses, but it
                // needs to see them to report the parse failure — filtering them here is what used to
                // make the worst case, a file MLQT could not read at all, the one thing CI never heard
                // about.
                if (model is not null)
                    models.Add(model);
            }
        }

        // Dependencies are loaded into the SAME graph so references resolve — an inherited icon from
        // Modelica.Icons.*, a modelica:// link into MSL, a type the code extends — but they are kept
        // out of `models`, which is the reported set. Their own findings are not the user's problem.
        var dependencyLibraries = new List<string>();
        foreach (var dependency in dependencyPaths ?? [])
        {
            if (!Directory.Exists(dependency) && !File.Exists(dependency))
            {
                stderr.WriteLine($"error: dependency path not found: {dependency}");
                return LoadResult.Failed(ExitCodes.Error);
            }

            foreach (var path in LibraryDiscovery.DiscoverLibraryPaths(dependency))
            {
                try
                {
                    var library = await libraryData.AddLibraryFromDirectoryAsync(path);
                    dependencyLibraries.Add(library.Name);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"warning: failed to load dependency '{path}': {ex.Message}");
                }
            }
        }
        if (dependencyLibraries.Count > 0)
            stderr.WriteLine(
                $"note: loaded {string.Join(", ", dependencyLibraries)} for reference resolution " +
                "(not reported on)");

        var graph = libraryData.CombinedGraph;

        // Match the GUI: trim each package's inline standalone children out of its stored source before
        // checking (they have their own nodes), so the CLI checks the same representation the app does
        // and reports the same findings with the same line numbers.
        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        // Some graph analyses (uses hygiene, unused classes) rely on cross-model dependency edges,
        // which the load path does not populate. Run dependency analysis once, only when such a rule
        // is enabled, so a plain style-check run doesn't pay for it.
        if (ModelicaGraph.Analysis.GraphAnalysisRunner.RequiresDependencyAnalysis(settings))
        {
            stderr.WriteLine("note: running dependency analysis (required by an enabled rule)…");
            // Pass the library roots so modelica:// URIs resolve against every loaded library, not
            // just the one under check.
            await ModelicaGraph.GraphBuilder.AnalyzeDependenciesAsync(graph, libraryData.GetLibraryInfos());
        }

        var customDictionary = new CustomDictionaryService();
        var dictionaryManager = new DictionaryManagerService();

        // Whether the edges are present is read off the graph itself, so this can't disagree with what
        // the GUI and MCP see for the same library.
        var findings = LibraryCheckSession
            .Check(graph, models, settings, customDictionary, dictionaryManager, honorSuppressions)
            .OrderBy(f => f.ModelId, StringComparer.Ordinal)
            .ThenBy(f => f.LineNumber)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.ElementPath ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
            foreach (var model in graph.GetModelsInFile(file.Id))
                modelToFile[model.Id] = file.FilePath;

        // Report the number of classes actually checked: excludes unparseable placeholders and any
        // library the settings exclude. Excluded classes are counted out loud rather than silently, so
        // a mistyped library name shows up as an unexpected number rather than as a quiet pass.
        var checkable = models.Where(m => !m.IsParseFailurePlaceholder).ToList();
        var excluded = checkable.Count(m => settings.IsLibraryExcluded(m.Id));
        if (excluded > 0)
            stderr.WriteLine($"note: {excluded} class(es) skipped as excluded libraries");
        var modelsChecked = checkable.Count - excluded;
        // The graph and model list are carried out so `--metrics` can compute coverage over exactly
        // the set that was checked, without loading the library a second time.
        return new LoadResult(
            ExitCodes.Ok, findings, modelToFile, modelsChecked, graph, models, settings, dependencyLibraries);
    }
}

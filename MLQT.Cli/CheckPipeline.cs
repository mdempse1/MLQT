using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services;
using MLQT.Services.Checking;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

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
    /// <summary>
    /// Records an encrypted library that has been loaded, and says so when it turned out to carry
    /// nothing. A library shipping no documentation contributes no classes, and saying so is the
    /// difference between a user understanding why a reference is still unresolved and assuming the
    /// check covered it.
    /// </summary>
    /// <param name="loadedNames">Collects the library's <b>bare name</b>. This list is recorded in
    /// the baseline and compared name-by-name to detect a check running against a different set of
    /// dependencies than the baseline was taken with, so nothing variable — a class count, a path —
    /// may be folded into it: that would report drift every time the vendor reissued the library.</param>
    private static void ReportEncrypted(
        LoadedLibrary library, List<string> loadedNames, TextWriter stderr)
    {
        if (library.ModelIds.Count == 0)
        {
            stderr.WriteLine(
                $"warning: encrypted library '{library.Name}' ships no usable documentation, so its " +
                "classes cannot be recovered; references into it stay unresolved");
            return;
        }

        // No per-library note on success: with a tool's library folder loaded that is fifty lines of
        // scrollback saying nothing actionable. The libraries that loaded are named once in the
        // reference-resolution summary, and the ones that could not are warned about individually.
        loadedNames.Add(library.Name);
    }

    /// <summary>
    /// Warns when the settings select a spell-check language this machine has no dictionary for.
    /// </summary>
    private static void WarnAboutMissingDictionaries(
        StyleCheckingSettings settings, IDictionaryManagerService dictionaryManager, TextWriter stderr)
    {
        if (!settings.SpellCheckDescription && !settings.SpellCheckDocumentation)
            return;

        var requested = settings.SpellCheckLanguages;
        if (requested is null || requested.Count == 0)
            return;

        var available = dictionaryManager.GetAvailableDictionaries()
            .Select(d => d.LanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = requested.Where(code => !available.Contains(code)).ToList();
        if (missing.Count == 0)
            return;

        stderr.WriteLine(
            $"warning: no spell-check dictionary installed for {string.Join(", ", missing)}; " +
            "those words are checked against the remaining languages, so the spelling findings " +
            "will not match a machine that has them");
    }

    public static async Task<LoadResult> LoadAndCheckAsync(
        string libraryPath, string? configPath, TextWriter stderr, bool honorSuppressions = true,
        IReadOnlyList<string>? dependencyPaths = null, bool allowVersionMismatch = false)
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
        string dictionaryRoot;
        try
        {
            var resolved = SettingsResolver.Resolve(libraryPath, configPath);
            settings = resolved.Settings;
            dictionaryRoot = resolved.DictionaryRoot;
            stderr.WriteLine($"note: settings from {resolved.Source}");
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
        var referenceLibraries = new List<string>();
        foreach (var path in libraryPaths)
        {
            LoadedLibrary library;
            try
            {
                library = await libraryData.AddLibraryFromPathAsync(path);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"warning: failed to load '{path}': {ex.Message}");
                continue;
            }

            // An encrypted library found inside the repository is loaded for reference but never
            // checked. There is no source in it to have an opinion about — only classes rebuilt
            // from the vendor's documentation — so reporting on it would be reporting on our own
            // reconstruction.
            if (library.SourceType == LibrarySourceType.EncryptedDirectory)
            {
                ReportEncrypted(library, referenceLibraries, stderr);
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
                    var library = await libraryData.AddLibraryFromPathAsync(path);
                    if (library.SourceType == LibrarySourceType.EncryptedDirectory)
                        ReportEncrypted(library, dependencyLibraries, stderr);
                    else
                        dependencyLibraries.Add(library.Name);
                }
                catch (Exception ex)
                {
                    stderr.WriteLine($"warning: failed to load dependency '{path}': {ex.Message}");
                }
            }
        }

        dependencyLibraries.AddRange(referenceLibraries);
        if (dependencyLibraries.Count > 0)
            stderr.WriteLine(
                $"note: loaded {string.Join(", ", dependencyLibraries)} for reference resolution " +
                "(not reported on)");

        var graph = libraryData.CombinedGraph;

        // Some graph analyses (uses hygiene, unused classes) rely on cross-model dependency edges,
        // which the load path does not populate. Run dependency analysis once, only when such a rule
        // is enabled, so a plain style-check run doesn't pay for it.
        var needsDependencyAnalysis =
            ModelicaGraph.Analysis.GraphAnalysisRunner.RequiresDependencyAnalysis(settings);

        // Match the GUI: trim each package's inline standalone children out of its stored source before
        // checking (they have their own nodes), so the CLI checks the same representation the app does
        // and reports the same findings with the same line numbers.
        //
        // Trimming rewrites a package's stored source, so it is worth doing exactly for the packages
        // whose source will be read again — and no others. The reported models always qualify. Every
        // other loaded package qualifies only if dependency analysis is going to parse it, in which
        // case trimming pays for itself several times over by shrinking what that parse sees; when no
        // analysis needs the edges, trimming a tool's whole library folder is pure cost for an
        // output that cannot change.
        PackageCodeTrimmer.TrimStandaloneChildren(
            graph,
            needsDependencyAnalysis ? null : models.Select(m => m.Id).ToHashSet(StringComparer.Ordinal));

        if (needsDependencyAnalysis)
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
        // Language dictionaries are installed per machine, not committed with the library, so a CI
        // runner can easily lack one the settings ask for. Hunspell would then quietly fall back and
        // spell-check, say, French prose against an English dictionary — a wrong answer that looks
        // like a real finding. Say so instead.
        WarnAboutMissingDictionaries(settings, dictionaryManager, stderr);

        // The accepted spellings live beside the settings that were resolved for this run, so CI reads
        // the same list a developer's app does even when the settings cover several libraries.
        var findings = LibraryCheckSession
            .Check(graph, models, settings, customDictionary, dictionaryManager, honorSuppressions,
                   dependenciesAnalyzed: null, repositoryRoot: dictionaryRoot)
            .OrderBy(f => f.ModelId, StringComparer.Ordinal)
            .ThenBy(f => f.LineNumber)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.ElementPath ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
            foreach (var model in graph.GetModelsInFile(file.Id))
                modelToFile[model.Id] = file.FilePath;

        // A dependency on the machine that is not the version the library targets resolves references
        // against classes that may have moved, been renamed or changed signature between versions. The
        // result is not a slightly-off check but a pile of findings that are not real, so stop rather
        // than hand back numbers nobody should act on. Exit code 2 (setup error), not 1 (gate failed):
        // in CI the two mean different things — fix your invocation vs fix your code.
        var mismatches = UsesVersionChecker.Check(graph, models);
        if (mismatches.Count > 0)
        {
            var severity = allowVersionMismatch ? "warning" : "error";
            stderr.WriteLine($"{severity}: dependency version mismatch");
            foreach (var mismatch in mismatches)
                stderr.WriteLine($"       {mismatch.Describe()}");

            if (!allowVersionMismatch)
            {
                stderr.WriteLine(
                    "       Checking against the wrong version reports findings that are not real, so " +
                    "this check has been stopped.");
                stderr.WriteLine(
                    "       Point --dependency at the declared versions, or update the uses(...) " +
                    "annotation to match what you have.");
                stderr.WriteLine(
                    "       If the difference is deliberate (a conversion(noneFromVersion=...) covers " +
                    "it, say), pass --allow-version-mismatch.");
                return LoadResult.Failed(ExitCodes.Error);
            }

            stderr.WriteLine(
                "       Continuing because --allow-version-mismatch was given; findings may not be real.");
        }

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

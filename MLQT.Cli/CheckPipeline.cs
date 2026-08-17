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

        // ─── TEMPORARY DIAGNOSTIC ────────────────────────────────────────────────────────────────
        // Per-rule breakdown of all findings (with a standalone/non-standalone split) plus the enabled
        // rules, so the CLI totals can be compared rule-by-rule with the GUI. Remove once resolved.
        ReportRuleDiagnostic(findings, models, settings, stderr);
        // ─────────────────────────────────────────────────────────────────────────────────────────

        var modelToFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in graph.FileNodes)
            foreach (var model in graph.GetModelsInFile(file.Id))
                modelToFile[model.Id] = file.FilePath;

        return new LoadResult(ExitCodes.Ok, findings, modelToFile, models.Count);
    }

    // TEMPORARY: per-rule breakdown of all findings, split by standalone vs non-standalone model, plus
    // the enabled rules — so the CLI can be compared rule-by-rule with the GUI to locate any divergence.
    private static void ReportRuleDiagnostic(
        IReadOnlyList<Finding> findings, IReadOnlyList<ModelNode> models, StyleCheckingSettings settings,
        TextWriter stderr)
    {
        var standaloneById = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var m in models)
            standaloneById[m.Id] = m.CanBeStoredStandalone;
        var nonStandaloneModels = models.Count(m => !m.CanBeStoredStandalone);

        // Duplicate detection: identical findings (same model+rule+line+element+discriminator).
        var distinct = findings
            .GroupBy(f => $"{f.ModelId}{f.RuleId}{f.LineNumber}{f.ElementPath}{f.Discriminator}")
            .Count();
        var dupeModelIds = findings
            .GroupBy(f => f.ModelId ?? "")
            .Where(g => g.Count() != g.Select(f => $"{f.RuleId}{f.LineNumber}{f.ElementPath}{f.Discriminator}").Distinct().Count())
            .Select(g => g.Key)
            .Take(15)
            .ToList();

        stderr.WriteLine();
        stderr.WriteLine("=== TEMP DIAGNOSTIC: findings by rule (all models) ===");
        stderr.WriteLine($"total findings: {findings.Count}    distinct findings: {distinct}    duplicates: {findings.Count - distinct}");
        var distinctModelIds = models.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count();
        stderr.WriteLine($"models checked: {models.Count}  (distinct ids: {distinctModelIds}, " +
                         $"standalone={models.Count - nonStandaloneModels}, non-standalone={nonStandaloneModels})");
        if (dupeModelIds.Count > 0)
            stderr.WriteLine("sample models with duplicate findings: " + string.Join(", ", dupeModelIds));

        var dupesByRule = findings
            .GroupBy(f => f.RuleId ?? "(none)")
            .Select(g => (rule: g.Key, dup: g.Count() - g.Select(f => $"{f.ModelId}{f.LineNumber}{f.ElementPath}{f.Discriminator}").Distinct().Count()))
            .Where(t => t.dup > 0)
            .OrderByDescending(t => t.dup)
            .ToList();
        if (dupesByRule.Count > 0)
        {
            stderr.WriteLine("duplicates by rule:");
            foreach (var t in dupesByRule)
                stderr.WriteLine($"  {t.dup,6}  {t.rule}");
        }
        stderr.WriteLine($"  {"total",8} {"standln",8} {"non-std",8}  rule");
        foreach (var g in findings.GroupBy(f => f.RuleId ?? "(none)").OrderByDescending(g => g.Count()))
        {
            var sa = g.Count(f => f.ModelId is not null && standaloneById.TryGetValue(f.ModelId, out var v) && v);
            var nsa = g.Count() - sa;
            stderr.WriteLine($"  {g.Count(),8} {sa,8} {nsa,8}  {g.Key}");
        }

        stderr.WriteLine("enabled rules (rule id → severity, from the resolved settings):");
        foreach (var kv in settings.RuleSeverities.OrderBy(k => k.Key, StringComparer.Ordinal))
            stderr.WriteLine($"  {kv.Value,-8} {kv.Key}");

        stderr.WriteLine("=== END TEMP DIAGNOSTIC ===");
        stderr.WriteLine();
    }
}

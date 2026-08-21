using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>Runs `check`: load + findings, classify against a baseline, format, compute exit code.</summary>
internal static class CheckRunner
{
    public static async Task<int> RunAsync(CheckOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var load = await CheckPipeline.LoadAndCheckAsync(
            opts.LibraryPath, opts.ConfigPath, stderr,
            honorSuppressions: !opts.NoSuppress, dependencyPaths: opts.DependencyPaths);
        if (!load.Ok)
            return load.ExitCode;

        Baseline? baseline = null;
        if (opts.BaselinePath is not null)
        {
            var baselinePath = RepoPath.Resolve(opts.LibraryPath, opts.BaselinePath);
            if (!File.Exists(baselinePath))
            {
                stderr.WriteLine($"error: baseline not found: {baselinePath}");
                return ExitCodes.Error;
            }
            try
            {
                baseline = Baseline.Load(baselinePath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: could not read baseline: {ex.Message}");
                return ExitCodes.Error;
            }
        }

        // Warn when the baseline was generated under different rules. Both failure modes are silent
        // otherwise: a rule enabled since reports its pre-existing violations as NEW, so a change looks
        // like it caused a regression it had nothing to do with; a rule disabled since leaves entries
        // that can never match again. Not fatal — the gate still means what it says, the user just
        // needs to know why the numbers moved.
        if (baseline is not null && load.Settings is not null)
        {
            var drift = baseline.DriftFrom(load.Settings, load.DependencyLibraries);
            if (drift.HasDrifted)
            {
                stderr.WriteLine("warning: the baseline was generated with a different configuration");
                foreach (var line in drift.Describe())
                    stderr.WriteLine($"         {line}");
                if (drift.EnabledSince.Count > 0)
                    stderr.WriteLine(
                        "         Pre-existing violations of a newly enabled rule are reported as new. " +
                        "`mlqt baseline update --force` would accept them.");
                if (drift.DependenciesMissing.Count > 0)
                    stderr.WriteLine(
                        "         Pass --dependency <path> for each, or references into them resolve as " +
                        "findings that the change did not cause.");
            }
            else if (!drift.IsComparable)
            {
                stderr.WriteLine(
                    "note: this baseline predates configuration recording, so changes to it cannot be " +
                    "detected — regenerate the baseline to enable that");
            }
        }

        IReadOnlySet<string>? changedModelIds = null;
        if (opts.ChangedFrom is not null)
        {
            var changed = ChangedModelResolver.Resolve(opts.LibraryPath, opts.ChangedFrom, load.ModelToFile);
            if (!changed.Ok)
            {
                stderr.WriteLine($"error: {changed.Error}");
                return ExitCodes.Error;
            }
            stderr.WriteLine(
                $"note: {changed.ChangedFileCount} changed .mo file(s), " +
                $"{changed.ChangedModelIds.Count} model(s) changed since {opts.ChangedFrom}");
            changedModelIds = changed.ChangedModelIds;
        }

        var classified = FindingClassifier.Classify(load.Findings, baseline, changedModelIds);

        // `--touched-debt ignore` means treat it exactly as accepted debt: out of the gate AND out of
        // the listings. Every formatter already keys off AcceptedDebt to decide what not to list, so
        // folding the status here gives all six output formats the same behaviour. Matters most on a
        // library stored as one big file, where any edit touches every model and unfixed debt would
        // otherwise swamp the findings the change actually introduced.
        if (opts.TouchedDebt == TouchedDebtPolicy.Ignore)
        {
            var ignored = classified.Count(c => c.Status == FindingStatus.TouchedDebt);
            if (ignored > 0)
            {
                stderr.WriteLine(
                    $"note: --touched-debt ignore: {ignored} touched-debt finding(s) counted as accepted debt");
                classified = classified
                    .Select(c => c.Status == FindingStatus.TouchedDebt
                        ? c with { Status = FindingStatus.AcceptedDebt }
                        : c)
                    .ToList();
            }
        }

        var gateFailureCount = classified.Count(c => FailsGate(c, opts));

        // Fixed = baseline findings in a changed model that are no longer present (positive feedback).
        IReadOnlyList<BaselineEntry> fixedEntries = [];
        if (baseline is not null && changedModelIds is not null)
        {
            fixedEntries = baseline.StaleEntries(load.Findings)
                .Where(e => changedModelIds.Contains(e.Model))
                .ToList();
        }

        // Record before formatting/exit-code so the point still lands when the gate fails — a failing
        // build is exactly the one whose numbers you want on the trend.
        if (opts.RecordMetrics && load.Graph is not null && load.Models is not null)
        {
            MetricsRecorder.Record(
                opts.ResolvedMetricsPath, load.Graph, load.Models, load.Findings,
                DateTime.UtcNow, VcsLocator.Stamp(opts.LibraryPath), opts.MetricsForce, stderr);
        }

        var report = new CheckReport(
            opts.LibraryPath, load.ModelsChecked, classified, load.ModelToFile,
            baseline is not null, gateFailureCount, fixedEntries);

        IFindingFormatter formatter = opts.Format switch
        {
            OutputFormat.Json => new JsonFindingFormatter(),
            OutputFormat.JUnit => new JUnitFindingFormatter(),
            OutputFormat.Sarif => new SarifFindingFormatter(),
            OutputFormat.TeamCity => new TeamCityFindingFormatter(),
            OutputFormat.Markdown => new MarkdownFindingFormatter(),
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

        return report.GatePassed ? ExitCodes.Ok : ExitCodes.GateFailed;
    }

    private static bool FailsGate(ClassifiedFinding c, CheckOptions opts)
    {
        if (opts.FailOn == FailOnLevel.Off)
            return false;
        if ((int)c.Finding.Severity < (int)ThresholdFor(opts.FailOn))
            return false;

        return c.Status switch
        {
            FindingStatus.New => true,
            FindingStatus.TouchedDebt => opts.TouchedDebt == TouchedDebtPolicy.Fail,
            _ => false // AcceptedDebt never fails the gate
        };
    }

    private static RuleSeverity ThresholdFor(FailOnLevel level) => level switch
    {
        FailOnLevel.Warning => RuleSeverity.Warning,
        _ => RuleSeverity.Error
    };
}

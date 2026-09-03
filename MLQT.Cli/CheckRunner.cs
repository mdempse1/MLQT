using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>Runs `check`: load + findings, classify against a baseline, format, compute exit code.</summary>
internal static class CheckRunner
{
    public static async Task<int> RunAsync(CheckOptions opts, TextWriter stdout, TextWriter stderr)
    {
        // Checked before the library is loaded: a base that cannot work is a mistake in the
        // invocation, and finding out after several minutes of checking helps nobody.
        string? sarifBase = null;
        if (opts.SarifBasePath is not null)
        {
            if (!SarifBase.TryResolve(opts.LibraryPath, opts.SarifBasePath, out sarifBase, out var baseError))
            {
                stderr.WriteLine($"error: {baseError}");
                return ExitCodes.Error;
            }

            // Any SARIF output counts, including one asked for with --report.
            if (opts.Format != OutputFormat.Sarif && !opts.Reports.Any(r => r.Format == OutputFormat.Sarif))
                stderr.WriteLine("note: --sarif-base only affects SARIF output");
        }

        var load = await CheckPipeline.LoadAndCheckAsync(
            opts.LibraryPath, opts.ConfigPath, stderr,
            honorSuppressions: !opts.NoSuppress, dependencyPaths: opts.DependencyPaths,
            allowVersionMismatch: opts.AllowVersionMismatch,
            // Only when this run is going to report coverage: the check has the parse tree in hand, so
            // measuring here costs the measurement alone rather than a second pass over the library.
            collectCoverage: opts.RecordMetrics);
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

            // Classification is per finding, but the ledger is a set of entries, and several findings
            // can share one. Without the ledger's own size on screen, an accepted count larger than the
            // number `baseline create` reported writing reads as a miscount.
            stderr.WriteLine(
                $"note: baseline holds {Plural.Entries(baseline.Entries.Count)}; one entry can cover " +
                "several findings, so the accepted count below can be larger");
        }

        // Warn when the baseline was generated under different rules. Both failure modes are silent
        // otherwise: a rule enabled since reports its pre-existing findings as NEW, so a change looks
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
                        "         Pre-existing findings of a newly enabled rule are reported as new. " +
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
        if (opts.RecordMetrics && load.Graph is not null && load.Models is not null && load.Settings is not null)
        {
            MetricsRecorder.Record(
                opts.ResolvedMetricsPath, load.Graph, load.Models, load.Findings, load.Settings,
                DateTime.UtcNow, VcsLocator.Stamp(opts.LibraryPath), opts.MetricsForce, stderr);
        }

        var report = new CheckReport(
            opts.LibraryPath, load.ModelsChecked, classified, load.Locations,
            baseline is not null, gateFailureCount, fixedEntries, sarifBase);

        // Written to a file: colour codes would be in the file rather than on a terminal.
        var toTerminal = opts.OutPath is null;
        var output = Formatter(opts.Format, useColor: toTerminal && !opts.NoColor && !Console.IsOutputRedirected)
            .Format(report);

        if (opts.OutPath is not null)
        {
            if (!await TryWriteAsync(opts.OutPath, output, stderr))
                return ExitCodes.Error;
        }
        else
        {
            await stdout.WriteAsync(output);
            if (!output.EndsWith('\n'))
                await stdout.WriteLineAsync();
        }

        // The extra reports, formatted from the same run. Checking the library again to produce a
        // second format costs minutes on a large one, and the two runs can disagree if anything on
        // disk moved between them — which is exactly the moment a report is being trusted.
        foreach (var extra in opts.Reports)
        {
            var text = Formatter(extra.Format, useColor: false).Format(report);
            if (!await TryWriteAsync(extra.Path, text, stderr))
                return ExitCodes.Error;
        }

        return report.GatePassed ? ExitCodes.Ok : ExitCodes.GateFailed;
    }

    private static IFindingFormatter Formatter(OutputFormat format, bool useColor) => format switch
    {
        OutputFormat.Json => new JsonFindingFormatter(),
        OutputFormat.JUnit => new JUnitFindingFormatter(),
        OutputFormat.Sarif => new SarifFindingFormatter(),
        OutputFormat.TeamCity => new TeamCityFindingFormatter(),
        OutputFormat.Markdown => new MarkdownFindingFormatter(),
        _ => new ConsoleFindingFormatter(useColor)
    };

    /// <summary>
    /// Writes one report, saying which one failed if it does. A pipeline that asked for two files and
    /// silently got one would carry the gap into whatever reads them.
    /// </summary>
    private static async Task<bool> TryWriteAsync(string path, string text, TextWriter stderr)
    {
        try
        {
            await File.WriteAllTextAsync(path, text);
            return true;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: failed to write '{path}': {ex.Message}");
            return false;
        }
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

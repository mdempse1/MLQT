using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;

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

        // The line-level diff review comments have to land inside. Resolved before the library is
        // loaded, for the same reason --sarif-base is checked here: an invocation that cannot produce
        // what it asked for is a mistake to report now, not after several minutes of checking. It is
        // also a separate question from the model-level --changed-from above, and measured
        // differently - from the merge base rather than the ref itself. See ChangedLineResolver.
        ChangedLineResult? diff = null;
        if (WritesReview(opts))
        {
            if (opts.ChangedFrom is null)
            {
                stderr.WriteLine(
                    "error: review output needs --changed-from <base-ref>. A review comment has to sit " +
                    "on a line the change touched, and without a diff there is no such line");
                return ExitCodes.Error;
            }

            diff = ChangedLineResolver.Resolve(opts.LibraryPath, opts.ChangedFrom);
            if (!diff.Ok)
            {
                stderr.WriteLine($"error: {diff.Error}");
                return ExitCodes.Error;
            }

            var changedLines = diff.LinesByFile.Values.Sum(l => l.Count);
            stderr.WriteLine(
                $"note: review diff covers {changedLines} changed line(s) in " +
                $"{diff.LinesByFile.Count} file(s) since the merge base with {opts.ChangedFrom}");
        }

        // Read before the library is loaded, for the same reason as the two above: a baseline that is
        // missing or unreadable is a mistake in the invocation, and reporting it after several minutes
        // of checking helps nobody. Nothing in reading it depends on the check.
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

        var load = await CheckPipeline.LoadAndCheckAsync(
            opts.LibraryPath, opts.ConfigPath, stderr,
            honorSuppressions: !opts.NoSuppress, dependencyPaths: opts.DependencyPaths,
            allowVersionMismatch: opts.AllowVersionMismatch,
            // Only when this run is going to report or judge coverage: the check has the parse tree in
            // hand, so measuring here costs the measurement alone rather than a second pass.
            collectCoverage: opts.RecordMetrics || opts.Coverage.IsActive);
        if (!load.Ok)
            return load.ExitCode;

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

        // The figures for every checked model, computed at most once. Both the trend point for the
        // "all libraries" scope and the coverage gate ask exactly this question, and `--metrics`
        // alongside `--min-coverage` is the recipe the CI walk-through recommends.
        var wholeSet = load.Graph is not null && load.Models is not null && load.Settings is not null
                       && (opts.RecordMetrics || opts.Coverage.IsActive)
            ? MetricsCalculator.Compute(load.Graph, load.Models, _ => load.Settings)
            : null;

        // What the ratchet compares against, read BEFORE this run records anything.
        //
        // This is the whole defect the ordering here exists to prevent. `--metrics --coverage-ratchet`
        // is the invocation both cli.md and ci-quality-gate.md recommend, and with the read left where
        // it used to be — inside the gate, after the record — the point this run had just appended was
        // the "last recorded snapshot" the ratchet compared itself to. It therefore passed always, and
        // most emphatically in the one case it exists for: a drop appends the lower numbers and then
        // measures itself against them. Verified before and after: coverage falling from 100% to 33%
        // exited 0 with `--metrics` and 1 without it.
        var previousSnapshot = opts.Coverage.Ratchet ? LastWholeSetSnapshot(opts) : null;

        // Record before formatting/exit-code so the point still lands when the gate fails — a failing
        // build is exactly the one whose numbers you want on the trend.
        if (opts.RecordMetrics && load.Graph is not null && load.Models is not null && load.Settings is not null)
        {
            MetricsRecorder.Record(
                opts.ResolvedMetricsPath, load.Graph, load.Models, load.Findings, load.Settings,
                DateTime.UtcNow, VcsLocator.Stamp(opts.LibraryPath), opts.MetricsForce, stderr, wholeSet);
        }

        var coverageResults = EvaluateCoverageGate(opts, load, wholeSet, previousSnapshot, stderr);

        var report = new CheckReport(
            opts.LibraryPath, load.ModelsChecked, classified, load.Locations,
            baseline is not null, gateFailureCount, fixedEntries, sarifBase, coverageResults,
            opts.SarifIncludeAccepted, diff);

        if (WritesSarif(opts))
            ReportSarifConsequences(opts, report, stderr);

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

        return report.GatePassed && report.CoverageGatePassed ? ExitCodes.Ok : ExitCodes.GateFailed;
    }

    /// <summary>
    /// Judges the measured coverage against whatever the caller asked for, saying on stderr what
    /// failed and why. Null when no coverage gate was requested — the ordinary case, which must not
    /// pay for the measurement.
    ///
    /// <para>Separate from the findings gate on purpose. "Did this change introduce findings" and "is
    /// this library documented well enough" are different questions, and a team that has switched the
    /// first off with <c>--fail-on off</c> has not thereby agreed to lose the second.</para>
    /// </summary>
    /// <param name="metrics">The whole checked set's figures, computed once by the caller and shared
    /// with the trend point — see the call site.</param>
    /// <param name="previous">The last recorded whole-set snapshot, read by the caller <b>before</b>
    /// this run appended one of its own — see the call site for why that ordering is the point.</param>
    private static IReadOnlyList<CoverageGateResult>? EvaluateCoverageGate(
        CheckOptions opts, LoadResult load, LibraryMetrics? metrics, MetricsSnapshot? previous,
        TextWriter stderr)
    {
        if (!opts.Coverage.IsActive || metrics is null)
            return null;

        if (metrics.Coverage.Count == 0)
        {
            stderr.WriteLine(
                "warning: --min-coverage/--coverage-ratchet asked for, but this repository tracks no " +
                "coverage dimensions — every rule they follow is switched off, so there is nothing to gate on");
            return [];
        }

        foreach (var unmatched in opts.Coverage.UnmatchedDimensions(metrics))
            stderr.WriteLine(
                $"warning: --min-coverage names '{unmatched}', which this run does not measure — " +
                "its rule is switched off for this repository, so the requirement checks nothing");

        if (opts.Coverage.Ratchet && previous is null)
            stderr.WriteLine(
                $"note: --coverage-ratchet has nothing to compare against yet — no snapshot in " +
                $"{opts.ResolvedMetricsPath}. Record one with --metrics.");

        var results = opts.Coverage.Evaluate(metrics, previous);
        foreach (var failure in results.Where(r => !r.Passed))
            stderr.WriteLine($"error: coverage gate: {failure.Describe()}");

        if (results.Count > 0 && results.All(r => r.Passed))
            stderr.WriteLine($"note: coverage gate passed ({results.Count} requirement(s) met)");

        return results;
    }

    /// <summary>
    /// The most recent snapshot for the whole checked set.
    ///
    /// <para>The whole set, not a library: the history also holds a point per library, and a gate that
    /// silently compared against one library's numbers would be answering a different question. A
    /// snapshot written before <c>Scope</c> existed carries none, which reads as the whole set — which
    /// is what it was.</para>
    /// </summary>
    private static MetricsSnapshot? LastWholeSetSnapshot(CheckOptions opts) =>
        MetricsHistoryStore.Load(opts.ResolvedMetricsPath)
            .Where(s => string.IsNullOrEmpty(s.Scope))
            .LastOrDefault();

    private static bool WritesSarif(CheckOptions opts) =>
        opts.Format == OutputFormat.Sarif || opts.Reports.Any(r => r.Format == OutputFormat.Sarif);

    private static bool WritesReview(CheckOptions opts) =>
        opts.Format == OutputFormat.Review || opts.Reports.Any(r => r.Format == OutputFormat.Review);

    /// <summary>
    /// Says what SARIF will and will not carry, because both are silent otherwise.
    ///
    /// <para>Accepted debt is left out by default (GitHub has no way to show it as accepted), and a
    /// run that quietly reported fewer findings than the console did would read as a bug. GitHub also
    /// rejects a run of more than 25,000 results and displays only the first 5,000 — a library the
    /// size of MSL passes both thresholds without anything saying so, and the symptom is an empty
    /// Security tab.</para>
    /// </summary>
    private static void ReportSarifConsequences(CheckOptions opts, CheckReport report, TextWriter stderr)
    {
        var accepted = report.CountOfStatus(FindingStatus.AcceptedDebt);
        if (accepted > 0 && !opts.SarifIncludeAccepted)
            stderr.WriteLine(
                $"note: {accepted} accepted-debt finding(s) left out of the SARIF — GitHub has no way to " +
                "show them as accepted, so they would arrive as open alerts. --sarif-include-accepted keeps them");

        var reported = opts.SarifIncludeAccepted ? report.Findings.Count : report.Findings.Count - accepted;
        if (reported > 25_000)
            stderr.WriteLine(
                $"warning: the SARIF carries {reported} results; GitHub rejects an upload of more than " +
                "25,000 and displays only the first 5,000. Narrow the run, or use a baseline");
        else if (reported > 5_000)
            stderr.WriteLine(
                $"note: the SARIF carries {reported} results; GitHub displays the first 5,000 of a run");
    }

    private static IFindingFormatter Formatter(OutputFormat format, bool useColor) => format switch
    {
        OutputFormat.Json => new JsonFindingFormatter(),
        OutputFormat.JUnit => new JUnitFindingFormatter(),
        OutputFormat.Sarif => new SarifFindingFormatter(),
        OutputFormat.TeamCity => new TeamCityFindingFormatter(),
        OutputFormat.Markdown => new MarkdownFindingFormatter(),
        OutputFormat.Review => new ReviewFindingFormatter(),
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

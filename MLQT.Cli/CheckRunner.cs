using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Cli;

/// <summary>Runs `check`: load + findings, classify against a baseline, format, compute exit code.</summary>
internal static class CheckRunner
{
    public static async Task<int> RunAsync(CheckOptions opts, TextWriter stdout, TextWriter stderr)
    {
        var load = await CheckPipeline.LoadAndCheckAsync(opts.LibraryPath, opts.ConfigPath, stderr);
        if (!load.Ok)
            return load.ExitCode;

        Baseline? baseline = null;
        if (opts.BaselinePath is not null)
        {
            if (!File.Exists(opts.BaselinePath))
            {
                stderr.WriteLine($"error: baseline not found: {opts.BaselinePath}");
                return ExitCodes.Error;
            }
            try
            {
                baseline = Baseline.Load(opts.BaselinePath);
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"error: could not read baseline: {ex.Message}");
                return ExitCodes.Error;
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
            changedModelIds = changed.ChangedModelIds;
        }

        var classified = FindingClassifier.Classify(load.Findings, baseline, changedModelIds);
        var gateFailureCount = classified.Count(c => FailsGate(c, opts));
        var report = new CheckReport(
            opts.LibraryPath, load.ModelsChecked, classified, load.ModelToFile, baseline is not null, gateFailureCount);

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

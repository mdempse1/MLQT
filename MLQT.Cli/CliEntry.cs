namespace MLQT.Cli;

/// <summary>Top-level dispatch for the `mlqt` command. Kept separate from Program.cs so it is testable.</summary>
internal static class CliEntry
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return ExitCodes.Error;
        }

        switch (args[0])
        {
            case "-h" or "--help" or "help":
                stdout.WriteLine(Usage);
                return ExitCodes.Ok;

            case "--version":
                stdout.WriteLine("mlqt 0.1.0");
                return ExitCodes.Ok;

            case "check":
                if (!CheckOptions.TryParse(args[1..], out var opts, out var error))
                {
                    stderr.WriteLine($"error: {error}");
                    stderr.WriteLine(Usage);
                    return ExitCodes.Error;
                }
                return await CheckRunner.RunAsync(opts!, stdout, stderr);

            case "baseline":
                return await BaselineCommand.RunAsync(args[1..], stdout, stderr);

            default:
                stderr.WriteLine($"error: unknown command '{args[0]}'");
                stderr.WriteLine(Usage);
                return ExitCodes.Error;
        }
    }

    public const string Usage = """
        mlqt — Modelica library quality checks

        Usage:
          mlqt check <library-path> [options]
          mlqt baseline create|prune|update <library-path> [--baseline <path>] [--config <path>]
                                                           [--dependency <path>] [--force]

        check options:
          --config <path>               Settings file (default: <library-path>/.mlqt/settings.json)
          --baseline <path>             Classify findings against a baseline (new vs accepted debt)
          --touched-debt warn|fail|ignore  Existing debt in a changed model (default: warn)
          --format <fmt>                console|json|junit|sarif|teamcity|markdown (default: console)
          --out <file>                  Write output to a file instead of stdout
          --fail-on off|warning|error   Exit non-zero when findings reach this level (default: error)
          --no-color                    Disable coloured console output
          --no-suppress                 Ignore __MLQT suppression annotations (audit)
          --changed-from <ref>          VCS ref to diff against, for touched-debt escalation
          --dependency <path>           Load another library so references resolve (repeatable).
                                        Never reported on — use for MSL and other dependencies
          --metrics                     Record a coverage snapshot in <library-path>/.mlqt/metrics-history.json
          --metrics-out <path>          Record it somewhere else instead (implies --metrics)
          --metrics-force               Record even when the numbers are unchanged
          -h, --help                    Show this help

        baseline: create  snapshot the current findings as accepted debt (refuses to overwrite
                          an existing file without --force)
                  prune   drop entries whose findings are now fixed. Never accepts anything new,
                          so it can only ever shrink the baseline — the safe maintenance command
                  update  regenerate from the current findings: drops fixed entries AND accepts
                          any new ones as debt. Requires --force when it would accept something,
                          because that is how a violation gets past the gate unreviewed

                  Writes <library-path>/.mlqt/baseline.json unless --baseline <path> is given.
                  The file records when it was generated and the revision it describes.

        deps:     without --dependency, a reference into a library that is not loaded cannot resolve,
                  so inherited icons and modelica:// links report as findings. Pass the same
                  --dependency set to `baseline` as to `check`, or the two disagree — check warns
                  when the baseline recorded a dependency this run did not load.

        metrics:  --metrics appends a point to the history the desktop Coverage dashboard reads.
                  An unchanged point is skipped, so a CI job that commits the file cannot loop.

        Exit codes: 0 = passed, 1 = findings at/above --fail-on (new; touched debt if --touched-debt fail),
                    2 = usage/load error
        """;
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int GateFailed = 1;
    public const int Error = 2;
}

namespace MLQT.Cli;

/// <summary>Top-level dispatch for the `mlqt` command. Kept separate from Program.cs so it is testable.</summary>
internal static class CliEntry
{
    /// <summary>
    /// Runs one command, and answers with an exit code whatever happens.
    ///
    /// <para>The catch is load-bearing, not defensive tidiness. `cli.md` publishes a three-value exit
    /// contract that CI scripts branch on, and without this an unexpected exception — an analyzer that
    /// throws, a report file on a full disk, a settings file that deserializes into something
    /// surprising — left the process with a .NET unhandled-exception code and a stack trace on stderr.
    /// A build reading that sees neither "clean" nor "findings" nor "setup error", and the operator
    /// sees a crash where every other failure prints one `error:` line.</para>
    /// </summary>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            return await DispatchAsync(args, stdout, stderr);
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.GetType().Name}: {ex.Message}");
            stderr.WriteLine(
                "       This is a defect in mlqt, not a problem with the library. Please report it " +
                "with the command line you used; the detail below is what to include.");
            stderr.WriteLine(ex.ToString());
            return ExitCodes.Error;
        }
    }

    private static async Task<int> DispatchAsync(string[] args, TextWriter stdout, TextWriter stderr)
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
                stdout.WriteLine($"{ToolInfo.Name} {ToolInfo.Version}");
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

            case "compare":
                return await CompareCommand.RunAsync(args[1..], stdout, stderr);

            case "hook":
                return await HookCommand.RunAsync(args[1..], stdout, stderr);

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
          mlqt compare <library-a> <library-b> [--format console|json] [--out <file>] [--no-added]
          mlqt hook install|uninstall|status [<library-path>] [options]
          mlqt --version

        check options:
          --config <path>               Settings file (default: <library-path>/.mlqt/settings.json)
          --baseline <path>             Classify findings against a baseline (new vs accepted debt)
          --touched-debt warn|fail|ignore  Existing debt in a changed model (default: warn)
          --format <fmt>                console|json|junit|sarif|teamcity|markdown|review
                                        (default: console). `review` is a GitHub pull-request
                                        review body and needs --changed-from - see `review:` below
          --sarif-base <path>           Directory SARIF file paths are written relative to
                                        (default: the library). Set it to the repository root when
                                        the library is a subdirectory, or code-scanning annotations
                                        attach to nothing
          --sarif-include-accepted      Keep accepted debt in SARIF. Off by default: GitHub shows
                                        every result as an open alert, so accepted debt would be
                                        indistinguishable from new findings
          --out <file>                  Write output to a file instead of stdout
          --report <fmt>:<file>         Also write this format to this file (repeatable), so one
                                        run can produce a readable log and a machine-readable
                                        report instead of checking the library twice
          --fail-on off|warning|error   Exit non-zero when findings reach this level (default: error)
          --min-coverage <spec>         Fail when coverage is below a percentage. <spec> is a number
                                        for every tracked dimension, or <dimension>=<percent> for one
                                        (repeatable), e.g. --min-coverage 80
                                        --min-coverage class-description=95
          --coverage-ratchet            Fail when any dimension is below the last recorded snapshot
          --no-color                    Disable coloured console output
          --no-suppress                 Ignore __MLQT suppression annotations (audit)
          --changed-from <ref>          VCS ref to diff against, for touched-debt escalation
          --dependency <path>           Load another library so references resolve (repeatable).
                                        Never reported on — use for MSL and other dependencies
          --allow-version-mismatch      Check anyway when a loaded dependency is not the version
                                        the library's uses(...) declares (findings may not be real)
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
                          because that is how a finding gets past the gate unreviewed

                  Writes <library-path>/.mlqt/baseline.json unless --baseline <path> is given.
                  The file records when it was generated and the revision it describes.

        deps:     without --dependency, a reference into a library that is not loaded cannot resolve,
                  so inherited icons and modelica:// links report as findings. Pass the same
                  --dependency set to `baseline` as to `check`, or the two disagree — check warns
                  when the baseline recorded a dependency this run did not load. A loaded copy that
                  is NOT the version the library's uses(...) declares STOPS the run (exit 2): the
                  findings would not be real. Override with --allow-version-mismatch.

        review:   --format review writes the body of a GitHub pull-request review: a summary plus one
                  inline comment per changed line that has a finding on it. Post it with
                    gh api --method POST /repos/OWNER/REPO/pulls/N/reviews --input review.json
                  Needs --changed-from <base-ref>, and Git - a pull request is a Git-forge feature.
                  A finding NOT on a line the change added or rewrote goes in the summary instead:
                  GitHub rejects a comment placed outside the diff, and the rejection loses the
                  whole review, not just that comment. Always posted as a comment, never as
                  REQUEST_CHANGES - the exit code is the gate.

        metrics:  --metrics appends a point to the history the desktop app's Metrics tab reads.
                  An unchanged point is skipped, so a CI job that commits the file cannot loop.

        compare:  lists the classes <library-a> has that <library-b> does not — for checking that a
                  reformat, restructure or merge did not lose any. Classes are matched on their full
                  Modelica name only, so how they are laid out on disk is free to have changed.
                  Also lists the classes only <library-b> has (--no-added to suppress), because a
                  class that lost its within clause shows up as one missing name and one added name.
                  No settings are read and no rules are run.

                  --format console|json    Output format (default: console)
                  --out <file>             Write output to a file instead of stdout
                  --no-added               List only what is missing, not what was added

                  Exit codes: 0 = nothing missing, 1 = classes missing, 2 = usage/load error

        hook:     installs `mlqt check` as a git pre-commit hook, so findings are caught before
                  the commit rather than by CI afterwards. The library defaults to the current
                  directory; the repository is found by walking up from it. The hook skips any
                  commit that stages no .mo file, and `git commit --no-verify` bypasses it.
                  A pre-commit hook mlqt did not write is left alone unless --force.
                  Git only - SVN runs its hooks on the server.

                  --fail-on off|warning|error  What blocks the commit (default: error)
                  --baseline <path>            Classify against a baseline
                  --changed-from <ref>         Escalate debt in models changed since this ref
                  --dependency <path>          Load another library (repeatable)
                  --force                      Replace or delete a hook mlqt did not write

        Exit codes: 0 = passed, 1 = findings at/above --fail-on (new; touched debt if --touched-debt fail),
                    2 = usage/load/setup error (bad path, unreadable config, dependency version mismatch)
        """;
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int GateFailed = 1;
    public const int Error = 2;
}

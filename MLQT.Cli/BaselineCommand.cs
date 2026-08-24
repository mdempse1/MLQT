using MLQT.Services.Checking;

namespace MLQT.Cli;

internal sealed record BaselineOptions
{
    public required string LibraryPath { get; init; }
    public string? BaselinePath { get; init; }
    public string? ConfigPath { get; init; }
    public bool Force { get; init; }

    /// <summary>Extra libraries loaded so references resolve. A baseline must be generated with the
    /// same set the check uses, or the two disagree about what resolves — see the drift warning.</summary>
    public IReadOnlyList<string> DependencyPaths { get; init; } = [];

    /// <summary>Baseline anyway despite a dependency version mismatch. A baseline taken against the
    /// wrong versions bakes findings that are not real into the ledger, so this is off by default.</summary>
    public bool AllowVersionMismatch { get; init; }

    /// <summary>The baseline file to operate on — explicit (resolved against the library path when
    /// relative), or the default <c>&lt;lib&gt;/.mlqt/baseline.json</c>.</summary>
    public string ResolvedBaselinePath =>
        BaselinePath is not null
            ? RepoPath.Resolve(LibraryPath, BaselinePath)
            : Path.Combine(LibraryPath, ".mlqt", "baseline.json");

    public static bool TryParse(IReadOnlyList<string> args, out BaselineOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? path = null, baseline = null, config = null;
        var force = false;
        var dependencies = new List<string>();
        var allowVersionMismatch = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--baseline":
                    if (!Next(args, ref i, out baseline, out error)) return false;
                    break;
                case "--config":
                    if (!Next(args, ref i, out config, out error)) return false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--dependency":
                    if (!Next(args, ref i, out var dependency, out error)) return false;
                    dependencies.Add(dependency!);
                    break;
                case "--allow-version-mismatch":
                    allowVersionMismatch = true;
                    break;
                default:
                    if (arg.StartsWith('-')) { error = $"unknown option '{arg}'"; return false; }
                    if (path is not null) { error = $"unexpected argument '{arg}'"; return false; }
                    path = arg;
                    break;
            }
        }

        if (path is null) { error = "missing <library-path>"; return false; }

        options = new BaselineOptions
        {
            LibraryPath = path, BaselinePath = baseline, ConfigPath = config,
            Force = force, DependencyPaths = dependencies, AllowVersionMismatch = allowVersionMismatch
        };
        return true;
    }

    private static bool Next(IReadOnlyList<string> args, ref int i, out string? value, out string? error)
    {
        error = null;
        if (i + 1 >= args.Count)
        {
            value = null;
            error = $"option '{args[i]}' requires a value";
            return false;
        }
        value = args[++i];
        return true;
    }
}

/// <summary>The `baseline create|update|prune` command group.</summary>
internal static class BaselineCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("error: missing subcommand (create|update|prune)");
            return ExitCodes.Error;
        }

        var sub = args[0];
        if (sub is not ("create" or "update" or "prune"))
        {
            stderr.WriteLine($"error: unknown baseline subcommand '{sub}' (expected create|update|prune)");
            return ExitCodes.Error;
        }

        if (!BaselineOptions.TryParse(args[1..], out var opts, out var error))
        {
            stderr.WriteLine($"error: {error}");
            return ExitCodes.Error;
        }

        var load = await CheckPipeline.LoadAndCheckAsync(
            opts!.LibraryPath, opts.ConfigPath, stderr, dependencyPaths: opts.DependencyPaths,
            allowVersionMismatch: opts.AllowVersionMismatch);
        if (!load.Ok)
            return load.ExitCode;

        // Recorded in the file so a later check can warn when the rules have moved on — a rule
        // enabled after baselining reports its pre-existing violations as new.
        var settings = load.Settings;

        var path = opts.ResolvedBaselinePath;

        // Stamp the file with when it was generated and the revision it describes, so a reviewer can
        // tell how old the accepted debt is and diff from there. Absent outside a working copy.
        var now = DateTime.UtcNow;
        var stamp = VcsLocator.Stamp(opts.LibraryPath);
        if (stamp.IsKnown)
            stderr.WriteLine($"note: stamping baseline with revision {Short(stamp.Revision!)}" +
                             (stamp.Branch is not null ? $" on {stamp.Branch}" : ""));

        switch (sub)
        {
            case "create":
                if (File.Exists(path) && !opts.Force)
                {
                    stderr.WriteLine($"error: baseline already exists: {path} (use --force, or `baseline update`)");
                    return ExitCodes.Error;
                }
                var created = Baseline.FromFindings(load.Findings, now, stamp, settings, load.DependencyLibraries);
                created.Save(path);
                var createdCoverage = Baseline.CoverageOf(load.Findings);
                stdout.WriteLine(
                    $"Wrote {Plural.Entries(created.Entries.Count)} to {path}, " +
                    $"covering {createdCoverage.Baselineable} finding(s).");
                ExplainCoverage(createdCoverage, stderr);
                return ExitCodes.Ok;

            case "update":
            {
                // update is the only command that can GROW the baseline, and growing it means accepting
                // violations nobody has reviewed — the one way to defeat the ratchet by accident. So it
                // says what it is absorbing, and refuses to absorb anything unless asked to.
                var existing = File.Exists(path) ? Baseline.Load(path) : null;
                var updated = Baseline.FromFindings(load.Findings, now, stamp, settings, load.DependencyLibraries);
                var absorbed = CountMissing(updated, existing);
                var droppedByUpdate = CountMissing(existing, updated);

                if (absorbed > 0 && !opts.Force)
                {
                    stderr.WriteLine(
                        $"error: this would absorb {Plural.Entries(absorbed)} not in the baseline, " +
                        $"accepting {(absorbed == 1 ? "it" : "them")} as debt.");
                    stderr.WriteLine(
                        "       Re-run with --force if that is intended (e.g. you just enabled new rules).");
                    stderr.WriteLine(
                        "       To only drop findings you have fixed, without accepting anything new, " +
                        "use `baseline prune`.");
                    return ExitCodes.Error;
                }

                updated.Save(path);
                var updatedCoverage = Baseline.CoverageOf(load.Findings);
                stdout.WriteLine(
                    $"Updated {path}: {Plural.Entries(updated.Entries.Count)} " +
                    $"covering {updatedCoverage.Baselineable} finding(s) " +
                    $"— absorbed {Plural.Entries(absorbed)} as accepted debt, " +
                    $"dropped {Plural.Entries(droppedByUpdate)} now fixed");
                ExplainCoverage(updatedCoverage, stderr);
                return ExitCodes.Ok;
            }

            case "prune":
            {
                if (!File.Exists(path))
                {
                    stderr.WriteLine($"error: baseline not found: {path}");
                    return ExitCodes.Error;
                }
                var baseline = Baseline.Load(path);
                var stale = baseline.StaleEntries(load.Findings);
                var pruned = baseline.WithoutStale(load.Findings, now, stamp, settings, load.DependencyLibraries);
                pruned.Save(path);

                stdout.WriteLine(
                    $"Pruned {Plural.Entries(stale.Count)} now fixed from {path}; " +
                    $"{Plural.Entries(pruned.Entries.Count)} remaining");

                // Prune never accepts anything new — say so when there is something it left alone, so
                // the difference from `update` is visible at the point of use rather than only in docs.
                var stillFailing = CountMissing(Baseline.FromFindings(load.Findings), pruned);
                if (stillFailing > 0)
                    stdout.WriteLine(
                        $"{Plural.Entries(stillFailing)} {(stillFailing == 1 ? "is" : "are")} not in the " +
                        "baseline and will still fail the gate. Prune never accepts new debt; " +
                        "`baseline update --force` would.");
                return ExitCodes.Ok;
            }

            default:
                return ExitCodes.Ok;
        }
    }

    /// <summary>
    /// Accounts for the gap between the number of findings a check reports and the number of entries
    /// the baseline holds — silent otherwise, and the two numbers read as a contradiction. Said only
    /// when there is a gap to account for, and on stderr with the other notes so the count on stdout
    /// stays the machine-readable line.
    /// </summary>
    private static void ExplainCoverage(BaselineCoverage coverage, TextWriter stderr)
    {
        if (coverage.SharingAnEntry > 0)
            stderr.WriteLine(
                $"note: {coverage.SharingAnEntry} finding(s) share an entry with another. An entry is " +
                "one rule + class + element + detail with no line number, so repeats of the same " +
                "violation within a class collapse into one — a later check still reports and accepts " +
                "every finding individually, so its accepted count is the larger number.");

        if (coverage.ParseDiagnostics > 0)
            stderr.WriteLine(
                $"note: {coverage.ParseDiagnostics} parse diagnostic(s) were not recorded — a baseline " +
                "cannot accept code that does not parse, so they will be reported as new. Severity is " +
                "not a filter: errors are baselined like anything else.");
    }

    /// <summary>How many of <paramref name="candidate"/>'s entries are absent from <paramref name="other"/>.
    /// A null <paramref name="other"/> is an empty baseline (nothing recorded yet).</summary>
    private static int CountMissing(Baseline? candidate, Baseline? other)
    {
        if (candidate is null)
            return 0;
        var known = (other?.Entries ?? []).Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
        return candidate.Entries.Count(e => !known.Contains(e.Fingerprint));
    }

    /// <summary>Abbreviates a Git SHA for a log line; SVN revision numbers are short already.</summary>
    private static string Short(string revision) => revision.Length > 12 ? revision[..12] : revision;
}

namespace MLQT.Cli;

internal enum OutputFormat { Console, Json, JUnit, Sarif, TeamCity, Markdown }

internal enum FailOnLevel { Off, Warning, Error }

internal enum TouchedDebtPolicy { Warn, Fail, Ignore }

/// <summary>
/// One extra report to write alongside the primary output: a format and where to put it. A pipeline
/// usually wants two — something a person reads and something a machine does — and producing them
/// from one run is not just faster on a large library, it is the only way they are guaranteed to
/// describe the same code.
/// </summary>
internal sealed record ReportOutput(OutputFormat Format, string Path);

/// <summary>Parsed options for the `check` command.</summary>
internal sealed record CheckOptions
{
    public required string LibraryPath { get; init; }
    public string? ConfigPath { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Console;
    public string? OutPath { get; init; }
    public FailOnLevel FailOn { get; init; } = FailOnLevel.Error;
    public bool NoColor { get; init; }

    /// <summary>Baseline file to classify findings against (new vs accepted debt). Null = no baseline.</summary>
    public string? BaselinePath { get; init; }

    /// <summary>
    /// Directory the file paths in SARIF output are written relative to. Null = the library itself,
    /// which is right only when the library is the repository — see <see cref="SarifBase"/>.
    /// </summary>
    public string? SarifBasePath { get; init; }

    /// <summary>
    /// Extra reports to write to files, on top of the primary output. Empty by default: one run, one
    /// report, exactly as before.
    /// </summary>
    public IReadOnlyList<ReportOutput> Reports { get; init; } = [];

    /// <summary>How to treat pre-existing (baseline) findings in a model the change touched.</summary>
    public TouchedDebtPolicy TouchedDebt { get; init; } = TouchedDebtPolicy.Warn;

    /// <summary>VCS ref to diff against for changed-model detection (touched-debt). Null = disabled.</summary>
    public string? ChangedFrom { get; init; }

    /// <summary>Audit mode: ignore __MLQT suppression annotations and report everything.</summary>
    public bool NoSuppress { get; init; }

    /// <summary>
    /// Extra library paths loaded so references resolve — the Modelica Standard Library and anything
    /// else the library under check depends on. They are never reported on: rules like "class has an
    /// icon" need to see <c>Modelica.Icons.*</c> to know the icon is inherited, but MSL's own findings
    /// are not your problem.
    /// </summary>
    public IReadOnlyList<string> DependencyPaths { get; init; } = [];

    /// <summary>
    /// Check anyway when a loaded dependency is not the version the library declares. Off by default:
    /// the findings such a run produces are not reliable, so stopping is the honest outcome. The
    /// escape hatch exists because a <c>conversion(noneFromVersion=...)</c> annotation can make a
    /// difference legitimate, and MLQT does not read those.
    /// </summary>
    public bool AllowVersionMismatch { get; init; }

    /// <summary>Record a coverage snapshot into the metrics history the desktop dashboard reads.</summary>
    public bool RecordMetrics { get; init; }

    /// <summary>Where to record it. Null = <c>&lt;library-path&gt;/.mlqt/metrics-history.json</c>, the
    /// shared, version-controllable file the desktop app uses. Point this outside the repository to
    /// collect the history as a CI artifact instead of committing it.</summary>
    public string? MetricsPath { get; init; }

    /// <summary>Record even when the numbers are unchanged. Off by default — see
    /// <c>MetricsHistoryStore.AppendIfChanged</c> for why that matters in CI.</summary>
    public bool MetricsForce { get; init; }

    /// <summary>The metrics file this run would write to.</summary>
    public string ResolvedMetricsPath =>
        MetricsPath is not null
            ? RepoPath.Resolve(LibraryPath, MetricsPath)
            : Path.Combine(LibraryPath, ".mlqt", "metrics-history.json");

    public static bool TryParse(IReadOnlyList<string> args, out CheckOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? path = null, config = null, outPath = null, baseline = null, changedFrom = null;
        string? sarifBase = null;
        var reports = new List<ReportOutput>();
        var format = OutputFormat.Console;
        var failOn = FailOnLevel.Error;
        var touchedDebt = TouchedDebtPolicy.Warn;
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        var noSuppress = false;
        string? metricsPath = null;
        var recordMetrics = false;
        var metricsForce = false;
        var dependencies = new List<string>();
        var allowVersionMismatch = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--config":
                    if (!Next(args, ref i, out config, out error)) return false;
                    break;
                case "--out":
                    if (!Next(args, ref i, out outPath, out error)) return false;
                    break;
                case "--baseline":
                    if (!Next(args, ref i, out baseline, out error)) return false;
                    break;
                case "--sarif-base":
                    if (!Next(args, ref i, out sarifBase, out error)) return false;
                    break;
                case "--report":
                    if (!Next(args, ref i, out var report, out error)) return false;
                    if (!TryParseReport(report!, out var parsedReport, out error)) return false;
                    if (reports.Any(r => string.Equals(r.Path, parsedReport!.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Two formats into one file: the second would overwrite the first, and the
                        // pipeline would carry on believing it had both.
                        error = $"--report writes '{parsedReport!.Path}' more than once";
                        return false;
                    }
                    reports.Add(parsedReport!);
                    break;
                case "--no-color":
                    noColor = true;
                    break;
                case "--no-suppress":
                    noSuppress = true;
                    break;
                case "--dependency":
                    if (!Next(args, ref i, out var dependency, out error)) return false;
                    dependencies.Add(dependency!);
                    break;
                case "--allow-version-mismatch":
                    allowVersionMismatch = true;
                    break;
                case "--metrics":
                    recordMetrics = true;
                    break;
                case "--metrics-out":
                    if (!Next(args, ref i, out metricsPath, out error)) return false;
                    recordMetrics = true;   // naming a destination implies recording
                    break;
                case "--metrics-force":
                    metricsForce = true;
                    recordMetrics = true;
                    break;
                case "--format":
                    if (!Next(args, ref i, out var fmt, out error)) return false;
                    if (!TryParseFormat(fmt!, out format))
                    {
                        error = $"invalid --format '{fmt}' (expected console|json|junit|sarif|teamcity|markdown)";
                        return false;
                    }
                    break;
                case "--fail-on":
                    if (!Next(args, ref i, out var lvl, out error)) return false;
                    if (!TryParseFailOn(lvl!, out failOn))
                    {
                        error = $"invalid --fail-on '{lvl}' (expected off|warning|error)";
                        return false;
                    }
                    break;
                case "--touched-debt":
                    if (!Next(args, ref i, out var td, out error)) return false;
                    if (!TryParseTouchedDebt(td!, out touchedDebt))
                    {
                        error = $"invalid --touched-debt '{td}' (expected warn|fail|ignore)";
                        return false;
                    }
                    break;
                case "--changed-from":
                    if (!Next(args, ref i, out changedFrom, out error)) return false;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"unknown option '{arg}'";
                        return false;
                    }
                    if (path is not null)
                    {
                        error = $"unexpected argument '{arg}'";
                        return false;
                    }
                    path = arg;
                    break;
            }
        }

        if (path is null)
        {
            error = "missing <library-path>";
            return false;
        }

        options = new CheckOptions
        {
            LibraryPath = path,
            ConfigPath = config,
            Format = format,
            OutPath = outPath,
            FailOn = failOn,
            NoColor = noColor,
            BaselinePath = baseline,
            SarifBasePath = sarifBase,
            Reports = reports,
            TouchedDebt = touchedDebt,
            ChangedFrom = changedFrom,
            NoSuppress = noSuppress,
            RecordMetrics = recordMetrics,
            MetricsPath = metricsPath,
            MetricsForce = metricsForce,
            DependencyPaths = dependencies,
            AllowVersionMismatch = allowVersionMismatch
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

    /// <summary>
    /// Parses <c>&lt;format&gt;:&lt;path&gt;</c>. Split at the first colon only, so a Windows path
    /// keeps its drive letter.
    /// </summary>
    private static bool TryParseReport(string value, out ReportOutput? report, out string? error)
    {
        report = null;
        error = null;

        var separator = value.IndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            error = $"invalid --report '{value}' (expected <format>:<path>, e.g. junit:results.xml)";
            return false;
        }

        var formatText = value[..separator];
        var path = value[(separator + 1)..].Trim();
        if (path.Length == 0)
        {
            error = $"invalid --report '{value}' (expected <format>:<path>, e.g. junit:results.xml)";
            return false;
        }

        if (!TryParseFormat(formatText, out var format))
        {
            error = $"invalid --report format '{formatText}' (expected console|json|junit|sarif|teamcity|markdown)";
            return false;
        }

        report = new ReportOutput(format, path);
        return true;
    }

    private static bool TryParseFormat(string s, out OutputFormat format)
    {
        switch (s.ToLowerInvariant())
        {
            case "console": format = OutputFormat.Console; return true;
            case "json": format = OutputFormat.Json; return true;
            case "junit": format = OutputFormat.JUnit; return true;
            case "sarif": format = OutputFormat.Sarif; return true;
            case "teamcity": format = OutputFormat.TeamCity; return true;
            case "markdown": format = OutputFormat.Markdown; return true;
            default: format = OutputFormat.Console; return false;
        }
    }

    private static bool TryParseFailOn(string s, out FailOnLevel level)
    {
        switch (s.ToLowerInvariant())
        {
            case "off": level = FailOnLevel.Off; return true;
            case "warning": level = FailOnLevel.Warning; return true;
            case "error": level = FailOnLevel.Error; return true;
            default: level = FailOnLevel.Error; return false;
        }
    }

    private static bool TryParseTouchedDebt(string s, out TouchedDebtPolicy policy)
    {
        switch (s.ToLowerInvariant())
        {
            case "warn": policy = TouchedDebtPolicy.Warn; return true;
            case "fail": policy = TouchedDebtPolicy.Fail; return true;
            case "ignore": policy = TouchedDebtPolicy.Ignore; return true;
            default: policy = TouchedDebtPolicy.Warn; return false;
        }
    }
}

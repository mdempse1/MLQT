namespace MLQT.Cli;

internal enum OutputFormat { Console, Json, JUnit, Sarif, TeamCity, Markdown }

internal enum FailOnLevel { Off, Warning, Error }

internal enum TouchedDebtPolicy { Warn, Fail, Ignore }

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

    /// <summary>How to treat pre-existing (baseline) findings in a model the change touched.</summary>
    public TouchedDebtPolicy TouchedDebt { get; init; } = TouchedDebtPolicy.Warn;

    /// <summary>VCS ref to diff against for changed-model detection (touched-debt). Null = disabled.</summary>
    public string? ChangedFrom { get; init; }

    /// <summary>Audit mode: ignore __MLQT suppression annotations and report everything.</summary>
    public bool NoSuppress { get; init; }

    public static bool TryParse(IReadOnlyList<string> args, out CheckOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? path = null, config = null, outPath = null, baseline = null, changedFrom = null;
        var format = OutputFormat.Console;
        var failOn = FailOnLevel.Error;
        var touchedDebt = TouchedDebtPolicy.Warn;
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;
        var noSuppress = false;

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
                case "--no-color":
                    noColor = true;
                    break;
                case "--no-suppress":
                    noSuppress = true;
                    break;
                case "--format":
                    if (!Next(args, ref i, out var fmt, out error)) return false;
                    if (!TryParseFormat(fmt!, out format))
                    {
                        error = $"invalid --format '{fmt}' (expected console|json|junit)";
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
            TouchedDebt = touchedDebt,
            ChangedFrom = changedFrom,
            NoSuppress = noSuppress
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

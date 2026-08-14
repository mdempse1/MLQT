namespace MLQT.Cli;

internal enum OutputFormat { Console, Json, JUnit }

internal enum FailOnLevel { Off, Warning, Error }

/// <summary>Parsed options for the `check` command.</summary>
internal sealed record CheckOptions
{
    public required string LibraryPath { get; init; }
    public string? ConfigPath { get; init; }
    public OutputFormat Format { get; init; } = OutputFormat.Console;
    public string? OutPath { get; init; }
    public FailOnLevel FailOn { get; init; } = FailOnLevel.Error;
    public bool NoColor { get; init; }

    public static bool TryParse(IReadOnlyList<string> args, out CheckOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? path = null, config = null, outPath = null;
        var format = OutputFormat.Console;
        var failOn = FailOnLevel.Error;
        var noColor = Environment.GetEnvironmentVariable("NO_COLOR") is not null;

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
                case "--no-color":
                    noColor = true;
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
                case "--baseline":
                case "--changed-from":
                    error = $"{arg} is not supported yet (planned for a later release)";
                    return false;
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
            NoColor = noColor
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
}

namespace MLQT.Cli;

/// <summary>Parsed command line for `mlqt compare`.</summary>
internal sealed record CompareOptions
{
    /// <summary>The library the classes are expected to be in.</summary>
    public required string LeftPath { get; init; }

    /// <summary>The library being checked for losses against <see cref="LeftPath"/>.</summary>
    public required string RightPath { get; init; }

    public OutputFormat Format { get; init; } = OutputFormat.Console;

    public string? OutPath { get; init; }

    /// <summary>
    /// Also list the classes only the second library has. On by default: after a restructure a class
    /// that lost its <c>within</c> clause shows up as one missing name and one added name, and seeing
    /// only half of that pair is what makes it look like a deletion.
    /// </summary>
    public bool ShowAdded { get; init; } = true;

    public static bool TryParse(IReadOnlyList<string> args, out CompareOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? left = null, right = null, outPath = null;
        var format = OutputFormat.Console;
        var showAdded = true;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--format":
                    if (!Next(args, ref i, out var formatName, out error)) return false;
                    switch (formatName!.ToLowerInvariant())
                    {
                        case "console": format = OutputFormat.Console; break;
                        case "json": format = OutputFormat.Json; break;
                        default:
                            error = $"unknown format '{formatName}' (expected console|json)";
                            return false;
                    }
                    break;

                case "--out":
                    if (!Next(args, ref i, out outPath, out error)) return false;
                    break;

                case "--no-added":
                    showAdded = false;
                    break;

                default:
                    if (arg.StartsWith('-')) { error = $"unknown option '{arg}'"; return false; }
                    if (left is null) left = arg;
                    else if (right is null) right = arg;
                    else { error = $"unexpected argument '{arg}'"; return false; }
                    break;
            }
        }

        if (left is null) { error = "missing <library-a> and <library-b>"; return false; }
        if (right is null) { error = "missing <library-b>"; return false; }

        options = new CompareOptions
        {
            LeftPath = left,
            RightPath = right,
            Format = format,
            OutPath = outPath,
            ShowAdded = showAdded
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
